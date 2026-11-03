using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// A lit window (glowWindow1/glowWindow2 on the magician's house). In play mode the glass
// glows (an emissive copy of glassMaterial in its slot, picked up by bloom), soft light
// rays shine a little way out of it and fade, each shaft slowly brightening and dimming on
// its own, and a shadowless spot light on the glass lights what is in front.
// The window stays dark until the player has been to the house's door (litBy), then
// brightens over fadeInTime once they are back outside, and stays lit.
// The window is a thin box: its thinnest axis is the glass normal, and of the two faces
// along it, the one away from the rest of the house is the outside.
// The rays and light live on their own root object, not under the window, whose uneven
// scale would stretch them, nor under the house, whose bounds frame the hat trick.
[DisallowMultipleComponent]
public class WindowGlow : MonoBehaviour
{
    [SerializeField]
    private Color color = new Color(1f, 0.76f, 0.4f);

    [Header("Switching on")]
    // The house's door. Left empty, the window is lit from the start.
    [SerializeField]
    private MagicianEncounter litBy;

    // Seconds after switch-on before this window starts to brighten, so neighbours don't light together.
    [SerializeField]
    private float switchOnDelay = 0f;

    [SerializeField]
    private float fadeInTime = 1.5f;

    [Header("Glass")]
    // Emissive URP Lit template; its colours are replaced by color.
    [SerializeField]
    private Material glassMaterial;

    // Material slot of the glass on the window's renderer (slot 1 is the window bars).
    [SerializeField]
    private int glassSlot = 0;

    [SerializeField]
    private float glassEmission = 2.6f;

    [Header("Rays")]
    [SerializeField]
    private Material rayMaterial;

    // Metres the longest shafts reach out of the glass.
    [SerializeField]
    private float rayLength = 2.6f;

    // How much wider than the window the beam is at its tip, as a share of the window.
    [SerializeField, Range(0f, 1.5f)]
    private float raySpread = 0.45f;

    // Downward lean of the rays, metres per metre out.
    [SerializeField, Range(0f, 1f)]
    private float rayDroop = 0.25f;

    [SerializeField, Range(1, 24)]
    private int shafts = 9;

    [Header("Light")]
    [SerializeField]
    private float lightIntensity = 22f;

    [SerializeField]
    private float lightRange = 14f;

    [SerializeField, Range(1f, 179f)]
    private float spotAngle = 120f;

    // Share of the intensity a slow candle-like waver takes away at most.
    [SerializeField, Range(0f, 0.5f)]
    private float flicker = 0.08f;

    private GameObject glowObject;
    private Mesh rayMesh;
    private Material glass;
    private Material rays;
    private Material[] originalMaterials;
    private Renderer windowRenderer;
    private Color glassBase;
    private float rayIntensity = 1f;
    private float noiseSeed;
    private float delayLeft;
    private bool? shown;

    // Latched the first time litBy says the player has visited and is back outside.
    public bool SwitchedOn { get; private set; }
    // 0 dark to 1 fully lit.
    public float Level { get; private set; }

    public Light Glow { get; private set; }
    public MeshRenderer Rays { get; private set; }
    public Vector3 Outward { get; private set; }
    // Centre of the outside face of the glass.
    public Vector3 FaceCentre { get; private set; }

    private void Start()
    {
        Build();
        Tick(0f);
    }

    private void OnEnable()
    {
        if (glowObject != null) Refresh();
    }

    private void OnDisable()
    {
        if (glowObject != null) Show(false);
    }

    private void OnDestroy()
    {
        if (glowObject != null && windowRenderer != null && originalMaterials != null)
        {
            windowRenderer.sharedMaterials = originalMaterials;
        }
        Discard(glowObject);
        Discard(rayMesh);
        Discard(glass);
        Discard(rays);
    }

    private void Update()
    {
        Tick(Time.deltaTime);
    }

    // Switches on once the player is back outside after visiting, then fades in.
    public void Tick(float deltaTime)
    {
        if (glowObject == null) return;
        if (!SwitchedOn && (litBy == null || (litBy.HasVisited && !litBy.InRoom)))
        {
            SwitchedOn = true;
            delayLeft = switchOnDelay;
        }
        if (SwitchedOn)
        {
            if (delayLeft > 0f) delayLeft -= deltaTime;
            else Level = Mathf.MoveTowards(Level, 1f, deltaTime / Mathf.Max(0.01f, fadeInTime));
        }
        Refresh();
    }

    private void Refresh()
    {
        bool visible = enabled && gameObject.activeInHierarchy && Level > 0f;
        Show(visible);
        if (!visible) return;
        float eased = Mathf.SmoothStep(0f, 1f, Level);
        float waver = Mathf.PerlinNoise(Time.time * 1.7f, noiseSeed) * 0.7f + Mathf.PerlinNoise(Time.time * 6.1f, noiseSeed + 7.3f) * 0.3f;
        Glow.intensity = lightIntensity * eased * (1f - flicker * waver);
        if (rays != null && rays.HasProperty("_Intensity")) rays.SetFloat("_Intensity", rayIntensity * eased);
        if (glass != null)
        {
            Color baseColor = Color.Lerp(glassBase, color, eased);
            if (glass.HasProperty("_BaseColor")) glass.SetColor("_BaseColor", baseColor);
            if (glass.HasProperty("_Color")) glass.SetColor("_Color", baseColor);
            glass.SetColor("_EmissionColor", color * (glassEmission * eased));
        }
    }

    private void Show(bool visible)
    {
        if (shown == visible) return;
        shown = visible;
        glowObject.SetActive(visible);
        if (windowRenderer == null || glass == null) return;
        Material[] materials = (Material[])originalMaterials.Clone();
        if (visible && glassSlot >= 0 && glassSlot < materials.Length) materials[glassSlot] = glass;
        windowRenderer.sharedMaterials = materials;
    }

    private void Build()
    {
        if (glowObject != null) return;
        noiseSeed = (StableHash(name) % 1000) * 0.1f;
        windowRenderer = GetComponent<Renderer>();
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
        int a = (thin + 1) % 3, b = (thin + 2) % 3;
        int vertical = Mathf.Abs(Vector3.Dot(axes[a].normalized, Vector3.up)) >= Mathf.Abs(Vector3.Dot(axes[b].normalized, Vector3.up)) ? a : b;
        int across = vertical == a ? b : a;

        Vector3 centre = transform.TransformPoint(local.center);
        Vector3 outward = axes[thin].normalized;
        if (Vector3.Dot(outward, centre - HouseCentre(centre)) < 0f) outward = -outward;
        Vector3 up = Vector3.ProjectOnPlane(axes[vertical], outward).normalized;
        float width = axes[across].magnitude;
        float height = axes[vertical].magnitude;
        Outward = outward;
        FaceCentre = centre + outward * (axes[thin].magnitude * 0.5f);

        glowObject = new GameObject(name + " Light Rays");
        glowObject.transform.SetPositionAndRotation(FaceCentre + outward * 0.02f, Quaternion.LookRotation(outward, up));

        rayMesh = BuildRays(width, height);
        glowObject.AddComponent<MeshFilter>().sharedMesh = rayMesh;
        Rays = glowObject.AddComponent<MeshRenderer>();
        if (rayMaterial != null)
        {
            // Its own copy, so the fade-in dims only this window's rays.
            rays = new Material(rayMaterial) { name = rayMaterial.name + " (" + name + ")" };
            if (rays.HasProperty("_Intensity")) rayIntensity = rays.GetFloat("_Intensity");
        }
        Rays.sharedMaterial = rays;
        Rays.shadowCastingMode = ShadowCastingMode.Off;
        Rays.receiveShadows = false;
        Rays.lightProbeUsage = LightProbeUsage.Off;
        Rays.reflectionProbeUsage = ReflectionProbeUsage.Off;
        Rays.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

        // On the glass, tipped down like the rays, so the cone opens at the window. Not behind
        // it: the window's own collider would then block it for SpriteLightTint's shadow rays.
        var lightObject = new GameObject("Window Light");
        lightObject.transform.SetParent(glowObject.transform, false);
        lightObject.transform.localRotation = Quaternion.LookRotation(new Vector3(0f, -rayDroop, 1f));
        Glow = lightObject.AddComponent<Light>();
        Glow.type = LightType.Spot;
        Glow.color = color;
        Glow.intensity = lightIntensity;
        Glow.range = Mathf.Max(0.1f, lightRange);
        Glow.spotAngle = spotAngle;
        Glow.innerSpotAngle = spotAngle * 0.45f;
        Glow.shadows = LightShadows.None;

        if (windowRenderer != null)
        {
            originalMaterials = windowRenderer.sharedMaterials;
            if (glassMaterial != null)
            {
                glass = new Material(glassMaterial) { name = glassMaterial.name + " (" + name + ")" };
                // The unlit glass colour it brightens from.
                Material unlit = glassSlot >= 0 && glassSlot < originalMaterials.Length ? originalMaterials[glassSlot] : null;
                glassBase = unlit != null && unlit.HasProperty("_BaseColor") ? unlit.GetColor("_BaseColor")
                    : unlit != null && unlit.HasProperty("_Color") ? unlit.GetColor("_Color") : color;
                if (glass.HasProperty("_BaseColor")) glass.SetColor("_BaseColor", color);
                if (glass.HasProperty("_Color")) glass.SetColor("_Color", color);
                glass.EnableKeyword("_EMISSION");
                glass.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                glass.SetColor("_EmissionColor", color * glassEmission);
            }
        }
    }

    // Centre of everything else the window's parent (the house) renders.
    private Vector3 HouseCentre(Vector3 fallback)
    {
        if (transform.parent == null) return fallback - transform.forward;
        bool found = false;
        var bounds = new Bounds();
        foreach (Renderer part in transform.parent.GetComponentsInChildren<Renderer>(true))
        {
            if (part.transform == transform || part.GetComponent<WindowGlow>() != null) continue;
            if (!found) bounds = part.bounds; else bounds.Encapsulate(part.bounds);
            found = true;
        }
        return found ? bounds.center : fallback - transform.forward;
    }

    // In the rays object's frame: x across the window, y up it, z out of the glass. Each shaft
    // is two crossed cards from a spot on the glass outwards, spreading and leaning down; two
    // upright and two flat haze cards fill the beam between them. UV x runs across a card, y
    // from the glass (0) to the tip (1); z is the shaft's own phase for the shader's shimmer.
    private Mesh BuildRays(float width, float height)
    {
        var random = new System.Random((int)StableHash(name));
        float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector4>();
        var colors = new List<Color>();
        var triangles = new List<int>();

        void Card(Vector3 from, Vector3 to, Vector3 acrossFrom, Vector3 acrossTo, float brightness, float phase)
        {
            Vector3 normal = Vector3.Cross(acrossFrom, to - from).normalized;
            int start = vertices.Count;
            vertices.Add(from - acrossFrom * 0.5f);
            vertices.Add(from + acrossFrom * 0.5f);
            vertices.Add(to + acrossTo * 0.5f);
            vertices.Add(to - acrossTo * 0.5f);
            uvs.Add(new Vector4(0f, 0f, phase, 0f));
            uvs.Add(new Vector4(1f, 0f, phase, 0f));
            uvs.Add(new Vector4(1f, 1f, phase, 0f));
            uvs.Add(new Vector4(0f, 1f, phase, 0f));
            for (int i = 0; i < 4; i++)
            {
                normals.Add(normal);
                colors.Add(new Color(color.r, color.g, color.b, brightness));
            }
            triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
        }

        Vector3 Tip(Vector3 from, float length) => new Vector3(
            from.x * (1f + raySpread),
            from.y * (1f + raySpread) - rayDroop * length,
            length);

        for (int i = 0; i < 2; i++)
        {
            float t = i == 0 ? -0.22f : 0.22f;
            float length = rayLength * 0.75f;
            Vector3 upright = new Vector3(t * width, 0f, 0f);
            Card(upright, Tip(upright, length), Vector3.up * height * 0.9f, Vector3.up * height * 0.9f * (1f + raySpread), 0.3f, Range(0f, 6.283f));
            Vector3 flat = new Vector3(0f, t * height, 0f);
            Card(flat, Tip(flat, length), Vector3.right * width * 0.9f, Vector3.right * width * 0.9f * (1f + raySpread), 0.3f, Range(0f, 6.283f));
        }

        for (int i = 0; i < shafts; i++)
        {
            float shaftWidth = width * Range(0.12f, 0.3f);
            float reach = Mathf.Max(0f, width * 0.5f - shaftWidth * 0.5f);
            // Spread evenly across the window, jittered, at a random height.
            float x = Mathf.Lerp(-reach, reach, (i + Range(0.2f, 0.8f)) / shafts);
            float y = Range(-0.4f, 0.4f) * height;
            float length = rayLength * Range(0.55f, 1f);
            var from = new Vector3(x, y, 0f);
            Vector3 to = Tip(from, length);
            Vector3 direction = (to - from).normalized;
            Vector3 flatAcross = Vector3.ProjectOnPlane(Vector3.right, direction).normalized;
            Vector3 uprightAcross = Vector3.Cross(direction, flatAcross).normalized;
            float brightness = Range(0.45f, 1f);
            float phase = Range(0f, 6.283f);
            float tipWidth = shaftWidth * (1f + raySpread);
            Card(from, to, flatAcross * shaftWidth, flatAcross * tipWidth, brightness, phase);
            Card(from, to, uprightAcross * shaftWidth, uprightAcross * tipWidth, brightness, phase);
        }

        var mesh = new Mesh { name = name + " Light Rays" };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void Discard(Object target)
    {
        if (target == null) return;
        if (Application.isPlaying) Destroy(target);
        else DestroyImmediate(target);
    }

    // FNV-1a: stable across sessions, unlike string.GetHashCode.
    private static uint StableHash(string text)
    {
        uint hash = 2166136261;
        foreach (char ch in text) hash = (hash ^ ch) * 16777619;
        return hash & 0x7fffffff;
    }
}
