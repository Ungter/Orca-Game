using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// A lit window, glowing like the lamp heads (maplamp:pCube1): their emissive material, made
// warmer and softer, in the window's glass, picked up by bloom, its dark bars and stripes left
// as they are. A shadowless spot on the glass shines out onto the ground in front, and a warm
// glow spills onto the wall round the window (an additive card, Orca/Window Spill), so the light
// shows on the house even where its walls are too dark to light. Every window named magHW..., alHW... or
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
    // The windows' glass: a deep candle orange at well under half the lamp heads' brightness
    // (emission 2.8, 1.85, 0.84). Paler and brighter, the grade's ACES and desaturation washed the
    // panes out to near white; a whole pane at the lamps' brightness is blinding anyway.
    private static readonly Color GlowBase = new Color(1f, 0.58f, 0.24f);
    private static readonly Color GlowEmission = new Color(1.1f, 0.48f, 0.13f);
    private static readonly Color LightColor = new Color(1f, 0.6f, 0.24f);
    // Matte glass, so it doesn't pick up a white sheen of reflected sky.
    private const float GlassSmoothness = 0.15f;
    private const float LightIntensity = 5f;
    private const float LightRange = 10f;
    private const float SpotAngle = 130f;
    // Downward lean of the light, metres per metre out.
    private const float Droop = 0.2f;

    // The spot points away from the wall, so on its own the wall stayed black round a bright
    // pane, and a real light in front of the glass only threw a hot specular spot off the bars.
    // The spill card covers the window plus SpillMargin x its smaller side all round, a little
    // out from the glass, and adds the light's colour fading from the window's edge outwards.
    public const string SpillShaderName = "Orca/Window Spill";
    public const float SpillMargin = 0.4f;
    public const float SpillIntensity = 0.12f;
    private const string SpillShaderPath = "WindowSpill/WindowSpill";
    // Out from the glass, as a share of its smaller side, so it never fights the wall for depth.
    private const float SpillLift = 0.02f;

    // Round windows (the glass drums alHWRound...) have no flat pane to shine out of: a drum is a
    // window whose two level sides are within DrumRoundness of each other and which stands at least
    // DrumHeightShare as tall as it is wide. It gets a point light in the middle, shining out all
    // round (shadowless, so the glass doesn't stop it), as bright DrumLightReach m beyond the glass
    // as a pane's spot is that far in front of it, and a ring of spill glow above and below it.
    public const float DrumRoundness = 0.75f;
    public const float DrumHeightShare = 0.25f;
    private const float DrumLightReach = 3f;
    private const int DrumSegments = 48;

    // A slot is glass when its colour is at least this share as light as the window's lightest,
    // and lighter than MinGlassLightness: the dark bars between the panes are not.
    private const float GlassShare = 0.6f;
    private const float MinGlassLightness = 0.08f;

    private static readonly Dictionary<Material, Material> Patterned = new Dictionary<Material, Material>();
    private static Material lampMaterial;
    private static Material fallback;
    private static Material glassGlow;
    private static Material spillMaterial;

    private Renderer window;
    private Material[] originalMaterials;
    private GameObject lightObject;
    private Mesh spillMesh;

    public Light Glow { get; private set; }
    // The glow spilling onto the wall round the window.
    public MeshRenderer Spill { get; private set; }
    // Out of the glass; zero for a round window, which shines out all round.
    public Vector3 Outward { get; private set; }
    // Centre of the outside face of the glass; a round window's middle.
    public Vector3 FaceCentre { get; private set; }
    public bool IsRound { get; private set; }
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
        Discard(spillMesh);
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
        IsRound = IsRoundWindow(axes);
        if (IsRound)
        {
            BuildDrum(centre, axes);
            Show(isActiveAndEnabled);
            return;
        }
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

        BuildSpill(axes, thin, outward);
        Show(isActiveAndEnabled);
    }

    // A drum: its two level sides match and it has some height. axes are its box's world edges.
    public static bool IsRoundWindow(Vector3[] axes)
    {
        int vertical = VerticalAxis(axes);
        float a = axes[(vertical + 1) % 3].magnitude, b = axes[(vertical + 2) % 3].magnitude;
        float wide = Mathf.Max(a, b);
        return wide > 0f && Mathf.Min(a, b) >= wide * DrumRoundness && axes[vertical].magnitude >= wide * DrumHeightShare;
    }

    // The box edge closest to straight up.
    private static int VerticalAxis(Vector3[] axes)
    {
        int vertical = 0;
        float best = -1f;
        for (int i = 0; i < 3; i++)
        {
            float upness = axes[i].sqrMagnitude > 0f ? Mathf.Abs(Vector3.Dot(axes[i].normalized, Vector3.up)) : 0f;
            if (upness > best)
            {
                best = upness;
                vertical = i;
            }
        }
        return vertical;
    }

    // A glass drum: a point light in its middle shining out all round, and a ring of spill glow
    // on whatever is just above and below it.
    private void BuildDrum(Vector3 centre, Vector3[] axes)
    {
        int vertical = VerticalAxis(axes);
        float height = axes[vertical].magnitude;
        float radius = (axes[(vertical + 1) % 3].magnitude + axes[(vertical + 2) % 3].magnitude) * 0.25f;
        Vector3 up = axes[vertical].normalized;
        if (Vector3.Dot(up, Vector3.up) < 0f) up = -up;
        Outward = Vector3.zero;
        FaceCentre = centre;

        lightObject = new GameObject(name + " Light");
        SceneManager.MoveGameObjectToScene(lightObject, gameObject.scene);
        lightObject.transform.SetPositionAndRotation(centre, Quaternion.FromToRotation(Vector3.up, up));
        Glow = lightObject.AddComponent<Light>();
        Glow.type = LightType.Point;
        Glow.color = LightColor;
        float reach = (radius + DrumLightReach) / DrumLightReach;
        Glow.intensity = LightIntensity * reach * reach;
        Glow.range = radius + LightRange;
        Glow.shadows = LightShadows.None;

        Material material = SpillMaterial();
        if (material == null) return;
        float margin = height * SpillMargin;
        spillMesh = BuildSpillRing(name, radius + Mathf.Max(radius, height) * SpillLift, height, margin);
        var spillObject = new GameObject("Wall Spill");
        spillObject.transform.SetParent(lightObject.transform, false);
        spillObject.AddComponent<MeshFilter>().sharedMesh = spillMesh;
        Spill = spillObject.AddComponent<MeshRenderer>();
        Spill.sharedMaterial = material;
        Spill.shadowCastingMode = ShadowCastingMode.Off;
        Spill.receiveShadows = false;
        Spill.lightProbeUsage = LightProbeUsage.Off;
        Spill.reflectionProbeUsage = ReflectionProbeUsage.Off;
        Spill.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
    }

    // An open tube just outside the drum, reaching margin above and below it. The spill shader
    // reads it like a card that is all window across (UV0.x stays mid-card, UV1.x is 1), so the
    // glow only rises and falls from the drum's top and bottom edges, the same all round.
    private static Mesh BuildSpillRing(string windowName, float radius, float height, float margin)
    {
        float half = height * 0.5f + margin;
        var share = new Vector2(1f, height * 0.5f / half);
        var vertices = new Vector3[DrumSegments * 2];
        var normals = new Vector3[vertices.Length];
        var uvs = new Vector2[vertices.Length];
        var windows = new Vector2[vertices.Length];
        var triangles = new int[DrumSegments * 6];
        for (int i = 0; i < DrumSegments; i++)
        {
            float angle = i * Mathf.PI * 2f / DrumSegments;
            var around = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            vertices[i * 2] = around * radius + Vector3.down * half;
            vertices[i * 2 + 1] = around * radius + Vector3.up * half;
            normals[i * 2] = normals[i * 2 + 1] = around;
            uvs[i * 2] = new Vector2(0.5f, 0f);
            uvs[i * 2 + 1] = new Vector2(0.5f, 1f);
            windows[i * 2] = windows[i * 2 + 1] = share;
            int next = (i + 1) % DrumSegments;
            int t = i * 6;
            triangles[t] = i * 2;
            triangles[t + 1] = i * 2 + 1;
            triangles[t + 2] = next * 2 + 1;
            triangles[t + 3] = i * 2;
            triangles[t + 4] = next * 2 + 1;
            triangles[t + 5] = next * 2;
        }
        var mesh = new Mesh { name = windowName + " Wall Spill", hideFlags = HideFlags.DontSave };
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.uv2 = windows;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        return mesh;
    }

    // The spill card, under the spot's object so it shows, hides and goes with it, but placed and
    // turned in world space: x across the window, y up it, z out of the glass. Skipped (the window
    // still glows and lights the ground) without its shader.
    private void BuildSpill(Vector3[] axes, int thin, Vector3 outward)
    {
        Material material = SpillMaterial();
        if (material == null) return;
        int a = (thin + 1) % 3, b = (thin + 2) % 3;
        int vertical = Mathf.Abs(Vector3.Dot(axes[a].normalized, Vector3.up)) >=
            Mathf.Abs(Vector3.Dot(axes[b].normalized, Vector3.up)) ? a : b;
        int across = vertical == a ? b : a;
        Vector3 up = Vector3.ProjectOnPlane(axes[vertical], outward).normalized;
        if (up.sqrMagnitude < 0.5f) up = Vector3.up;
        if (Vector3.Dot(up, Vector3.up) < 0f) up = -up;
        float width = axes[across].magnitude;
        float height = axes[vertical].magnitude;
        float margin = Mathf.Min(width, height) * SpillMargin;
        spillMesh = BuildSpillMesh(name, width, height, margin);

        var spillObject = new GameObject("Wall Spill");
        spillObject.transform.SetParent(lightObject.transform, false);
        spillObject.transform.SetPositionAndRotation(FaceCentre + outward * (Mathf.Min(width, height) * SpillLift),
            Quaternion.LookRotation(outward, up));
        spillObject.AddComponent<MeshFilter>().sharedMesh = spillMesh;
        Spill = spillObject.AddComponent<MeshRenderer>();
        Spill.sharedMaterial = material;
        Spill.shadowCastingMode = ShadowCastingMode.Off;
        Spill.receiveShadows = false;
        Spill.lightProbeUsage = LightProbeUsage.Off;
        Spill.reflectionProbeUsage = ReflectionProbeUsage.Off;
        Spill.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
    }

    // A quad over the window and the margin round it, centred on the window. UV0 runs 0-1 across
    // it; UV1 carries the window's share of it on each axis, for the shader's cut-out and fade.
    private static Mesh BuildSpillMesh(string windowName, float width, float height, float margin)
    {
        float halfWidth = width * 0.5f + margin;
        float halfHeight = height * 0.5f + margin;
        var share = new Vector2(width * 0.5f / halfWidth, height * 0.5f / halfHeight);
        var mesh = new Mesh { name = windowName + " Wall Spill", hideFlags = HideFlags.DontSave };
        mesh.vertices = new[]
        {
            new Vector3(-halfWidth, -halfHeight, 0f), new Vector3(halfWidth, -halfHeight, 0f),
            new Vector3(halfWidth, halfHeight, 0f), new Vector3(-halfWidth, halfHeight, 0f),
        };
        mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
        mesh.uv2 = new[] { share, share, share, share };
        mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
        mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        mesh.RecalculateBounds();
        return mesh;
    }

    // One material for every window's spill: Orca/Window Spill in the window light's colour.
    public static Material SpillMaterial()
    {
        if (spillMaterial != null) return spillMaterial;
        Shader shader = Resources.Load<Shader>(SpillShaderPath);
        if (shader == null) shader = Shader.Find(SpillShaderName);
        if (shader == null) return null;
        spillMaterial = new Material(shader) { name = "Window Spill", hideFlags = HideFlags.DontSave };
        spillMaterial.SetColor("_Color", LightColor);
        spillMaterial.SetFloat("_Intensity", SpillIntensity);
        return spillMaterial;
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
        if (glassGlow.HasProperty("_Smoothness")) glassGlow.SetFloat("_Smoothness", GlassSmoothness);
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
