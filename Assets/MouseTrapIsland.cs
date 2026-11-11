using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// The main island cut into hexes for the lighthouse's final game (LighthouseMouseTrap). Built
// while the screen is black and thrown away when the scene reloads for the ending:
//
// - The ground and the other big meshes on it (the merged scenery chunks, paths) are cut into one
//   piece per hex: the ground exactly along the hex edges, plants and rocks whole, by where they
//   stand. Each piece hangs under its own hex object, and the originals are hidden.
// - Everything smaller standing on the island (trees, lamps, hedges, fence pickets, stones) moves
//   with the hex it stands on.
// - Each hex gets rock sides down into the sea (the cliff material), so when a neighbour sinks it
//   shows a cliff, and thin dark seams along its edges, so the island reads as split.
// - The houses stay where they are: the hexes they cover are the board's walls.
//
// A struck hex shakes, then drops into the sea taking all of that with it. Fences the villain
// runs through fall flat (each run of fence falls as one, about its foot, away from him).
public sealed class MouseTrapIsland : IDisposable
{
    public const string GroundName = "IslandDemo:pCube1";
    private const float ShakeTime = 0.4f;
    private const float SplitTime = 1.5f;
    private const float Gravity = 18f;
    private const float FallDepth = 9f;
    private const int EdgeSamples = 12;
    private const float SeamWidth = 0.75f;
    private const float KnockTime = 0.45f;
    private const float KnockAngle = 84f;

    private sealed class Piece
    {
        public Hex Hex;
        public Transform Transform;
        public Vector3 Centre;
        public float Settle;
        public float SettleDelay;
        public float Offset;
        public Vector2 Jitter;
        public Quaternion Tilt = Quaternion.identity;
        public Quaternion TiltTarget = Quaternion.identity;
        public float SinkTime = -1f;
        public bool Gone;
        public float Top = float.NegativeInfinity;
        public readonly List<Renderer> Renderers = new List<Renderer>();
    }

    private sealed class Unit
    {
        public Transform Transform;
        public Vector3 Position;
        public Quaternion Rotation;
        public bool WasActive;
        public Hex Home;
        public Hex[] Covers;
        public bool OnBoard;
        public Panel Panel;
        public Renderer[] Renderers;
    }

    private sealed class Panel
    {
        public Vector2 A, B;
        public Vector3 Pivot;
        public Vector3 Axis;
        public float Angle;
        public float Time = -1f;
        public bool Knocked;
        public readonly List<Unit> Members = new List<Unit>();
    }

    // A vertex being cut: everything a source mesh may carry, in world space.
    private struct Vert
    {
        public Vector3 P;
        public Vector3 N;
        public Vector4 T;
        public Color C;
        public Vector2 U0;
        public Vector2 U1;

        public static Vert Lerp(Vert a, Vert b, float t) => new Vert
        {
            P = Vector3.LerpUnclamped(a.P, b.P, t),
            N = Vector3.LerpUnclamped(a.N, b.N, t),
            T = Vector4.LerpUnclamped(a.T, b.T, t),
            C = Color.LerpUnclamped(a.C, b.C, t),
            U0 = Vector2.LerpUnclamped(a.U0, b.U0, t),
            U1 = Vector2.LerpUnclamped(a.U1, b.U1, t)
        };
    }

    // One source mesh's share of one hex.
    private sealed class PieceMesh
    {
        public readonly List<Vector3> Positions = new List<Vector3>();
        public readonly List<Vector3> Normals = new List<Vector3>();
        public readonly List<Vector4> Tangents = new List<Vector4>();
        public readonly List<Color> Colors = new List<Color>();
        public readonly List<Vector2> Uv0 = new List<Vector2>();
        public readonly List<Vector2> Uv1 = new List<Vector2>();
        public readonly Dictionary<int, int> Shared = new Dictionary<int, int>();
        public List<int>[] Triangles;
    }

    public HexGrid Grid { get; }
    public float SeaLevel { get; }
    public IReadOnlyCollection<Hex> Cells => cells;
    public IReadOnlyCollection<Hex> Walls => walls;
    public Transform Root => root;
    public IReadOnlyList<Renderer> Occluders => occluders;
    public IReadOnlyList<Renderer> GroundPieces => groundPieces;
    public int SplitSources => hiddenSources.Count;
    public int UnitCount => units.Count;
    public int PanelCount => panels.Count;
    public Bounds BoardBounds { get; private set; }
    public bool IsSplitting => splitTime >= 0f && splitTime < SplitTime + 0.6f;

    private readonly Scene scene;
    private readonly Collider ground;
    // What things stand on: the ground and the colliders of the paved slabs cut with it.
    private readonly List<Collider> surfaces = new List<Collider>();
    private readonly float groundTop;
    private readonly HashSet<Hex> cells = new HashSet<Hex>();
    private readonly HashSet<Hex> walls = new HashSet<Hex>();
    private readonly Dictionary<Hex, Vector2> stands = new Dictionary<Hex, Vector2>();
    private readonly Dictionary<Hex, Piece> pieces = new Dictionary<Hex, Piece>();
    private readonly List<Unit> units = new List<Unit>();
    private readonly List<Panel> panels = new List<Panel>();
    private readonly List<Renderer> hiddenSources = new List<Renderer>();
    private readonly List<Renderer> occluders = new List<Renderer>();
    private readonly List<Renderer> groundPieces = new List<Renderer>();
    private readonly List<Object> created = new List<Object>();
    private readonly HashSet<Piece> moving = new HashSet<Piece>();
    private readonly System.Random random;
    private Transform root;
    private Material cliff;
    private Material seam;
    private float splitTime = -1f;

    private MouseTrapIsland(Scene scene, Collider ground, float radius, float seaLevel, int seed)
    {
        this.scene = scene;
        this.ground = ground;
        Bounds b = ground.bounds;
        groundTop = b.max.y;
        Grid = new HexGrid(new Vector2(b.center.x, b.center.z), radius);
        surfaces.Add(ground);
        SeaLevel = seaLevel;
        random = new System.Random(seed);
    }

    // Cuts the island up. seams is the dark seams' material (blended), readableCopies stands in
    // for big meshes a player build cannot read back.
    public static MouseTrapIsland Build(Scene scene, float radius, int seed, Material seams,
        IReadOnlyDictionary<Mesh, Mesh> readableCopies = null)
    {
        GameObject groundObject = Find(scene, GroundName);
        if (groundObject == null || !groundObject.TryGetComponent(out MeshCollider collider) ||
            !groundObject.TryGetComponent(out MeshRenderer groundRenderer)) return null;
        Renderer ocean = OceanHorizon.FindOcean(scene);
        float sea = ocean != null ? ocean.bounds.max.y : -1.83f;
        var island = new MouseTrapIsland(scene, collider, radius, sea, seed);
        island.root = new GameObject("Mouse trap island").transform;
        if (scene.IsValid() && scene.isLoaded) SceneManager.MoveGameObjectToScene(island.root.gameObject, scene);
        island.cliff = FindCliff(groundRenderer);
        island.seam = seams;
        island.FindCells(groundRenderer.bounds);
        List<Renderer> houses = island.FindHouses();
        island.FindWalls(houses);
        island.CutAndGather(groundRenderer, houses, readableCopies);
        island.BuildEdges();
        island.BuildPanels();
        island.Settle();
        return island;
    }

    // ---- Board -----------------------------------------------------------------------------

    // Points across a hex, as shares of its radius: the middle and three hexagonal rings, all inside
    // it, for telling how much of it is land.
    private static readonly Vector2[] Samples = BuildSamples();

    private static Vector2[] BuildSamples()
    {
        var samples = new List<Vector2> { Vector2.zero };
        foreach (var (scale, perEdge) in new[] { (0.33f, 1), (0.66f, 2), (0.95f, 3) })
            for (int corner = 0; corner < 6; corner++)
            {
                Vector2 a = Polar(60f * corner - 30f, scale), b = Polar(60f * corner + 30f, scale);
                for (int k = 0; k < perEdge; k++) samples.Add(Vector2.Lerp(a, b, k / (float)perEdge));
            }
        return samples.ToArray();
    }

    // Hexes with any dry land in them, the biggest joined patch of them: the whole hexes inland and
    // the part-hexes along the shore. Each gets a stand point in the middle of its land, where the
    // villain stands and the beam aims (for a whole hex, its middle).
    private void FindCells(Bounds groundBounds)
    {
        var land = new HashSet<Hex>();
        var standing = new Dictionary<Hex, Vector2>();
        float pitch = 1.5f * Grid.Radius;
        int rMin = Mathf.FloorToInt((groundBounds.min.z - Grid.Origin.y) / pitch) - 1;
        int rMax = Mathf.CeilToInt((groundBounds.max.z - Grid.Origin.y) / pitch) + 1;
        for (int r = rMin; r <= rMax; r++)
        {
            int qMin = Mathf.FloorToInt((groundBounds.min.x - Grid.Origin.x) / Grid.Width - r * 0.5f) - 1;
            int qMax = Mathf.CeilToInt((groundBounds.max.x - Grid.Origin.x) / Grid.Width - r * 0.5f) + 1;
            for (int q = qMin; q <= qMax; q++)
            {
                var hex = new Hex(q, r);
                Vector2 c = Grid.Centre(hex);
                var dry = new List<Vector2>();
                foreach (Vector2 s in Samples)
                {
                    Vector2 p = c + s * Grid.Radius;
                    if (GroundHeight(p.x, p.y) > SeaLevel + 0.3f) dry.Add(p);
                }
                if (dry.Count == 0) continue;
                land.Add(hex);
                Vector2 middle = Vector2.zero;
                foreach (Vector2 p in dry) middle += p;
                middle /= dry.Count;
                if (dry.Count < Samples.Length && GroundHeight(middle.x, middle.y) <= SeaLevel + 0.3f)
                {
                    // A crescent of shore: stand on its dry point nearest its middle.
                    Vector2 nearest = dry[0];
                    foreach (Vector2 p in dry)
                        if ((p - middle).sqrMagnitude < (nearest - middle).sqrMagnitude) nearest = p;
                    middle = nearest;
                }
                standing[hex] = middle;
            }
        }
        var best = new List<Hex>();
        var seen = new HashSet<Hex>();
        foreach (Hex start in land)
        {
            if (!seen.Add(start)) continue;
            var patch = new List<Hex> { start };
            for (int i = 0; i < patch.Count; i++)
                for (int d = 0; d < 6; d++)
                {
                    Hex next = patch[i].Neighbour(d);
                    if (land.Contains(next) && seen.Add(next)) patch.Add(next);
                }
            if (patch.Count > best.Count) best = patch;
        }
        foreach (Hex hex in best)
        {
            cells.Add(hex);
            stands[hex] = standing[hex];
        }
        Bounds bounds = default;
        bool any = false;
        foreach (Hex hex in cells)
        {
            Vector2 s = stands[hex];
            var c = new Vector3(s.x, GroundHeight(s.x, s.y), s.y);
            if (any) bounds.Encapsulate(c);
            else { bounds = new Bounds(c, Vector3.zero); any = true; }
        }
        BoardBounds = bounds;
    }

    // The three houses: the magician's (world/building1), Al Cachud's (the loose parts straight
    // under world) and the alchemist's (building3). Glows and window light are left out.
    private List<Renderer> FindHouses()
    {
        var houses = new List<Renderer>();
        GameObject world = Find(scene, "world");
        if (world != null)
        {
            foreach (Transform child in world.transform)
            {
                if (IsFenceName(child.name) || child.name == "island_demo_map" || child.name == "ocean") continue;
                houses.AddRange(child.GetComponentsInChildren<Renderer>(true));
            }
        }
        foreach (GameObject top in scene.GetRootGameObjects())
            if (top.name == "building3") houses.AddRange(top.GetComponentsInChildren<Renderer>(true));
        houses.RemoveAll(r => r == null || r.GetComponentInParent<DoorBeacon>() != null ||
            r.sharedMaterial != null && r.sharedMaterial.renderQueue >= 2500);
        return houses;
    }

    // Hexes the houses stand on: at least 40% of the hex under a house part.
    private void FindWalls(List<Renderer> houses)
    {
        var parts = new List<(Bounds world, Matrix4x4 toLocal, Bounds local)>();
        foreach (Renderer part in houses)
            parts.Add((part.bounds, part.transform.worldToLocalMatrix, part.localBounds));
        var samples = new List<Vector2> { Vector2.zero };
        for (int i = 0; i < 6; i++) samples.Add(Polar(60f * i + 30f, 0.5f));
        for (int i = 0; i < 12; i++) samples.Add(Polar(30f * i, 0.85f));
        foreach (Hex hex in cells)
        {
            Vector2 c = Grid.Centre(hex);
            int covered = 0;
            foreach (Vector2 s in samples)
            {
                Vector2 p = c + s * Grid.Radius;
                foreach (var part in parts)
                {
                    if (p.x < part.world.min.x || p.x > part.world.max.x || p.y < part.world.min.z || p.y > part.world.max.z) continue;
                    Vector3 local = part.toLocal.MultiplyPoint3x4(new Vector3(p.x, part.world.center.y, p.y));
                    if (Mathf.Abs(local.x - part.local.center.x) <= part.local.extents.x &&
                        Mathf.Abs(local.z - part.local.center.z) <= part.local.extents.z)
                    {
                        covered++;
                        break;
                    }
                }
            }
            if (covered >= samples.Count * 0.4f) walls.Add(hex);
        }
    }

    private static Vector2 Polar(float degrees, float length) =>
        new Vector2(Mathf.Cos(degrees * Mathf.Deg2Rad), Mathf.Sin(degrees * Mathf.Deg2Rad)) * length;

    // ---- Cutting ---------------------------------------------------------------------------

    private void CutAndGather(MeshRenderer groundRenderer, List<Renderer> houses, IReadOnlyDictionary<Mesh, Mesh> copies)
    {
        var houseSet = new HashSet<Renderer>(houses);
        Bounds area = groundRenderer.bounds;
        area.Expand(new Vector3(Grid.Width * 2f, 0f, Grid.Width * 2f));
        area.SetMinMax(new Vector3(area.min.x, SeaLevel - 20f, area.min.z), new Vector3(area.max.x, area.max.y + 150f, area.max.z));
        Renderer ocean = OceanHorizon.FindOcean(scene);
        float unitLimit = Grid.Width;

        var splits = new List<(Renderer renderer, Mesh mesh)>();
        var unitRenderers = new List<Renderer>();
        var blocked = new HashSet<Transform>();
        foreach (GameObject top in scene.GetRootGameObjects())
        {
            if (top.transform == root || top.name == "Lighthouse island") continue;
            foreach (Renderer r in top.GetComponentsInChildren<Renderer>(false))
            {
                if (!r.enabled || r == ocean || !area.Intersects(r.bounds)) continue;
                if (houseSet.Contains(r) || Excluded(r))
                {
                    if (houseSet.Contains(r)) occluders.Add(r);
                    MarkAncestors(r.transform, blocked);
                    continue;
                }
                Mesh source = r is MeshRenderer && r.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
                Mesh readable = null;
                if (source != null) readable = source.isReadable ? source : copies != null && copies.TryGetValue(source, out Mesh copy) ? copy : null;
                float size = Mathf.Max(r.bounds.size.x, r.bounds.size.z);
                if (readable != null && (r == groundRenderer || size > unitLimit && !IsFencePiece(r.transform)))
                {
                    splits.Add((r, readable));
                    MarkAncestors(r.transform, blocked);
                }
                else unitRenderers.Add(r);
            }
        }

        foreach (var (renderer, mesh) in splits) Cut(renderer, mesh, renderer == groundRenderer);
        foreach (var (renderer, _) in splits)
        {
            renderer.enabled = false;
            hiddenSources.Add(renderer);
            foreach (Collider c in renderer.GetComponents<Collider>())
                if (c.enabled && !c.isTrigger && !surfaces.Contains(c)) surfaces.Add(c);
        }
        GatherUnits(unitRenderers, blocked, unitLimit);
    }

    private static void MarkAncestors(Transform t, HashSet<Transform> into)
    {
        for (; t != null; t = t.parent) into.Add(t);
    }

    // Things on the island that are not part of it: the player, cameras, the boat, the sea.
    private static bool Excluded(Renderer r) =>
        r.GetComponentInParent<PlayerController>() != null || r.GetComponentInParent<Camera>() != null ||
        r.GetComponentInParent<LighthouseFerry>() != null || r.GetComponentInParent<OceanWaves>() != null ||
        r.GetComponentInParent<FarOcean>() != null || r.GetComponentInParent<DoorBeacon>() != null;

    private static bool IsFenceName(string name) =>
        name.StartsWith("fence", StringComparison.OrdinalIgnoreCase) || name == "Fence Picket" || name == "Picket Fence Rail";

    private static bool IsFencePiece(Transform t)
    {
        for (; t != null; t = t.parent)
            if (IsFenceName(t.name)) return true;
        return false;
    }

    // Cuts one mesh into the hexes. Joined lumps smaller than a hex (a plant, a rock) go whole to
    // the hex under their middle; bigger ones (the ground, long rock strips) are cut along the
    // hex edges. Points are welded by position first, as low-poly plants split every face.
    private void Cut(Renderer renderer, Mesh mesh, bool cutAll)
    {
        Matrix4x4 toWorld = renderer.localToWorldMatrix;
        Matrix4x4 normalToWorld = toWorld.inverse.transpose;
        Vector3[] positions = mesh.vertices;
        Vector3[] normals = mesh.normals;
        Vector4[] tangents = mesh.tangents;
        Color[] colors = mesh.colors;
        Vector2[] uv0 = mesh.uv;
        Vector2[] uv1 = mesh.uv2;
        int count = positions.Length;
        bool hasN = normals.Length == count, hasT = tangents.Length == count, hasC = colors.Length == count;
        bool hasU0 = uv0.Length == count, hasU1 = uv1.Length == count;
        var world = new Vector3[count];
        for (int i = 0; i < count; i++) world[i] = toWorld.MultiplyPoint3x4(positions[i]);

        var parent = new int[count];
        for (int i = 0; i < count; i++) parent[i] = i;
        int FindRoot(int i)
        {
            while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; }
            return i;
        }
        void Join(int a, int b)
        {
            a = FindRoot(a); b = FindRoot(b);
            if (a != b) parent[a] = b;
        }
        var weld = new Dictionary<Vector3Int, int>();
        for (int i = 0; i < count; i++)
        {
            var key = new Vector3Int(Mathf.RoundToInt(world[i].x * 500f), Mathf.RoundToInt(world[i].y * 500f), Mathf.RoundToInt(world[i].z * 500f));
            if (weld.TryGetValue(key, out int other)) Join(i, other);
            else weld[key] = i;
        }
        int subMeshes = mesh.subMeshCount;
        var triangles = new int[subMeshes][];
        for (int s = 0; s < subMeshes; s++)
        {
            triangles[s] = mesh.GetTriangles(s);
            int[] t = triangles[s];
            for (int i = 0; i + 2 < t.Length; i += 3) { Join(t[i], t[i + 1]); Join(t[i + 1], t[i + 2]); }
        }
        var lumpMin = new Dictionary<int, Vector2>();
        var lumpMax = new Dictionary<int, Vector2>();
        for (int i = 0; i < count; i++)
        {
            int lump = FindRoot(i);
            var p = new Vector2(world[i].x, world[i].z);
            if (lumpMin.TryGetValue(lump, out Vector2 lo)) { lumpMin[lump] = Vector2.Min(lo, p); lumpMax[lump] = Vector2.Max(lumpMax[lump], p); }
            else { lumpMin[lump] = p; lumpMax[lump] = p; }
        }

        Vert Get(int i) => new Vert
        {
            P = world[i],
            N = hasN ? normalToWorld.MultiplyVector(normals[i]).normalized : Vector3.up,
            T = hasT ? Tangent(toWorld, tangents[i]) : new Vector4(1f, 0f, 0f, 1f),
            C = hasC ? colors[i] : Color.white,
            U0 = hasU0 ? uv0[i] : Vector2.zero,
            U1 = hasU1 ? uv1[i] : Vector2.zero
        };

        var meshes = new Dictionary<Hex, PieceMesh>();
        PieceMesh For(Hex hex)
        {
            if (!meshes.TryGetValue(hex, out PieceMesh piece))
            {
                piece = new PieceMesh { Triangles = new List<int>[subMeshes] };
                for (int s = 0; s < subMeshes; s++) piece.Triangles[s] = new List<int>();
                meshes[hex] = piece;
            }
            return piece;
        }
        void AddWhole(Hex hex, int submesh, int a, int b, int c)
        {
            PieceMesh piece = For(hex);
            Vector3 origin = Grid.Centre(hex, 0f);
            foreach (int index in new[] { a, b, c })
            {
                if (!piece.Shared.TryGetValue(index, out int at))
                {
                    at = piece.Positions.Count;
                    piece.Shared[index] = at;
                    Append(piece, Get(index), origin);
                }
                piece.Triangles[submesh].Add(at);
            }
        }

        var polygon = new List<Vert>(12);
        var scratch = new List<Vert>(12);
        var candidates = new HashSet<Hex>();
        for (int s = 0; s < subMeshes; s++)
        {
            int[] t = triangles[s];
            for (int i = 0; i + 2 < t.Length; i += 3)
            {
                int a = t[i], b = t[i + 1], c = t[i + 2];
                int lump = FindRoot(a);
                Vector2 size = lumpMax[lump] - lumpMin[lump];
                if (!cutAll && Mathf.Max(size.x, size.y) <= Grid.Radius)
                {
                    Vector2 middle = (lumpMin[lump] + lumpMax[lump]) * 0.5f;
                    AddWhole(Grid.At(middle.x, middle.y), s, a, b, c);
                    continue;
                }
                Hex ha = Grid.At(world[a].x, world[a].z), hb = Grid.At(world[b].x, world[b].z), hc = Grid.At(world[c].x, world[c].z);
                if (ha == hb && hb == hc)
                {
                    AddWhole(ha, s, a, b, c);
                    continue;
                }
                candidates.Clear();
                foreach (Hex h in new[] { ha, hb, hc })
                {
                    candidates.Add(h);
                    for (int d = 0; d < 6; d++) candidates.Add(h.Neighbour(d));
                }
                Vert va = Get(a), vb = Get(b), vc = Get(c);
                foreach (Hex hex in candidates)
                {
                    polygon.Clear();
                    polygon.Add(va); polygon.Add(vb); polygon.Add(vc);
                    if (!ClipToHex(hex, polygon, scratch)) continue;
                    PieceMesh piece = For(hex);
                    Vector3 origin = Grid.Centre(hex, 0f);
                    int first = piece.Positions.Count;
                    foreach (Vert v in polygon) Append(piece, v, origin);
                    for (int k = 1; k + 1 < polygon.Count; k++)
                    {
                        piece.Triangles[s].Add(first);
                        piece.Triangles[s].Add(first + k);
                        piece.Triangles[s].Add(first + k + 1);
                    }
                }
            }
        }

        bool groundLike = cutAll || renderer.bounds.size.y < 2.5f;
        foreach (var pair in meshes)
        {
            Piece piece = PieceAt(pair.Key);
            var built = new Mesh { name = renderer.name + " " + pair.Key };
            created.Add(built);
            PieceMesh data = pair.Value;
            built.indexFormat = data.Positions.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            built.SetVertices(data.Positions);
            if (hasN) built.SetNormals(data.Normals);
            if (hasT) built.SetTangents(data.Tangents);
            if (hasC) built.SetColors(data.Colors);
            if (hasU0) built.SetUVs(0, data.Uv0);
            if (hasU1) built.SetUVs(1, data.Uv1);
            built.subMeshCount = subMeshes;
            for (int s = 0; s < subMeshes; s++) built.SetTriangles(data.Triangles[s], s, false);
            built.RecalculateBounds();
            var holder = new GameObject(renderer.name) { layer = renderer.gameObject.layer };
            holder.transform.SetParent(piece.Transform, false);
            holder.AddComponent<MeshFilter>().sharedMesh = built;
            MeshRenderer drawn = holder.AddComponent<MeshRenderer>();
            drawn.sharedMaterials = renderer.sharedMaterials;
            drawn.shadowCastingMode = renderer.shadowCastingMode;
            drawn.receiveShadows = renderer.receiveShadows;
            drawn.lightProbeUsage = renderer.lightProbeUsage;
            drawn.reflectionProbeUsage = renderer.reflectionProbeUsage;
            drawn.renderingLayerMask = renderer.renderingLayerMask;
            piece.Renderers.Add(drawn);
            (groundLike ? groundPieces : occluders).Add(drawn);
        }
    }

    private static Vector4 Tangent(Matrix4x4 toWorld, Vector4 t)
    {
        Vector3 d = toWorld.MultiplyVector(new Vector3(t.x, t.y, t.z)).normalized;
        return new Vector4(d.x, d.y, d.z, t.w);
    }

    private static void Append(PieceMesh piece, Vert v, Vector3 origin)
    {
        piece.Positions.Add(v.P - origin);
        piece.Normals.Add(v.N.sqrMagnitude > 1e-8f ? v.N.normalized : Vector3.up);
        piece.Tangents.Add(v.T);
        piece.Colors.Add(v.C);
        piece.Uv0.Add(v.U0);
        piece.Uv1.Add(v.U1);
    }

    // Keeps the part of the polygon inside the hex (Sutherland-Hodgman against its six sides).
    private bool ClipToHex(Hex hex, List<Vert> polygon, List<Vert> scratch)
    {
        Vector2 centre = Grid.Centre(hex);
        float apothem = Grid.Width * 0.5f;
        for (int side = 0; side < 6 && polygon.Count > 0; side++)
        {
            float angle = side * 60f * Mathf.Deg2Rad;
            var n = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            scratch.Clear();
            for (int i = 0; i < polygon.Count; i++)
            {
                Vert current = polygon[i], next = polygon[(i + 1) % polygon.Count];
                float dc = Vector2.Dot(new Vector2(current.P.x, current.P.z) - centre, n) - apothem;
                float dn = Vector2.Dot(new Vector2(next.P.x, next.P.z) - centre, n) - apothem;
                if (dc <= 0f) scratch.Add(current);
                if (dc <= 0f != dn <= 0f) scratch.Add(Vert.Lerp(current, next, dc / (dc - dn)));
            }
            polygon.Clear();
            polygon.AddRange(scratch);
        }
        return polygon.Count >= 3;
    }

    private Piece PieceAt(Hex hex)
    {
        if (pieces.TryGetValue(hex, out Piece piece)) return piece;
        Vector2 c = Grid.Centre(hex);
        piece = new Piece
        {
            Hex = hex,
            Centre = new Vector3(c.x, 0f, c.y),
            Transform = new GameObject("Hex " + hex.Q + "," + hex.R).transform
        };
        piece.Transform.SetParent(root, false);
        piece.Transform.localPosition = piece.Centre;
        // The height of its land: for a part-hex on the shore its middle is over the sea, so take
        // its stand point (on its land) instead.
        Vector2 stand = Stand(hex);
        piece.Top = GroundHeight(stand.x, stand.y);
        if (float.IsNegativeInfinity(piece.Top)) piece.Top = SeaLevel;
        pieces[hex] = piece;
        return piece;
    }

    // ---- Things standing on the island -----------------------------------------------------

    // Each renderer moves with the biggest group above it that is still smaller than a hex (a
    // whole tree, a whole lamp), unless that group also holds something cut up or left alone.
    private void GatherUnits(List<Renderer> renderers, HashSet<Transform> blocked, float limit)
    {
        var sizes = new Dictionary<Transform, float>();
        float SizeOf(Transform t)
        {
            if (sizes.TryGetValue(t, out float size)) return size;
            Bounds? b = null;
            foreach (Renderer r in t.GetComponentsInChildren<Renderer>(false))
            {
                if (!r.enabled) continue;
                if (b.HasValue) { Bounds e = b.Value; e.Encapsulate(r.bounds); b = e; }
                else b = r.bounds;
            }
            size = b.HasValue ? Mathf.Max(b.Value.size.x, b.Value.size.z) : 0f;
            sizes[t] = size;
            return size;
        }
        var roots = new HashSet<Transform>();
        foreach (Renderer r in renderers)
        {
            Transform t = r.transform;
            if (blocked.Contains(t)) continue;
            bool fence = IsFencePiece(t);
            // Fence pickets and rails stay single, so a run of fence can fall as one.
            if (!fence)
                while (t.parent != null && !blocked.Contains(t.parent) && SizeOf(t.parent) <= limit) t = t.parent;
            roots.Add(t);
        }
        foreach (Transform t in roots)
        {
            bool nested = false;
            for (Transform up = t.parent; up != null && !nested; up = up.parent) nested = roots.Contains(up);
            if (nested) continue;
            Renderer[] drawn = t.GetComponentsInChildren<Renderer>(false);
            Bounds? lit = null;
            foreach (Renderer r in drawn)
            {
                if (!r.enabled) continue;
                if (lit.HasValue) { Bounds e = lit.Value; e.Encapsulate(r.bounds); lit = e; }
                else lit = r.bounds;
            }
            Bounds b = lit ?? drawn[0].bounds;
            var covers = new List<Hex>();
            Hex home = Grid.At(b.center.x, b.center.z);
            if (cells.Contains(home)) covers.Add(home);
            // Something too big for one hex (a paved slab no build can cut) goes with the first
            // hex under it to sink.
            if (Mathf.Max(b.size.x, b.size.z) > Grid.Width)
            {
                foreach (Hex cell in cells)
                {
                    Vector2 c = Grid.Centre(cell);
                    if (c.x >= b.min.x && c.x <= b.max.x && c.y >= b.min.z && c.y <= b.max.z && cell != home) covers.Add(cell);
                }
            }
            bool fence = IsFencePiece(t);
            if (covers.Count == 0 && !fence) continue;
            units.Add(new Unit
            {
                Transform = t,
                Position = t.position,
                Rotation = t.rotation,
                WasActive = t.gameObject.activeSelf,
                Home = home,
                Covers = covers.ToArray(),
                OnBoard = covers.Count > 0,
                Renderers = drawn
            });
            occluders.AddRange(drawn);
        }
    }

    // ---- Rock sides and seams -------------------------------------------------------------

    private void BuildEdges()
    {
        var sideHexes = new HashSet<Hex>(cells);
        foreach (Hex cell in cells)
            for (int d = 0; d < 6; d++)
                if (pieces.ContainsKey(cell.Neighbour(d))) sideHexes.Add(cell.Neighbour(d));
        foreach (Hex hex in sideHexes)
        {
            bool board = cells.Contains(hex);
            var sides = new List<Vector3>();
            var sideNormals = new List<Vector3>();
            var sideUv = new List<Vector2>();
            var sideTris = new List<int>();
            var seamPoints = new List<Vector3>();
            var seamColors = new List<Color>();
            var seamTris = new List<int>();
            Vector3 origin = Grid.Centre(hex, 0f);
            for (int d = 0; d < 6; d++)
            {
                Hex next = hex.Neighbour(d);
                if (!board && !cells.Contains(next)) continue;
                Vector2 c0 = Grid.Corner(hex, d), c1 = Grid.Corner(hex, d + 1);
                float angle = d * 60f * Mathf.Deg2Rad;
                var outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                float bottom = SeaLevel - 3f;
                float[] tops = new float[EdgeSamples + 1];
                for (int k = 0; k <= EdgeSamples; k++)
                {
                    Vector2 p = Vector2.Lerp(c0, c1, k / (float)EdgeSamples);
                    tops[k] = SurfaceHeight(p.x, p.y);
                }
                for (int k = 0; k < EdgeSamples; k++)
                {
                    if (tops[k] < SeaLevel - 0.3f || tops[k + 1] < SeaLevel - 0.3f) continue;
                    Vector2 p0 = Vector2.Lerp(c0, c1, k / (float)EdgeSamples);
                    Vector2 p1 = Vector2.Lerp(c0, c1, (k + 1) / (float)EdgeSamples);
                    int at = sides.Count;
                    float u0 = (p0 - c0).magnitude / 6f + d, u1 = (p1 - c0).magnitude / 6f + d;
                    sides.Add(new Vector3(p0.x, tops[k] + 0.03f, p0.y) - origin);
                    sides.Add(new Vector3(p1.x, tops[k + 1] + 0.03f, p1.y) - origin);
                    sides.Add(new Vector3(p1.x, bottom, p1.y) - origin);
                    sides.Add(new Vector3(p0.x, bottom, p0.y) - origin);
                    sideUv.Add(new Vector2(u0, tops[k] / 6f));
                    sideUv.Add(new Vector2(u1, tops[k + 1] / 6f));
                    sideUv.Add(new Vector2(u1, bottom / 6f));
                    sideUv.Add(new Vector2(u0, bottom / 6f));
                    for (int n = 0; n < 4; n++) sideNormals.Add(outward);
                    // Seen from outside, p0 is on the left: clockwise, so it faces outward.
                    sideTris.AddRange(new[] { at, at + 1, at + 2, at, at + 2, at + 3 });
                }
                // Dark seams only between hexes of the board, half on each side.
                if (!board || !cells.Contains(next)) continue;
                Vector2 inward = -new Vector2(outward.x, outward.z) * SeamWidth;
                for (int k = 0; k <= EdgeSamples; k++)
                {
                    if (tops[k] < SeaLevel) continue;
                    Vector2 p = Vector2.Lerp(c0, c1, k / (float)EdgeSamples);
                    Vector2 q = p + inward;
                    float inner = SurfaceHeight(q.x, q.y);
                    if (inner < SeaLevel) inner = tops[k];
                    seamPoints.Add(new Vector3(p.x, tops[k] + 0.12f, p.y) - origin);
                    seamPoints.Add(new Vector3(q.x, inner + 0.12f, q.y) - origin);
                    seamColors.Add(new Color(1f, 1f, 1f, 1f));
                    seamColors.Add(new Color(1f, 1f, 1f, 0f));
                    if (k > 0 && tops[k - 1] >= SeaLevel)
                    {
                        int at = seamPoints.Count - 4;
                        seamTris.AddRange(new[] { at, at + 1, at + 3, at, at + 3, at + 2 });
                    }
                }
            }
            Piece piece = PieceAt(hex);
            if (sideTris.Count > 0)
            {
                var mesh = new Mesh { name = "Hex sides " + hex };
                created.Add(mesh);
                mesh.SetVertices(sides);
                mesh.SetNormals(sideNormals);
                mesh.SetUVs(0, sideUv);
                mesh.SetTriangles(sideTris, 0);
                mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                piece.Renderers.Add(AddDrawn(piece.Transform, "Rock sides", mesh, cliff, ShadowCastingMode.On));
                groundPieces.Add(piece.Renderers[piece.Renderers.Count - 1]);
            }
            if (seamTris.Count > 0 && seam != null)
            {
                var mesh = new Mesh { name = "Hex seams " + hex };
                created.Add(mesh);
                mesh.SetVertices(seamPoints);
                mesh.SetColors(seamColors);
                mesh.SetTriangles(seamTris, 0);
                mesh.RecalculateBounds();
                piece.Renderers.Add(AddDrawn(piece.Transform, "Seams", mesh, seam, ShadowCastingMode.Off));
            }
        }
    }

    private static MeshRenderer AddDrawn(Transform parent, string name, Mesh mesh, Material material, ShadowCastingMode shadows)
    {
        var holder = new GameObject(name);
        holder.transform.SetParent(parent, false);
        holder.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer drawn = holder.AddComponent<MeshRenderer>();
        drawn.sharedMaterial = material;
        drawn.shadowCastingMode = shadows;
        drawn.receiveShadows = shadows != ShadowCastingMode.Off;
        return drawn;
    }

    private static Material FindCliff(MeshRenderer groundRenderer)
    {
        Material[] materials = groundRenderer.sharedMaterials;
        foreach (Material m in materials)
            if (m != null && m.name.IndexOf("Cliff", StringComparison.OrdinalIgnoreCase) >= 0) return m;
        return materials.Length > 0 ? materials[materials.Length - 1] : null;
    }

    // ---- Fences ------------------------------------------------------------------------------

    // Runs of fence: each rail and the pickets along it, falling together.
    private void BuildPanels()
    {
        var rails = new List<Unit>();
        var pickets = new List<Unit>();
        foreach (Unit unit in units)
        {
            if (!IsFencePiece(unit.Transform)) continue;
            if (unit.Transform.name.IndexOf("Rail", StringComparison.OrdinalIgnoreCase) >= 0) rails.Add(unit);
            else pickets.Add(unit);
        }
        foreach (Unit rail in rails)
        {
            if (!Line(rail, out Vector2 a, out Vector2 b)) continue;
            Vector2 dir = (b - a).normalized;
            Panel into = null;
            foreach (Panel panel in panels)
            {
                Vector2 pd = (panel.B - panel.A).normalized;
                if (Mathf.Abs(pd.x * dir.y - pd.y * dir.x) > 0.08f) continue;
                if (DistanceToLine(a, panel.A, panel.B) > 0.8f || DistanceToLine(b, panel.A, panel.B) > 0.8f) continue;
                float ta = Vector2.Dot(a - panel.A, pd), tb = Vector2.Dot(b - panel.A, pd), len = (panel.B - panel.A).magnitude;
                if (Mathf.Max(ta, tb) < -0.5f || Mathf.Min(ta, tb) > len + 0.5f) continue;
                into = panel;
                float lo = Mathf.Min(0f, Mathf.Min(ta, tb)), hi = Mathf.Max(len, Mathf.Max(ta, tb));
                Vector2 start = panel.A;
                panel.A = start + pd * lo;
                panel.B = start + pd * hi;
                break;
            }
            if (into == null)
            {
                into = new Panel { A = a, B = b };
                panels.Add(into);
            }
            into.Members.Add(rail);
        }
        foreach (Unit picket in pickets)
        {
            Vector3 p3 = picket.Renderers[0].bounds.center;
            var p = new Vector2(p3.x, p3.z);
            Panel best = null;
            float bestDistance = 1.2f;
            foreach (Panel panel in panels)
            {
                Vector2 pd = (panel.B - panel.A).normalized;
                float t = Vector2.Dot(p - panel.A, pd);
                if (t < -1f || t > (panel.B - panel.A).magnitude + 1f) continue;
                float distance = DistanceToLine(p, panel.A, panel.B);
                if (distance < bestDistance) { bestDistance = distance; best = panel; }
            }
            best?.Members.Add(picket);
        }
        foreach (Panel panel in panels)
        {
            float lowA = float.MaxValue, lowB = float.MaxValue;
            float ya = GroundHeight(panel.A.x, panel.A.y), yb = GroundHeight(panel.B.x, panel.B.y);
            foreach (Unit member in panel.Members)
            {
                member.Panel = panel;
                foreach (Renderer r in member.Renderers)
                {
                    lowA = Mathf.Min(lowA, r.bounds.min.y);
                    lowB = Mathf.Min(lowB, r.bounds.min.y);
                }
            }
            if (ya < SeaLevel) ya = lowA;
            if (yb < SeaLevel) yb = lowB;
            panel.Pivot = new Vector3(panel.A.x, ya, panel.A.y);
            panel.Axis = (new Vector3(panel.B.x, yb, panel.B.y) - panel.Pivot).normalized;
        }
    }

    private static bool Line(Unit rail, out Vector2 a, out Vector2 b)
    {
        Renderer r = rail.Renderers[0];
        Bounds local = r.localBounds;
        Vector3 scale = r.transform.lossyScale;
        Vector3 axisX = r.transform.TransformDirection(Vector3.right) * local.extents.x * Mathf.Abs(scale.x);
        Vector3 axisZ = r.transform.TransformDirection(Vector3.forward) * local.extents.z * Mathf.Abs(scale.z);
        Vector3 axisY = r.transform.TransformDirection(Vector3.up) * local.extents.y * Mathf.Abs(scale.y);
        Vector3 along = axisX;
        if (Flat(axisZ) > Flat(along)) along = axisZ;
        if (Flat(axisY) > Flat(along)) along = axisY;
        Vector3 c = r.bounds.center;
        a = new Vector2(c.x - along.x, c.z - along.z);
        b = new Vector2(c.x + along.x, c.z + along.z);
        return (b - a).sqrMagnitude > 1f;
    }

    private static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

    private static float DistanceToLine(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 d = (b - a).normalized;
        Vector2 off = p - a;
        return Mathf.Abs(off.x * d.y - off.y * d.x);
    }

    // Knocks flat every run of fence the step from one point to the other goes through, away
    // from the one running through it. Returns how many fell.
    public int KnockDown(Vector3 from, Vector3 to)
    {
        var p = new Vector2(from.x, from.z);
        var q = new Vector2(to.x, to.z);
        int knocked = 0;
        foreach (Panel panel in panels)
        {
            if (panel.Knocked || panel.Members.Count == 0) continue;
            Vector2 d = (panel.B - panel.A).normalized;
            if (!SegmentsCross(p, q, panel.A - d * 0.4f, panel.B + d * 0.4f)) continue;
            var side = new Vector2(-d.y, d.x);
            if (Vector2.Dot(q - p, side) < 0f) side = -side;
            // Turning about the foot so the top swings towards side.
            Vector3 lean = Vector3.Cross(panel.Axis, Vector3.up);
            if (Vector3.Dot(lean, new Vector3(side.x, 0f, side.y)) < 0f) panel.Axis = -panel.Axis;
            panel.Knocked = true;
            panel.Time = 0f;
            knocked++;
        }
        return knocked;
    }

    private static bool SegmentsCross(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        float Cross(Vector2 u, Vector2 v) => u.x * v.y - u.y * v.x;
        Vector2 r = b - a, s = d - c;
        float den = Cross(r, s);
        if (Mathf.Abs(den) < 1e-6f) return false;
        float t = Cross(c - a, s) / den, u = Cross(c - a, r) / den;
        return t >= 0f && t <= 1f && u >= 0f && u <= 1f;
    }

    public bool AnyFenceFalling
    {
        get
        {
            foreach (Panel panel in panels)
                if (panel.Time >= 0f && panel.Time < KnockTime + 0.3f) return true;
            return false;
        }
    }

    public int FallenPanels
    {
        get
        {
            int fallen = 0;
            foreach (Panel panel in panels)
                if (panel.Knocked) fallen++;
            return fallen;
        }
    }

    // ---- The island splitting and sinking ----------------------------------------------------

    // Each hex of the board settles a little, by its own amount, so the seams between them show as
    // small steps. The houses' hexes stay put under them.
    private void Settle()
    {
        foreach (Piece piece in pieces.Values)
        {
            if (!cells.Contains(piece.Hex) || walls.Contains(piece.Hex)) continue;
            piece.Settle = -Mathf.Lerp(0.25f, 0.9f, (float)random.NextDouble());
            piece.SettleDelay = (float)random.NextDouble() * 0.6f;
        }
    }

    public void BeginSplit()
    {
        splitTime = 0f;
        foreach (Piece piece in pieces.Values)
            if (piece.Settle != 0f) moving.Add(piece);
    }

    public bool CanSink(Hex hex) => cells.Contains(hex) && pieces.TryGetValue(hex, out Piece piece) && piece.SinkTime < 0f;

    public void Sink(Hex hex)
    {
        if (!CanSink(hex)) return;
        Piece piece = pieces[hex];
        piece.SinkTime = 0f;
        float tilt = 4f + (float)random.NextDouble() * 5f;
        float dir = (float)random.NextDouble() * 360f;
        piece.TiltTarget = Quaternion.AngleAxis(tilt, Quaternion.Euler(0f, dir, 0f) * Vector3.right);
        moving.Add(piece);
    }

    public bool IsSinking(Hex hex) => pieces.TryGetValue(hex, out Piece piece) && piece.SinkTime >= 0f && !piece.Gone;

    // How far the hex has dropped (0 to 1): 1 once it is under the sea.
    public float Sunk(Hex hex)
    {
        if (!pieces.TryGetValue(hex, out Piece piece) || piece.SinkTime < 0f) return 0f;
        float depth = Mathf.Max(1f, piece.Top - SeaLevel + FallDepth);
        return Mathf.Clamp01(-piece.Offset / depth);
    }

    public void Tick(float deltaTime)
    {
        deltaTime = Mathf.Max(0f, deltaTime);
        if (splitTime >= 0f) splitTime += deltaTime;
        var done = new List<Piece>();
        var changed = new HashSet<Hex>();
        foreach (Piece piece in moving)
        {
            if (piece.SinkTime >= 0f)
            {
                piece.SinkTime += deltaTime;
                float shake = Mathf.Clamp01(piece.SinkTime / ShakeTime);
                float fall = Mathf.Max(0f, piece.SinkTime - ShakeTime);
                float strength = piece.SinkTime < ShakeTime ? 0.18f * shake : 0.1f;
                piece.Jitter = new Vector2(Mathf.Sin(piece.SinkTime * 71f), Mathf.Sin(piece.SinkTime * 53f + 1.7f)) * strength;
                float depth = Mathf.Max(1f, piece.Top - SeaLevel + FallDepth);
                piece.Offset = piece.Settle - 0.15f * shake - 0.5f * Gravity * fall * fall;
                piece.Tilt = Quaternion.Slerp(Quaternion.identity, piece.TiltTarget, Mathf.Clamp01(fall / 0.8f));
                if (-piece.Offset >= depth)
                {
                    piece.Offset = -depth;
                    piece.Gone = true;
                    foreach (Renderer r in piece.Renderers) if (r != null) r.enabled = false;
                    done.Add(piece);
                }
            }
            else if (splitTime >= 0f)
            {
                float t = Mathf.Clamp01((splitTime - piece.SettleDelay) / (SplitTime - 0.6f));
                // Drops into place with a little bounce.
                float ease = 1f - Mathf.Pow(1f - t, 3f) + Mathf.Sin(t * Mathf.PI) * 0.12f;
                piece.Offset = piece.Settle * ease;
                piece.Jitter = t > 0f && t < 1f ? new Vector2(Mathf.Sin(splitTime * 60f + piece.Hex.Q), Mathf.Sin(splitTime * 47f + piece.Hex.R)) * 0.05f * (1f - t) : Vector2.zero;
                if (t >= 1f) { piece.Offset = piece.Settle; piece.Jitter = Vector2.zero; done.Add(piece); }
            }
            piece.Transform.localPosition = piece.Centre + new Vector3(piece.Jitter.x, piece.Offset, piece.Jitter.y);
            piece.Transform.localRotation = piece.Tilt;
            changed.Add(piece.Hex);
        }
        foreach (Piece piece in done) moving.Remove(piece);

        var falling = new HashSet<Panel>();
        foreach (Panel panel in panels)
        {
            if (panel.Time < 0f || panel.Time > KnockTime + 0.3f) continue;
            panel.Time += deltaTime;
            float t = Mathf.Clamp01(panel.Time / KnockTime);
            float bounce = panel.Time > KnockTime ? Mathf.Sin(Mathf.Clamp01((panel.Time - KnockTime) / 0.3f) * Mathf.PI) * 6f : 0f;
            panel.Angle = KnockAngle * t * t - bounce;
            falling.Add(panel);
        }

        foreach (Unit unit in units)
        {
            if (unit.Panel != null && falling.Contains(unit.Panel)) { Pose(unit); continue; }
            foreach (Hex cell in unit.Covers)
                if (changed.Contains(cell)) { Pose(unit); break; }
        }
    }

    // A unit rides the first hex under it that sank, otherwise its own.
    private Piece Carrier(Unit unit)
    {
        Piece home = null;
        foreach (Hex cell in unit.Covers)
        {
            if (!pieces.TryGetValue(cell, out Piece piece)) continue;
            if (piece.SinkTime >= 0f) return piece;
            if (home == null) home = piece;
        }
        return home;
    }

    private void Pose(Unit unit)
    {
        if (unit.Transform == null) return;
        Vector3 position = unit.Position;
        Quaternion rotation = unit.Rotation;
        if (unit.Panel != null && unit.Panel.Angle != 0f)
        {
            Quaternion knock = Quaternion.AngleAxis(unit.Panel.Angle, unit.Panel.Axis);
            position = unit.Panel.Pivot + knock * (position - unit.Panel.Pivot);
            rotation = knock * rotation;
        }
        Piece carrier = Carrier(unit);
        if (carrier != null)
        {
            Vector3 centre = carrier.Centre;
            position = centre + new Vector3(carrier.Jitter.x, carrier.Offset, carrier.Jitter.y) + carrier.Tilt * (position - centre);
            rotation = carrier.Tilt * rotation;
            if (carrier.Gone && unit.Transform.gameObject.activeSelf) unit.Transform.gameObject.SetActive(false);
        }
        unit.Transform.SetPositionAndRotation(position, rotation);
    }

    // ---- Where things are --------------------------------------------------------------------

    public float GroundHeight(float x, float z)
    {
        var ray = new Ray(new Vector3(x, groundTop + 50f, z), Vector3.down);
        return ground != null && ground.Raycast(ray, out RaycastHit hit, groundTop - SeaLevel + 300f) ? hit.point.y : float.NegativeInfinity;
    }

    // The highest of the ground and the paved slabs on it, where they were before the split.
    public float SurfaceHeight(float x, float z)
    {
        var ray = new Ray(new Vector3(x, groundTop + 50f, z), Vector3.down);
        float best = float.NegativeInfinity;
        foreach (Collider c in surfaces)
            if (c != null && c.Raycast(ray, out RaycastHit hit, groundTop - SeaLevel + 300f)) best = Mathf.Max(best, hit.point.y);
        return best;
    }

    // The top of a hex's ground at its middle, as it is now (settled or sinking).
    public Vector3 Surface(Hex hex) => At(hex, false);

    // The same as it stood before any sinking (settled): where the beam aims, even at a hex that
    // has gone under, so it shines into the hole rather than past it onto the hex in front.
    public Vector3 Top(Hex hex) => At(hex, true);

    // Where on a hex the villain stands and the beam aims: its middle, or for a part-hex on the
    // shore, the middle of its land.
    public Vector2 Stand(Hex hex) => stands.TryGetValue(hex, out Vector2 s) ? s : Grid.Centre(hex);

    private Vector3 At(Hex hex, bool resting)
    {
        Vector2 c = Stand(hex);
        float y = SurfaceHeight(c.x, c.y);
        if (float.IsNegativeInfinity(y)) y = SeaLevel;
        if (pieces.TryGetValue(hex, out Piece piece)) y += resting && piece.SinkTime >= 0f ? piece.Settle : piece.Offset;
        return new Vector3(c.x, y, c.y);
    }

    // The height light lands at, now: the ground (settled with its hex) where there is ground, the
    // water where there isn't, or where its hex has dropped away.
    public float LitHeight(float x, float z)
    {
        float y = SurfaceHeight(x, z);
        if (pieces.TryGetValue(Grid.At(x, z), out Piece piece))
        {
            if (piece.SinkTime >= 0f && piece.Offset < piece.Settle - 0.5f) return SeaLevel;
            y += piece.Offset;
        }
        return Mathf.Max(y, SeaLevel);
    }

    // Out on the water off a shore hex, in the given direction.
    public Vector3 WaterBeyond(Hex hex, int direction)
    {
        float angle = direction * 60f * Mathf.Deg2Rad;
        Vector2 c = Grid.Centre(hex) + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Grid.Width * 1.3f;
        return new Vector3(c.x, SeaLevel, c.y);
    }

    private static GameObject Find(Scene scene, string name)
    {
        if (!scene.IsValid() || !scene.isLoaded) return null;
        foreach (GameObject top in scene.GetRootGameObjects())
        {
            if (top.name == name) return top;
            foreach (Transform t in top.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t.gameObject;
        }
        return null;
    }

    public void Dispose()
    {
        foreach (Renderer r in hiddenSources) if (r != null) r.enabled = true;
        hiddenSources.Clear();
        foreach (Unit unit in units)
        {
            if (unit.Transform == null) continue;
            unit.Transform.SetPositionAndRotation(unit.Position, unit.Rotation);
            unit.Transform.gameObject.SetActive(unit.WasActive);
        }
        units.Clear();
        foreach (Object o in created) Destroy(o);
        created.Clear();
        if (root != null) Destroy(root.gameObject);
        root = null;
    }

    private static void Destroy(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Object.Destroy(o);
        else Object.DestroyImmediate(o);
    }
}
