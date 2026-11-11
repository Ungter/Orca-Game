using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Low-poly waves on the sea. Round the camera the water is one connected faceted surface, like
// the island's shore rocks: a grid of points, each nudged a little off its place so no two facets
// are alike, rising and falling with the waves that roll across the sea, the flat triangles between
// them tilting at every angle and catching the moonlight. It is drawn with the ocean's own
// pixel-art water (Orca/Pixel Water). Further out the waves die down to the flat sea, which the
// map's ocean and the Far Ocean draw as before; inside the waves they are cut away so the troughs
// show.
//
// The surface is one mesh round the origin that the water shader lifts and carries along with
// whichever camera draws it, snapped to the grid so the facets never swim, so every camera (the
// game's, the Scene view's) sees waves round itself. This sets the waves' shape for the shader as
// globals each frame, and works out the same waves here so things can float on them: Ride gives a
// hull's heave, pitch and roll (the starting boat rides it in the opening and as the ferry), and
// holds the water down round it. The water round the docks (calmAround) stays calmer so it never
// laps over their decks.
[ExecuteAlways]
[DisallowMultipleComponent]
[DefaultExecutionOrder(-100)]
public sealed class OceanWaves : MonoBehaviour
{
    public const string ObjectName = "Ocean Waves";
    public const int MaxCalmAreas = 8;
    private const string SeaName = "Wave Surface";
    // Round a hull the water stays held down this far out from its sides, in metres, before it
    // starts to rise again.
    private const float HullMargin = 1f;

    [Header("Surface")]
    [Tooltip("Spacing of the points the surface is made of, in metres.")]
    [SerializeField, Min(0.25f)] private float cellSize = 1.5f;
    [Tooltip("Points across the square of them round the camera (an even number).")]
    [SerializeField, Range(16, 240)] private int cellsAcross = 172;
    [Tooltip("How far each point is nudged off the grid, as a share of the spacing, so the facets are not all alike.")]
    [SerializeField, Range(0f, 0.4f)] private float jitter = 0.3f;

    [Header("Waves")]
    [Tooltip("Highest a wave rises above the sea (and deepest it sinks below), in metres.")]
    [SerializeField, Min(0f)] private float amplitude = 0.8f;
    [Tooltip("How fast the waves roll, against real deep-water waves of their length.")]
    [SerializeField, Min(0f)] private float speed = 0.6f;
    [Tooltip("Three waves added up: direction (x, z), wavelength in metres, share of the height.")]
    [SerializeField] private Vector4 waveA = new Vector4(1f, 0.35f, 22f, 0.5f);
    [SerializeField] private Vector4 waveB = new Vector4(-0.4f, 1f, 13f, 0.3f);
    [SerializeField] private Vector4 waveC = new Vector4(0.8f, -0.7f, 7.5f, 0.2f);

    [Header("Reach")]
    [Tooltip("Full waves within this far of the camera, in metres.")]
    [SerializeField, Min(0f)] private float fullWithin = 85f;
    [Tooltip("Flat sea from this far out, in metres. Stays inside the square of the surface.")]
    [SerializeField, Min(1f)] private float flatBeyond = 105f;

    [Header("Calm water")]
    [Tooltip("The waves die down round these (docks, piers), so they never lap over them.")]
    [SerializeField] private Transform[] calmAround = new Transform[0];
    [Tooltip("How much of the waves is left right by them.")]
    [SerializeField, Range(0f, 1f)] private float calmShare = 0.4f;
    [Tooltip("How far out from them the waves come back to full, in metres.")]
    [SerializeField, Min(0.1f)] private float calmFalloff = 12f;

    [Header("Floating")]
    [Tooltip("The water under a hull riding the waves is kept this far below where it rides, in metres.")]
    [SerializeField, Min(0f)] private float hullClearance = 0.3f;
    [Tooltip("A metre out from the hull's sides the water may rise again this much per metre.")]
    [SerializeField, Min(0.05f)] private float hullSlope = 0.4f;

    [Header("Look")]
    [Tooltip("How much the facets' tilt shows: lighter facing the moon, darker facing away.")]
    [SerializeField, Range(0f, 3f)] private float facetShade = 1.5f;
    [Tooltip("Crests a little lighter and troughs a little darker, so the heights read from above.")]
    [SerializeField, Range(0f, 0.5f)] private float crestLight = 0.2f;

    // The waves the shader draws, here too so things can float on them.
    public static OceanWaves Active { get; private set; }

    public float Level { get; private set; }
    public float CellSize => cellSize;
    public float Amplitude => amplitude;
    public float FullWithin => fullWithin;
    public float FlatBeyond => flatBeyond;
    public float HullClearance => hullClearance;
    // Where the flat sea is cut away round the camera: a little past the waves' end, and still
    // inside the square of the surface wherever its snapping leaves it.
    public float FlatCutRadius => Mathf.Min(flatBeyond + cellSize * 2f, HalfWidth - cellSize * 2f);
    public float HalfWidth => EvenCells * cellSize * 0.5f;
    public int CalmAreaCount { get; private set; }
    public Mesh SeaMesh => sea != null ? sea.sharedMesh : null;
    // The surface's grid moves with the camera two points at a time, so its triangles always fall
    // the same way on the same spot of sea: an even number of points across.
    private int EvenCells => cellsAcross + (cellsAcross & 1);

    private static readonly int VoxelId = Shader.PropertyToID("_OceanVoxel");
    private static readonly int WaveAId = Shader.PropertyToID("_OceanWaveA");
    private static readonly int WaveBId = Shader.PropertyToID("_OceanWaveB");
    private static readonly int WaveCId = Shader.PropertyToID("_OceanWaveC");
    private static readonly int ShapeId = Shader.PropertyToID("_OceanWaveShape");
    private static readonly int ReachId = Shader.PropertyToID("_OceanReach");
    private static readonly int CalmId = Shader.PropertyToID("_OceanCalm");
    private static readonly int CalmShapeId = Shader.PropertyToID("_OceanCalmShape");
    private static readonly int LookId = Shader.PropertyToID("_OceanVoxelLook");
    private static readonly int FlatCutId = Shader.PropertyToID("_OceanFlatCut");
    public static readonly int SurfaceId = Shader.PropertyToID("_OceanSurface");
    private static readonly int HullId = Shader.PropertyToID("_OceanHull");
    private static readonly int HullSizeId = Shader.PropertyToID("_OceanHullSize");
    private static readonly Dictionary<(int, float), Mesh> Meshes = new Dictionary<(int, float), Mesh>();

    private readonly Vector4[] calm = new Vector4[MaxCalmAreas];
    private Vector4 waveAPacked, waveBPacked, waveCPacked;
    // The last hull to ride the waves (the starting boat): middle x, z and heading x, z; half its
    // length and width, the highest the water may stand under it and how fast it may rise out
    // from its sides; and the frame it rode.
    private Vector4 hull, hullSize;
    private int hullFrame = -10;
    private MeshFilter sea;
    private Renderer ocean;
    private Renderer farOcean;
    private MaterialPropertyBlock flatCut;

    private void OnEnable()
    {
        Active = this;
        Refresh();
        BuildSea();
        CutFlatSea(true);
    }

    private void OnDisable()
    {
        if (Active == this) Active = null;
        CutFlatSea(false);
        Shader.SetGlobalVector(ReachId, Vector4.zero);
        Shader.SetGlobalVector(ShapeId, Vector4.zero);
        Shader.SetGlobalVector(HullSizeId, Vector4.zero);
        if (sea != null)
        {
            if (Application.isPlaying) Destroy(sea.gameObject);
            else DestroyImmediate(sea.gameObject);
            sea = null;
        }
    }

    private void OnValidate()
    {
        flatBeyond = Mathf.Max(flatBeyond, fullWithin + 1f);
        if (isActiveAndEnabled) Refresh();
    }

    private void Update()
    {
        if (Active == null) Active = this;
        Refresh();
        if (sea == null) BuildSea();
        else if (ocean != null && sea.TryGetComponent(out MeshRenderer drawn) && drawn.sharedMaterial != ocean.sharedMaterial)
            drawn.sharedMaterial = ocean.sharedMaterial;
        CutFlatSea(true);
    }

    // Once no hull has ridden for a frame, the water is let up again.
    private void LateUpdate()
    {
        if (hullFrame < Time.frameCount - 1) Shader.SetGlobalVector(HullSizeId, Vector4.zero);
    }

    // Game time, so the waves stop with the game; in the editor, real time.
    public static float WaveTime => Application.isPlaying ? Time.time : Time.realtimeSinceStartup;

    // Reads the sea and the calm areas and hands the waves' shape to the shader.
    public void Refresh()
    {
        if (ocean == null) ocean = OceanHorizon.FindOcean(gameObject.scene);
        Level = ocean != null ? ocean.bounds.max.y : transform.position.y;
        waveAPacked = Pack(waveA);
        waveBPacked = Pack(waveB);
        waveCPacked = Pack(waveC);

        int count = 0;
        foreach (Transform around in calmAround)
        {
            if (around == null || count >= MaxCalmAreas) continue;
            bool any = false;
            Bounds bounds = default;
            foreach (Renderer part in around.GetComponentsInChildren<Renderer>())
            {
                if (any) bounds.Encapsulate(part.bounds);
                else bounds = part.bounds;
                any = true;
            }
            if (!any) continue;
            calm[count++] = new Vector4(bounds.center.x, bounds.center.z, bounds.extents.x, bounds.extents.z);
        }
        for (int i = count; i < MaxCalmAreas; i++) calm[i] = Vector4.zero;
        CalmAreaCount = count;

        Shader.SetGlobalVector(VoxelId, new Vector4(cellSize, jitter, Level, WaveTime));
        Shader.SetGlobalVector(WaveAId, waveAPacked);
        Shader.SetGlobalVector(WaveBId, waveBPacked);
        Shader.SetGlobalVector(WaveCId, waveCPacked);
        Shader.SetGlobalVector(ShapeId, new Vector4(waveA.w, waveB.w, waveC.w, amplitude));
        Shader.SetGlobalVector(ReachId, new Vector4(fullWithin, flatBeyond, FlatCutRadius, HullMargin));
        Shader.SetGlobalVectorArray(CalmId, calm);
        Shader.SetGlobalVector(CalmShapeId, new Vector4(count, calmShare, calmFalloff, 0f));
        Shader.SetGlobalVector(LookId, new Vector4(facetShade, crestLight, 0f, 0f));
    }

    // Direction (normalised), wavenumber and how fast it turns, from direction, wavelength.
    private Vector4 Pack(Vector4 wave)
    {
        var direction = new Vector2(wave.x, wave.y);
        direction = direction.sqrMagnitude > 1e-8f ? direction.normalized : Vector2.right;
        float k = 2f * Mathf.PI / Mathf.Max(wave.z, 0.1f);
        float omega = Mathf.Sqrt(9.81f * k) * speed;
        return new Vector4(direction.x, direction.y, k, omega);
    }

    // ---- The waves, as the shader works them out ------------------------------------------

    // Height of the waves above the sea at a point, in metres, with the waves reaching out round
    // centre (the camera), now or at a given time.
    public float HeightAt(float x, float z, Vector2 centre) => HeightAt(x, z, centre, WaveTime);

    public float HeightAt(float x, float z, Vector2 centre, float time)
    {
        float h = waveA.w * Mathf.Sin(waveAPacked.z * (waveAPacked.x * x + waveAPacked.y * z) - waveAPacked.w * time)
                + waveB.w * Mathf.Sin(waveBPacked.z * (waveBPacked.x * x + waveBPacked.y * z) - waveBPacked.w * time)
                + waveC.w * Mathf.Sin(waveCPacked.z * (waveCPacked.x * x + waveCPacked.y * z) - waveCPacked.w * time);
        return h * amplitude * Calm(x, z) * Reach(x, z, centre);
    }

    public float HeightAt(Vector3 at) => HeightAt(at.x, at.z, CentreFor(at));

    // The surface's height at a point (between its own points it runs straight from one to the
    // next): the waves, held down round the hull riding them.
    public float SurfaceHeightAt(Vector3 at) => Mathf.Min(HeightAt(at), HullAllowance(at.x, at.z));

    public float SurfaceHeightAt(float x, float z, Vector2 centre, float time) =>
        Mathf.Min(HeightAt(x, z, centre, time), HullAllowance(x, z));

    // The highest the water may stand at a point with the hull riding the waves: below the hull
    // under it and for HullMargin out, then rising again hullSlope per metre, so the water eases
    // back up round it rather than jumping as it moves. No limit without a hull.
    private float HullAllowance(float x, float z)
    {
        if (hullFrame < Time.frameCount - 1 || hullSize.w <= 0f) return float.MaxValue;
        float dx = x - hull.x, dz = z - hull.y;
        float along = Mathf.Max(Mathf.Abs(dx * hull.z + dz * hull.w) - hullSize.x, 0f);
        float across = Mathf.Max(Mathf.Abs(dx * hull.w - dz * hull.z) - hullSize.y, 0f);
        float outside = Mathf.Sqrt(along * along + across * across);
        return hullSize.z + Mathf.Max(outside - HullMargin, 0f) * hullSize.w;
    }

    // The waves' centre, or the point itself when there is no game camera to centre them on.
    private Vector2 CentreFor(Vector3 at)
    {
        Vector2 centre = Centre();
        return float.IsNaN(centre.x) ? new Vector2(at.x, at.z) : centre;
    }

    // Full waves near centre, none from flatBeyond out.
    private float Reach(float x, float z, Vector2 centre)
    {
        float distance = new Vector2(x - centre.x, z - centre.y).magnitude;
        return 1f - Smooth(fullWithin, flatBeyond, distance);
    }

    // calmShare of the waves right by a calm area, full again calmFalloff out.
    private float Calm(float x, float z)
    {
        float nearest = float.MaxValue;
        for (int i = 0; i < CalmAreaCount; i++)
        {
            float dx = Mathf.Max(Mathf.Abs(x - calm[i].x) - calm[i].z, 0f);
            float dz = Mathf.Max(Mathf.Abs(z - calm[i].y) - calm[i].w, 0f);
            nearest = Mathf.Min(nearest, Mathf.Sqrt(dx * dx + dz * dz));
        }
        return CalmAreaCount == 0 ? 1f : Mathf.Lerp(calmShare, 1f, Smooth(0f, calmFalloff, nearest));
    }

    // HLSL's smoothstep.
    private static float Smooth(float from, float to, float x)
    {
        float t = Mathf.Clamp01((x - from) / Mathf.Max(to - from, 1e-5f));
        return t * t * (3f - 2f * t);
    }

    // Where the waves reach out from: the game camera, as the shader does (where it is, not
    // snapped to the grid, so the waves die down smoothly as it moves).
    public Vector2 Centre()
    {
        Camera view = Camera.main;
        if (view == null) return new Vector2(float.NaN, float.NaN);
        Vector3 at = view.transform.position;
        return new Vector2(at.x, at.z);
    }

    // ---- Floating -------------------------------------------------------------------------

    // Where on the hull the waves are felt: bow, middle and stern (shares of its length either way
    // of the middle), each at port, centre and starboard (shares of its width).
    public const float FeltAlong = 0.4f, FeltAcross = 0.45f;

    // Where a hull this long and this wide rides on the waves when it would otherwise sit at
    // rest, heading this way. It feels the waves at nine points across it (FeltAlong, FeltAcross),
    // pitches and rolls to the slope they make (eased, so it rocks rather than lurches), and then
    // sits just high enough that the water at none of them stands above where it rides, so it
    // never dips under a wave. Round it the water is held down a little below where it rides
    // (hullClearance) and eases back up out from its sides, so no wave stands up through the open
    // hull or over its sides. At rest without waves.
    public static Pose Ride(Vector3 rest, Quaternion heading, float length, float width)
    {
        if (Active == null || !Active.isActiveAndEnabled) return new Pose(rest, heading);
        return Ride(rest, heading, length, width, Active.CentreFor(rest), WaveTime);
    }

    // The same, with the waves reaching out from a given point (another camera's), now or at a
    // given time.
    public static Pose Ride(Vector3 rest, Quaternion heading, float length, float width, Vector2 centre) =>
        Ride(rest, heading, length, width, centre, WaveTime);

    public static Pose Ride(Vector3 rest, Quaternion heading, float length, float width, Vector2 centre, float time)
    {
        if (Active == null || !Active.isActiveAndEnabled) return new Pose(rest, heading);
        Vector3 ahead = heading * Vector3.forward;
        ahead.y = 0f;
        ahead = ahead.sqrMagnitude > 1e-6f ? ahead.normalized : Vector3.forward;
        var side = new Vector3(ahead.z, 0f, -ahead.x);
        float along = Mathf.Max(length * FeltAlong, 0.05f), across = Mathf.Max(width * FeltAcross, 0.05f);
        Vector3 Offset(int i, int j) => ahead * (i * along) + side * (j * across);
        float Water(int i, int j)
        {
            Vector3 at = Offset(i, j);
            return Active.HeightAt(rest.x + at.x, rest.z + at.z, centre, time);
        }

        // The slope of the water under it, fitted through the nine points.
        float slopeAhead = 0f, slopeSide = 0f;
        for (int i = -1; i <= 1; i++)
        {
            for (int j = -1; j <= 1; j++)
            {
                float h = Water(i, j);
                slopeAhead += i * along * h;
                slopeSide += j * across * h;
            }
        }
        slopeAhead /= 6f * along * along;
        slopeSide /= 6f * across * across;
        const float Ease = 0.6f;
        float pitch = -Mathf.Atan(slopeAhead) * Mathf.Rad2Deg * Ease;
        float roll = Mathf.Atan(slopeSide) * Mathf.Rad2Deg * Ease;
        Quaternion rotation = Quaternion.AngleAxis(pitch, side) * Quaternion.AngleAxis(roll, ahead) * heading;

        // High enough that the water stands nowhere above it, at its nine points and at its ends
        // and corners; its lowest point, for holding down the water round it.
        Vector3 up = rotation * Vector3.up;
        float heave = float.MinValue, lowest = float.MaxValue;
        for (int i = -1; i <= 1; i++)
        {
            for (int j = -1; j <= 1; j++)
            {
                Vector3 at = Offset(i, j);
                float tilted = -(up.x * at.x + up.z * at.z) / Mathf.Max(up.y, 0.1f);
                heave = Mathf.Max(heave, Water(i, j) - tilted);
                Vector3 end = ahead * (i * length * 0.5f) + side * (j * width * 0.5f);
                lowest = Mathf.Min(lowest, Mathf.Min(tilted, -(up.x * end.x + up.z * end.z) / Mathf.Max(up.y, 0.1f)));
            }
        }

        // The water round it, held down for the shader at once, so the surface and the hull move
        // together in the same frame.
        Active.hull = new Vector4(rest.x, rest.z, ahead.x, ahead.z);
        Active.hullSize = new Vector4(length * 0.5f, width * 0.5f, heave + lowest - Active.hullClearance, Active.hullSlope);
        Active.hullFrame = Time.frameCount;
        Shader.SetGlobalVector(HullId, Active.hull);
        Shader.SetGlobalVector(HullSizeId, Active.hullSize);
        return new Pose(rest + Vector3.up * heave, rotation);
    }

    // ---- The surface ----------------------------------------------------------------------

    // The flat sea (the map's ocean and the Far Ocean) is cut away where the waves are, so the
    // troughs show; only those two renderers, so nothing else drawn with the water is touched.
    // Every block set on the water says both whether it is cut and whether it is the wave
    // surface: Unity carries a block's value over to the next draw of the same material whose
    // block leaves it out, so the flat sea drawn just after the surface would otherwise be taken
    // for it (from some camera positions the whole sea went black).
    private void CutFlatSea(bool cut)
    {
        flatCut ??= new MaterialPropertyBlock();
        if (ocean == null) ocean = OceanHorizon.FindOcean(gameObject.scene);
        // Not while its scene is still loading or already closing.
        if (farOcean == null && gameObject.scene.isLoaded)
        {
            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
            {
                FarOcean far = root.GetComponentInChildren<FarOcean>(true);
                if (far != null && far.TryGetComponent(out farOcean)) break;
            }
        }
        float want = cut ? 1f : 0f;
        foreach (Renderer flat in new[] { ocean, farOcean })
        {
            if (flat == null) continue;
            flat.GetPropertyBlock(flatCut);
            if (flatCut.HasFloat(FlatCutId) && flatCut.GetFloat(FlatCutId) == want
                && flatCut.HasFloat(SurfaceId) && flatCut.GetFloat(SurfaceId) == 0f) continue;
            flatCut.SetFloat(FlatCutId, want);
            flatCut.SetFloat(SurfaceId, 0f);
            flat.SetPropertyBlock(flatCut);
        }
    }

    private void BuildSea()
    {
        if (sea != null) return;
        if (ocean == null) ocean = OceanHorizon.FindOcean(gameObject.scene);
        if (ocean == null) return;
        var holder = new GameObject(SeaName) { hideFlags = HideFlags.DontSave | HideFlags.NotEditable };
        holder.layer = ocean.gameObject.layer;
        holder.transform.SetParent(transform, false);
        sea = holder.AddComponent<MeshFilter>();
        sea.sharedMesh = SeaGrid(EvenCells, cellSize);
        MeshRenderer renderer = holder.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = ocean.sharedMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        // Marked as the wave surface on its renderer, so the shader lifts its points and nothing
        // else drawn with the water (the flat sea's meshes have no channel that could say so);
        // and, said outright, not cut away (see CutFlatSea).
        var marker = new MaterialPropertyBlock();
        marker.SetFloat(SurfaceId, 1f);
        marker.SetFloat(FlatCutId, 0f);
        renderer.SetPropertyBlock(marker);
    }

    // A square grid of points round the origin, across x across cells of this size. Each square
    // is cut into two triangles, the cut running one way and then the other like a chequerboard,
    // so the facets lean every way. The shader nudges and lifts the points (its renderer is
    // marked as the wave surface). Made once per size and kept.
    public static Mesh SeaGrid(int across, float cell)
    {
        across += across & 1;
        if (Meshes.TryGetValue((across, cell), out Mesh made) && made != null) return made;
        float half = across * cell * 0.5f;
        int row = across + 1;
        var positions = new List<Vector3>(row * row);
        for (int j = 0; j <= across; j++)
        {
            for (int i = 0; i <= across; i++)
            {
                positions.Add(new Vector3(i * cell - half, 0f, j * cell - half));
            }
        }
        var indices = new List<int>(across * across * 6);
        for (int j = 0; j < across; j++)
        {
            for (int i = 0; i < across; i++)
            {
                int a = j * row + i, b = a + 1, c = a + row, d = c + 1;
                if (((i + j) & 1) == 0) indices.AddRange(new[] { a, c, d, a, d, b });
                else indices.AddRange(new[] { a, c, b, b, c, d });
            }
        }
        var mesh = new Mesh { name = "Wave Surface " + across + " x " + cell + " m", hideFlags = HideFlags.DontSave };
        mesh.indexFormat = positions.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.SetVertices(positions);
        mesh.SetIndices(indices, MeshTopology.Triangles, 0, false);
        // The shader carries it round every camera: never cull it.
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(1e5f, 1e3f, 1e5f));
        Meshes[(across, cell)] = mesh;
        return mesh;
    }
}
