using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// A lit window, glowing like the lamp heads (maplamp:pCube1): their emissive material, made
// warmer and softer, in the window's glass, picked up by bloom, its dark bars and stripes left
// as they are, and a soft shadowless spot light on the glass shining out onto whatever is in front. Every window named magHW..., alHW... or
// chudHW... (the magician's, the alchemist's and Al Cachud's houses) and glowWindow1/2 gets one
// on every scene load; nothing to place by hand. glowWindow1/2's WindowGlow is switched off so
// all the windows look alike.
[DisallowMultipleComponent]
public class WindowLamp : MonoBehaviour
{
    public static readonly string[] Prefixes = { "magHW", "alHW", "chudHW" };
    public static readonly string[] Names = { "glowWindow1", "glowWindow2" };
    public const string LampMaterialName = "LampHeadGlow";

    // The lamp heads' material, for when no lamp head is about to borrow it from.
    private static readonly Color FallbackBase = new Color(1f, 0.86f, 0.55f);
    private static readonly Color FallbackEmission = new Color(2.7f, 2.23f, 1f);
    // The windows' glass: warmer and about half as bright as the lamp heads (emission 2.7, 2.23,
    // 1), as a whole pane at their brightness stands out too hard against the dark walls.
    private static readonly Color GlowBase = new Color(1f, 0.72f, 0.4f);
    private static readonly Color GlowEmission = new Color(1.35f, 0.78f, 0.3f);
    private static readonly Color LightColor = new Color(1f, 0.68f, 0.3f);
    private const float LightIntensity = 10f;
    private const float LightRange = 10f;
    private const float SpotAngle = 130f;
    // Downward lean of the light, metres per metre out.
    private const float Droop = 0.2f;

    // A slot is glass when its colour is at least this share as light as the window's lightest,
    // and lighter than MinGlassLightness: the dark bars between the panes are not.
    private const float GlassShare = 0.6f;
    private const float MinGlassLightness = 0.08f;

    private static readonly Dictionary<Material, Material> Patterned = new Dictionary<Material, Material>();
    private static Material lampMaterial;
    private static Material fallback;
    private static Material glassGlow;

    private Renderer window;
    private Material[] originalMaterials;
    private GameObject lightObject;

    public Light Glow { get; private set; }
    public Vector3 Outward { get; private set; }
    // Centre of the outside face of the glass.
    public Vector3 FaceCentre { get; private set; }
    // The material slots that glow: the window's own, not the house's frame or walls.
    public bool[] GlowingSlots { get; private set; }

    public static bool IsWindow(string objectName)
    {
        foreach (string prefix in Prefixes)
            if (objectName.StartsWith(prefix, System.StringComparison.Ordinal)) return true;
        return System.Array.IndexOf(Names, objectName) >= 0;
    }

    // Every load, including the pause menu's restart. Unsubscribing first keeps play mode
    // without domain reload to one handler.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Install(scene);

    // Also after the first scene loads, in case sceneLoaded fired before Register.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap() => Install(SceneManager.GetActiveScene());

    // Lights every window in the scene that hasn't got its lamp yet; returns how many it found.
    // Runs before the windows' Start, so a WindowGlow switched off here never builds.
    public static int Install(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded) return 0;
        int count = 0;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>())
            {
                if (!IsWindow(renderer.name)) continue;
                count++;
                if (renderer.TryGetComponent(out WindowGlow rays)) rays.enabled = false;
                if (renderer.GetComponent<WindowLamp>() == null) renderer.gameObject.AddComponent<WindowLamp>();
            }
        }
        return count;
    }

    private void Start()
    {
        Build();
    }

    private void OnEnable()
    {
        if (lightObject != null) Show(true);
    }

    private void OnDisable()
    {
        if (lightObject != null) Show(false);
    }

    private void OnDestroy()
    {
        if (window != null && originalMaterials != null) window.sharedMaterials = originalMaterials;
        Discard(lightObject);
    }

    private void Show(bool lit)
    {
        lightObject.SetActive(lit);
        if (window == null || originalMaterials == null) return;
        var materials = (Material[])originalMaterials.Clone();
        if (lit)
        {
            for (int i = 0; i < materials.Length; i++) if (GlowingSlots[i]) materials[i] = GlowFor(materials[i]);
        }
        window.sharedMaterials = materials;
    }

    private void Build()
    {
        if (lightObject != null) return;
        window = GetComponent<Renderer>();
        originalMaterials = window != null ? window.sharedMaterials : new Material[0];
        GlowingSlots = GlassSlots(originalMaterials);

        MeshFilter filter = GetComponent<MeshFilter>();
        Bounds local = filter != null && filter.sharedMesh != null ? filter.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one);
        // The box's three edges in world space; the shortest is the glass's thickness.
        var axes = new[]
        {
            transform.TransformVector(new Vector3(local.size.x, 0f, 0f)),
            transform.TransformVector(new Vector3(0f, local.size.y, 0f)),
            transform.TransformVector(new Vector3(0f, 0f, local.size.z)),
        };
        int thin = 0;
        for (int i = 1; i < 3; i++) if (axes[i].sqrMagnitude < axes[thin].sqrMagnitude) thin = i;
        Vector3 centre = transform.TransformPoint(local.center);
        Vector3 outward = Vector3.ProjectOnPlane(axes[thin], Vector3.up).normalized;
        if (outward.sqrMagnitude < 0.5f) outward = axes[thin].normalized;
        // Of its two faces, the one away from the rest of the house is the outside.
        if (Vector3.Dot(outward, centre - HouseCentre(centre)) < 0f) outward = -outward;
        Outward = outward;
        FaceCentre = centre + outward * (axes[thin].magnitude * 0.5f);

        // On its own root object, not under the window, whose uneven scale would skew it. Just
        // outside the glass, so the window's collider doesn't hide it from SpriteLightTint.
        lightObject = new GameObject(name + " Light");
        SceneManager.MoveGameObjectToScene(lightObject, gameObject.scene);
        lightObject.transform.SetPositionAndRotation(FaceCentre + outward * 0.05f,
            Quaternion.LookRotation(outward + Vector3.down * Droop, Vector3.up));
        Glow = lightObject.AddComponent<Light>();
        Glow.type = LightType.Spot;
        Glow.color = LightColor;
        Glow.intensity = LightIntensity;
        Glow.range = LightRange;
        Glow.spotAngle = SpotAngle;
        Glow.innerSpotAngle = SpotAngle * 0.45f;
        Glow.shadows = LightShadows.None;
        Show(isActiveAndEnabled);
    }

    // The glass: the window's light-coloured slots. The dark ones (its bars, stripes and supports)
    // stay as they are, the way the lamp heads keep their black stripes, and so does any slot the
    // rest of the house also uses (frames, walls). A window of one material glows whole.
    private bool[] GlassSlots(Material[] materials)
    {
        var house = new HashSet<Material>();
        if (transform.parent != null)
        {
            foreach (Renderer part in transform.parent.GetComponentsInChildren<Renderer>(true))
            {
                if (part.transform == transform || IsWindow(part.name)) continue;
                house.UnionWith(part.sharedMaterials);
            }
        }
        var lightness = new float[materials.Length];
        float brightest = 0f;
        int brightestSlot = -1;
        for (int i = 0; i < materials.Length; i++)
        {
            lightness[i] = materials[i] == null || house.Contains(materials[i]) ? -1f : Lightness(materials[i]);
            if (lightness[i] > brightest)
            {
                brightest = lightness[i];
                brightestSlot = i;
            }
        }
        var glass = new bool[materials.Length];
        bool any = false;
        for (int i = 0; i < materials.Length; i++)
        {
            glass[i] = lightness[i] > MinGlassLightness && lightness[i] >= brightest * GlassShare;
            any |= glass[i];
        }
        if (materials.Length == 1) glass[0] = true;
        else if (!any)
        {
            // Dark glass, or every slot the house's too: the lightest of them is the glass.
            if (brightestSlot < 0)
            {
                brightest = -1f;
                for (int i = 0; i < materials.Length; i++)
                {
                    float candidate = materials[i] != null ? Lightness(materials[i]) : -1f;
                    if (candidate > brightest)
                    {
                        brightest = candidate;
                        brightestSlot = i;
                    }
                }
            }
            if (brightestSlot >= 0) glass[brightestSlot] = true;
        }
        return glass;
    }

    // How light a material's colour is, 0 (black) to 1 (white).
    private static float Lightness(Material material)
    {
        Color color = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") :
            material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
        return color.r * 0.2126f + color.g * 0.7152f + color.b * 0.0722f;
    }

    // The glow for one glass material: the windows' shared glow, or, for glass painted with a
    // texture, a copy of it that takes the texture as its base and emission map, so painted-on
    // stripes stay dark too.
    private static Material GlowFor(Material source)
    {
        Material shared = GlassGlow();
        Texture pattern = source != null && source.HasProperty("_BaseMap") ? source.GetTexture("_BaseMap") : null;
        if (pattern == null) return shared;
        if (Patterned.TryGetValue(source, out Material glow) && glow != null) return glow;
        glow = new Material(shared) { name = shared.name + " (" + source.name + ")", hideFlags = HideFlags.DontSave };
        glow.SetTexture("_BaseMap", pattern);
        glow.SetTextureScale("_BaseMap", source.GetTextureScale("_BaseMap"));
        glow.SetTextureOffset("_BaseMap", source.GetTextureOffset("_BaseMap"));
        if (glow.HasProperty("_EmissionMap")) glow.SetTexture("_EmissionMap", pattern);
        Patterned[source] = glow;
        return glow;
    }

    // Centre of everything else the window's parent (the house) renders.
    private Vector3 HouseCentre(Vector3 fallbackCentre)
    {
        if (transform.parent == null) return fallbackCentre - transform.forward;
        bool found = false;
        var bounds = new Bounds();
        foreach (Renderer part in transform.parent.GetComponentsInChildren<Renderer>(true))
        {
            if (part.transform == transform || IsWindow(part.name)) continue;
            if (!found) bounds = part.bounds; else bounds.Encapsulate(part.bounds);
            found = true;
        }
        return found ? bounds.center : fallbackCentre - transform.forward;
    }

    // The windows' glass glow: the lamp heads' material with a warmer, softer colour.
    public static Material GlassGlow()
    {
        if (glassGlow != null) return glassGlow;
        glassGlow = new Material(LampMaterial()) { name = "Window Glow", hideFlags = HideFlags.DontSave };
        if (glassGlow.HasProperty("_BaseColor")) glassGlow.SetColor("_BaseColor", GlowBase);
        if (glassGlow.HasProperty("_Color")) glassGlow.SetColor("_Color", GlowBase);
        glassGlow.EnableKeyword("_EMISSION");
        glassGlow.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        glassGlow.SetColor("_EmissionColor", GlowEmission);
        return glassGlow;
    }

    // The lamp heads' own glow material (LampHeadGlow, set on them when the map imports), or a
    // stand-in with its colours when no lamp head is loaded.
    public static Material LampMaterial()
    {
        if (lampMaterial != null) return lampMaterial;
        foreach (Renderer renderer in FindObjectsByType<Renderer>())
        {
            foreach (Material material in renderer.sharedMaterials)
            {
                if (material != null && material.name == LampMaterialName) return lampMaterial = material;
            }
        }
        if (fallback == null)
        {
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            fallback = new Material(lit != null ? lit : Shader.Find("Standard"))
            {
                name = LampMaterialName + " (stand-in)",
                hideFlags = HideFlags.DontSave,
                color = FallbackBase,
            };
            fallback.EnableKeyword("_EMISSION");
            fallback.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            fallback.SetColor("_EmissionColor", FallbackEmission);
        }
        return fallback;
    }

    private static void Discard(Object target)
    {
        if (target == null) return;
        if (Application.isPlaying) Destroy(target);
        else DestroyImmediate(target);
    }
}
