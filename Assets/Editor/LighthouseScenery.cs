using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Dresses the lighthouse island the way IslandScenery dresses the main one: a few small grassy
// hills on its plateau, then the same trees, bushes, ferns, grass tufts, flowers and boulders,
// grown by the same code from a seed of its own.
//
// The island is one prefab (WorldModelSetup) whose grass top, hedges and lamps are each merged
// into one mesh, so nothing standing on it can be lifted. The hills are a grass skin of their
// own laid over the plateau, sunk just under the flat grass at their feet, and they only rise
// where nothing stands: clear of the hedges, lamps, cottage and tower, the plateau's edge, the
// ferry landing and the lighthouse entrance. Plants stand on the grass and the hills, and keep
// off the hedges (which have no colliders to find them by) and the way from the landing to
// the entrance.
//
// Everything goes on the island in the open scene, under "Lighthouse Scenery", and the prefab
// is left alone (its tests count its renderers). Tools > Orca > Regrow Lighthouse Scenery
// rebuilds all of it; run it again after rebuilding the island with WorldModelSetup.
public static partial class IslandScenery
{
    public const string LighthouseRootName = "Lighthouse Scenery";
    public const string LighthouseHillsName = "Lighthouse Hills";
    private const string LighthouseFolder = "Assets/Generated/LighthouseScenery";
    private const string LighthouseMeshesPath = LighthouseFolder + "/Lighthouse Scenery.asset";
    private const int LighthouseSeed = 7341;

    // The island's merged parts, which WorldModelSetup names after their material.
    private const string LighthouseGrass = "Lighthouse-aiStandardSurface3";
    private const string LighthouseHedges = "Lighthouse-maphedge";
    private const string LighthouseLamps = "Lighthouse-maplamp";
    private static readonly string[] LighthouseBuildings = { "Lighthouse-aiStandardSurface6", "Lighthouse-aiStandardSurface4" };

    // One zone for the whole island, planted thicker than the main island's lawns since there is
    // so little of it. Woods' clearings are a bigger pattern than the island, so its trees
    // scatter rather than clump. Windswept: more pines than on the main island.
    private static readonly Zone LighthouseZone = new Zone(-1e5f, 1e5f, -1e5f, 1e5f, 3f, 0.3f, 0.15f, 3f, 5f, 1.2f);
    private const float LighthouseTufts = 10f;
    // Its lamps are half the main island's height, so trees may come closer.
    private const float LighthouseLampClear = 3.5f;
    private const float HedgeFootprint = 0.4f;

    // ---- Hills ------------------------------------------------------------------------

    private const float HillCell = 0.5f;
    private const int HillCount = 6;
    // A hill's radius is as much room as it has, between these.
    private const float HillMinRadius = 2.5f, HillMaxRadius = 7.5f;
    // Height for each metre of radius: about 30 degrees at the steepest.
    private const float HillSteepness = 0.4f;
    private const float HillMinHeight = 1f, HillMaxHeight = 2.4f;
    // Hills flatten out over this much before anything in their way.
    private const float HillEdge = 1.25f;
    // How far the skin sits under the flat grass where there is no hill, so the two never fight.
    private const float HillSink = 0.08f;
    private const float HedgeMargin = 0.3f;

    // Kept clear for the player: around where the ferry lands and the stacking game starts, and
    // a lane between the two.
    private const float EntranceClear = 6f, LandingClear = 5f, LaneClear = 2f;

    [MenuItem("Tools/Orca/Regrow Lighthouse Scenery")]
    public static void RegrowLighthouse()
    {
        Debug.Log(GrowLighthouse());
    }

    public static string GrowLighthouse()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "[IslandScenery] Not in play mode: nothing would be kept.";
        GameObject island = GameObject.Find(WorldModelSetup.IslandName);
        LighthouseStacker stacker = island != null ? island.GetComponent<LighthouseStacker>() : null;
        if (stacker == null) return "[IslandScenery] No " + WorldModelSetup.IslandName + " in the open scene.";

        MeshCollider grass = null;
        var hedges = new List<MeshFilter>();
        var lamps = new List<MeshFilter>();
        var buildings = new List<Renderer>();
        foreach (Transform part in island.transform)
        {
            string name = part.name;
            if (name.StartsWith(LighthouseGrass, StringComparison.Ordinal)) part.TryGetComponent(out grass);
            else if (name.StartsWith(LighthouseHedges, StringComparison.Ordinal) && part.TryGetComponent(out MeshFilter hedge)) hedges.Add(hedge);
            else if (name.StartsWith(LighthouseLamps, StringComparison.Ordinal) && part.TryGetComponent(out MeshFilter lamp)) lamps.Add(lamp);
            else if (Array.Exists(LighthouseBuildings, prefix => name.StartsWith(prefix, StringComparison.Ordinal)) && part.TryGetComponent(out Renderer building)) buildings.Add(building);
        }
        if (grass == null || !grass.TryGetComponent(out Renderer grassRenderer)) return "[IslandScenery] No " + LighthouseGrass + " grass on the lighthouse island.";
        Transform tower = LighthouseTower(stacker);
        if (tower != null) buildings.AddRange(tower.GetComponentsInChildren<Renderer>(true));

        Undo.SetCurrentGroupName("Regrow Lighthouse Scenery");
        int undo = Undo.GetCurrentGroup();
        Transform old = island.transform.Find(LighthouseRootName);
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        Physics.SyncTransforms();

        // The island on a grid: where the grass top is open to the sky and how high it is there
        // (it isn't quite level), and where the hedges and lamps stand.
        var grid = new HillGrid(grassRenderer.bounds, HillCell);
        bool[,] open = OpenGrass(grass, grid, out float[,] ground, out float top);
        if (float.IsNegativeInfinity(top)) return "[IslandScenery] The lighthouse island's grass has no top.";
        bool[,] hedgeCells = grid.Rasterize(hedges);
        bool[,] lampCells = grid.Rasterize(lamps);
        List<Vector3> lampSpots = grid.Clumps(lampCells, top);
        Vector3 entrance = stacker.Entrance, landing = stacker.PierLanding;

        // Hills.
        var random = new System.Random(LighthouseSeed);
        bool[,] blocked = new bool[grid.Nx, grid.Nz];
        bool[,] nearHedge = grid.Within(hedgeCells, HedgeMargin);
        for (int i = 0; i < grid.Nx; i++)
        {
            for (int j = 0; j < grid.Nz; j++)
            {
                Vector3 at = grid.Point(i, j, 0f);
                blocked[i, j] = !open[i, j] || nearHedge[i, j] || lampCells[i, j] || FlatDistance(at - entrance) < EntranceClear || FlatDistance(at - landing) < LandingClear;
            }
        }
        float[,] heights = Hills(grid, blocked, random, out int hillCount);
        var root = new GameObject(LighthouseRootName);
        Undo.RegisterCreatedObjectUndo(root, "Regrow Lighthouse Scenery");
        root.transform.SetParent(island.transform, false);
        Mesh hillMesh = HillMesh(grid, heights, ground, root.transform.worldToLocalMatrix, out float hillArea);
        MeshCollider hillCollider = null;
        if (hillMesh != null)
        {
            var hills = new GameObject(LighthouseHillsName);
            hills.transform.SetParent(root.transform, false);
            hills.layer = grass.gameObject.layer;
            hills.AddComponent<MeshFilter>().sharedMesh = hillMesh;
            var renderer = hills.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = grassRenderer.sharedMaterials;
            renderer.shadowCastingMode = grassRenderer.shadowCastingMode;
            renderer.receiveShadows = grassRenderer.receiveShadows;
            renderer.renderingLayerMask = grassRenderer.renderingLayerMask;
            renderer.lightProbeUsage = grassRenderer.lightProbeUsage;
            renderer.reflectionProbeUsage = grassRenderer.reflectionProbeUsage;
            hillCollider = hills.AddComponent<MeshCollider>();
            hillCollider.sharedMesh = hillMesh;
        }
        Physics.SyncTransforms();

        // Plants, by the main island's rules.
        var grounds = new List<Collider> { grass };
        if (hillCollider != null) grounds.Add(hillCollider);
        Bounds land = grid.BoundsOf(open, top);
        land.Encapsulate(land.center + Vector3.up * (HillMaxHeight + 1f));
        var site = new Site(grounds, island.transform, land, island.transform.position.y, grassRenderer)
        {
            ZoneList = new[] { LighthouseZone },
            Fallback = LighthouseZone,
            WoodDensity = float.MaxValue,
            LampClearance = LighthouseLampClear,
            TuftDensity = LighthouseTufts,
            // The hedges are knee-high: only a plant's own footprint keeps off them, and crowns
            // may spread over them.
            Occupied = (at, reach) => grid.Any(hedgeCells, at, Mathf.Min(reach, HedgeFootprint)),
        };
        foreach (Renderer building in buildings) site.AddBuilding(building.bounds);
        foreach (Vector3 lamp in lampSpots) site.AddLamp(lamp);
        site.KeepClear(entrance, EntranceClear);
        site.KeepClear(landing, LandingClear);
        float lane = FlatDistance(entrance - landing);
        for (float along = 0f; along <= lane; along += LaneClear * 0.8f) site.KeepClear(Vector3.Lerp(landing, entrance, along / lane), LaneClear);

        var patches = new Dictionary<(int, int, Layer), Builder>();
        var plants = new List<Plant>();
        int[] counts = GrowPlants(site, patches, plants, random);

        // Meshes, one file: the hills first.
        if (AssetDatabase.IsValidFolder(LighthouseFolder)) AssetDatabase.DeleteAsset(LighthouseFolder);
        Directory.CreateDirectory(LighthouseFolder);
        AssetDatabase.Refresh();
        bool assetExists = false;
        if (hillMesh != null)
        {
            AssetDatabase.CreateAsset(hillMesh, LighthouseMeshesPath);
            assetExists = true;
        }
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null) material = SceneryMaterial(island.transform);
        int vertices = MakeSceneryObjects(root.transform, patches, plants, LighthouseMeshesPath, assetExists, material, grassRenderer);
        Physics.SyncTransforms();
        Undo.CollapseUndoOperations(undo);
        EditorSceneManager.MarkSceneDirty(root.scene);

        return "[IslandScenery] Lighthouse island: " + hillCount + " hills over " + Mathf.RoundToInt(hillArea) + " of its " + Mathf.RoundToInt(grid.Area(open)) + " m2 of open grass. Grew " + counts[0] + " broadleaves, " +
            counts[1] + " poplars, " + counts[2] + " pines, " + counts[3] + " bushes, " + counts[4] + " ferns, " + counts[5] + " grass tufts, " +
            counts[6] + " flowers and " + counts[7] + " boulders. " + patches.Count + " meshes, " + (vertices + (hillMesh != null ? hillMesh.vertexCount : 0)) + " vertices.";
    }

    // The tower the stacking game hides: LighthouseStacker's own reference to it.
    internal static Transform LighthouseTower(LighthouseStacker stacker)
    {
        var tower = new SerializedObject(stacker).FindProperty("originalTower").objectReferenceValue as Transform;
        return tower != null ? tower : stacker.transform.Find("Supplied lighthouse");
    }

    private static float FlatDistance(Vector3 offset)
    {
        return new Vector2(offset.x, offset.z).magnitude;
    }

    // Grass with nothing over it: the first thing straight down from the sky is the grass's
    // gently sloping top. Also the height of that top at every point (the middle height where
    // there is none) and its highest.
    private static bool[,] OpenGrass(Collider grass, HillGrid grid, out float[,] ground, out float top)
    {
        var open = new bool[grid.Nx, grid.Nz];
        ground = new float[grid.Nx, grid.Nz];
        var found = new bool[grid.Nx, grid.Nz];
        var heights = new List<float>();
        top = float.NegativeInfinity;
        for (int i = 0; i < grid.Nx; i++)
        {
            for (int j = 0; j < grid.Nz; j++)
            {
                var down = new Ray(grid.Point(i, j, grid.Top + 50f), Vector3.down);
                if (!grass.Raycast(down, out RaycastHit turf, 200f) || turf.normal.y < 0.9f) continue;
                found[i, j] = true;
                ground[i, j] = turf.point.y;
                heights.Add(turf.point.y);
                top = Mathf.Max(top, turf.point.y);
                open[i, j] = Physics.Raycast(down, out RaycastHit hit, 200f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) && hit.collider == grass;
            }
        }
        if (heights.Count == 0) return open;
        heights.Sort();
        float middle = heights[heights.Count / 2];
        for (int i = 0; i < grid.Nx; i++)
        {
            for (int j = 0; j < grid.Nz; j++)
            {
                if (!found[i, j]) ground[i, j] = middle;
            }
        }
        return open;
    }

    // Picks the roomiest open spots one at a time, each hill as wide as its room, a little
    // longer one way than the other, and adds them up. Returns each grid point's height above
    // the plateau.
    private static float[,] Hills(HillGrid grid, bool[,] blocked, System.Random random, out int count)
    {
        float[,] clearance = grid.Distance(blocked);
        var taken = (bool[,])blocked.Clone();
        var hills = new List<(Vector2 centre, float radius, float height, float turn, float stretch)>();
        for (int n = 0; n < HillCount; n++)
        {
            float[,] room = n == 0 ? clearance : grid.Distance(taken);
            float best = 0f;
            int bi = 0, bj = 0;
            for (int i = 0; i < grid.Nx; i++)
            {
                for (int j = 0; j < grid.Nz; j++)
                {
                    if (room[i, j] > best)
                    {
                        best = room[i, j];
                        bi = i;
                        bj = j;
                    }
                }
            }
            if (best < HillMinRadius) break;
            // Its foot may run a little past its room: the edge flattens it there.
            float radius = Mathf.Min(best * 1.3f, HillMaxRadius);
            float height = Mathf.Clamp(radius * HillSteepness * Range(random, 0.8f, 1.15f), HillMinHeight, HillMaxHeight);
            Vector3 centre = grid.Point(bi, bj, 0f);
            hills.Add((new Vector2(centre.x, centre.z), radius, height, Range(random, 0f, Mathf.PI), Range(random, 1f, 1.4f)));
            // The next hill goes elsewhere, though it may run into this one's foot.
            for (int i = 0; i < grid.Nx; i++)
            {
                for (int j = 0; j < grid.Nz; j++)
                {
                    Vector3 at = grid.Point(i, j, 0f);
                    if ((new Vector2(at.x, at.z) - new Vector2(centre.x, centre.z)).sqrMagnitude < radius * radius * 0.6f) taken[i, j] = true;
                }
            }
        }
        count = hills.Count;

        float seed = Range(random, 0f, 100f);
        var heights = new float[grid.Nx, grid.Nz];
        for (int i = 0; i < grid.Nx; i++)
        {
            for (int j = 0; j < grid.Nz; j++)
            {
                Vector3 at = grid.Point(i, j, 0f);
                float h = 0f;
                foreach ((Vector2 centre, float radius, float height, float turn, float stretch) in hills)
                {
                    Vector2 offset = new Vector2(at.x, at.z) - centre;
                    float along = (offset.x * Mathf.Cos(turn) + offset.y * Mathf.Sin(turn)) / radius;
                    float across = (-offset.x * Mathf.Sin(turn) + offset.y * Mathf.Cos(turn)) * stretch / radius;
                    float t = along * along + across * across;
                    if (t < 1f) h += height * (1f - t) * (1f - t);
                }
                if (h <= 0f) continue;
                h *= 0.85f + 0.3f * Mathf.PerlinNoise(at.x * 0.12f + seed, at.z * 0.12f - seed);
                heights[i, j] = h * Mathf.SmoothStep(0f, 1f, clearance[i, j] / HillEdge);
            }
        }
        return heights;
    }

    // The hills as one smooth skin, in the space toLocal leads into: every grid square with a
    // hill at any corner, the rest left out. Where the hill runs out the skin is HillSink under
    // the flat grass. Null if there are no hills.
    private static Mesh HillMesh(HillGrid grid, float[,] heights, float[,] ground, Matrix4x4 toLocal, out float area)
    {
        area = 0f;
        var index = new int[grid.Nx, grid.Nz];
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        for (int i = 0; i < grid.Nx; i++)
        {
            for (int j = 0; j < grid.Nz; j++) index[i, j] = -1;
        }
        for (int i = 0; i + 1 < grid.Nx; i++)
        {
            for (int j = 0; j + 1 < grid.Nz; j++)
            {
                if (heights[i, j] <= 0.001f && heights[i + 1, j] <= 0.001f && heights[i, j + 1] <= 0.001f && heights[i + 1, j + 1] <= 0.001f) continue;
                area += grid.Cell * grid.Cell;
                int a = Corner(i, j), b = Corner(i + 1, j), c = Corner(i, j + 1), d = Corner(i + 1, j + 1);
                triangles.Add(a);
                triangles.Add(c);
                triangles.Add(b);
                triangles.Add(b);
                triangles.Add(c);
                triangles.Add(d);
            }
        }
        if (triangles.Count == 0) return null;

        int Corner(int i, int j)
        {
            if (index[i, j] >= 0) return index[i, j];
            Vector3 at = grid.Point(i, j, ground[i, j] + heights[i, j] - HillSink);
            // The slope from the neighbouring heights, for smooth shading.
            float dx = (heights[Mathf.Min(i + 1, grid.Nx - 1), j] - heights[Mathf.Max(i - 1, 0), j]) / (2f * grid.Cell);
            float dz = (heights[i, Mathf.Min(j + 1, grid.Nz - 1)] - heights[i, Mathf.Max(j - 1, 0)]) / (2f * grid.Cell);
            index[i, j] = positions.Count;
            positions.Add(toLocal.MultiplyPoint3x4(at));
            normals.Add(toLocal.MultiplyVector(new Vector3(-dx, 1f, -dz)).normalized);
            uvs.Add(new Vector2(at.x, at.z));
            return index[i, j];
        }

        var mesh = new Mesh { name = LighthouseHillsName };
        mesh.indexFormat = positions.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.SetVertices(positions);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        return mesh;
    }

    // A level grid of points over the island, HillCell apart.
    private sealed class HillGrid
    {
        public readonly float X0, Z0, Cell, Top;
        public readonly int Nx, Nz;

        public HillGrid(Bounds over, float cell)
        {
            Cell = cell;
            X0 = over.min.x - cell * 2f;
            Z0 = over.min.z - cell * 2f;
            Nx = Mathf.CeilToInt(over.size.x / cell) + 5;
            Nz = Mathf.CeilToInt(over.size.z / cell) + 5;
            Top = over.max.y;
        }

        public Vector3 Point(int i, int j, float y)
        {
            return new Vector3(X0 + i * Cell, y, Z0 + j * Cell);
        }

        private bool IndexOf(Vector3 at, out int i, out int j)
        {
            i = Mathf.RoundToInt((at.x - X0) / Cell);
            j = Mathf.RoundToInt((at.z - Z0) / Cell);
            return i >= 0 && j >= 0 && i < Nx && j < Nz;
        }

        // The points any of the meshes' vertices are nearest, seen from above.
        public bool[,] Rasterize(List<MeshFilter> meshes)
        {
            var cells = new bool[Nx, Nz];
            foreach (MeshFilter filter in meshes)
            {
                if (filter.sharedMesh == null) continue;
                Matrix4x4 toWorld = filter.transform.localToWorldMatrix;
                foreach (Vector3 vertex in filter.sharedMesh.vertices)
                {
                    if (IndexOf(toWorld.MultiplyPoint3x4(vertex), out int i, out int j)) cells[i, j] = true;
                }
            }
            return cells;
        }

        // The middle of each separate clump of marked points, at height y.
        public List<Vector3> Clumps(bool[,] cells, float y)
        {
            var seen = new bool[Nx, Nz];
            var clumps = new List<Vector3>();
            var queue = new Queue<Vector2Int>();
            for (int i = 0; i < Nx; i++)
            {
                for (int j = 0; j < Nz; j++)
                {
                    if (!cells[i, j] || seen[i, j]) continue;
                    Vector2 sum = Vector2.zero;
                    int count = 0;
                    seen[i, j] = true;
                    queue.Enqueue(new Vector2Int(i, j));
                    while (queue.Count > 0)
                    {
                        Vector2Int at = queue.Dequeue();
                        sum += at;
                        count++;
                        for (int di = -2; di <= 2; di++)
                        {
                            for (int dj = -2; dj <= 2; dj++)
                            {
                                int ni = at.x + di, nj = at.y + dj;
                                if (ni < 0 || nj < 0 || ni >= Nx || nj >= Nz || seen[ni, nj] || !cells[ni, nj]) continue;
                                seen[ni, nj] = true;
                                queue.Enqueue(new Vector2Int(ni, nj));
                            }
                        }
                    }
                    clumps.Add(new Vector3(X0 + sum.x / count * Cell, y, Z0 + sum.y / count * Cell));
                }
            }
            return clumps;
        }

        // Every point within reach of a marked one.
        public bool[,] Within(bool[,] cells, float reach)
        {
            float[,] distance = Distance(cells);
            var near = new bool[Nx, Nz];
            for (int i = 0; i < Nx; i++)
            {
                for (int j = 0; j < Nz; j++) near[i, j] = distance[i, j] <= reach;
            }
            return near;
        }

        // Whether any marked point is within reach of a spot.
        public bool Any(bool[,] cells, Vector3 at, float reach)
        {
            int steps = Mathf.CeilToInt(reach / Cell);
            IndexOf(at, out int ci, out int cj);
            for (int i = Mathf.Max(0, ci - steps); i <= Mathf.Min(Nx - 1, ci + steps); i++)
            {
                for (int j = Mathf.Max(0, cj - steps); j <= Mathf.Min(Nz - 1, cj + steps); j++)
                {
                    if (!cells[i, j]) continue;
                    float x = X0 + i * Cell - at.x, z = Z0 + j * Cell - at.z;
                    if (x * x + z * z <= reach * reach) return true;
                }
            }
            return false;
        }

        // How far each point is from the nearest marked one (or the grid's edge), in metres:
        // a two-pass chamfer, close enough to the true distance for shaping hills.
        public float[,] Distance(bool[,] cells)
        {
            const float Far = 1e6f;
            float straight = Cell, diagonal = Cell * 1.41421356f;
            var d = new float[Nx, Nz];
            for (int i = 0; i < Nx; i++)
            {
                for (int j = 0; j < Nz; j++) d[i, j] = cells[i, j] || i == 0 || j == 0 || i == Nx - 1 || j == Nz - 1 ? 0f : Far;
            }
            for (int j = 1; j < Nz; j++)
            {
                for (int i = 1; i < Nx; i++)
                {
                    float v = d[i, j];
                    v = Mathf.Min(v, d[i - 1, j] + straight);
                    v = Mathf.Min(v, d[i, j - 1] + straight);
                    v = Mathf.Min(v, d[i - 1, j - 1] + diagonal);
                    if (i + 1 < Nx) v = Mathf.Min(v, d[i + 1, j - 1] + diagonal);
                    d[i, j] = v;
                }
            }
            for (int j = Nz - 2; j >= 0; j--)
            {
                for (int i = Nx - 2; i >= 0; i--)
                {
                    float v = d[i, j];
                    v = Mathf.Min(v, d[i + 1, j] + straight);
                    v = Mathf.Min(v, d[i, j + 1] + straight);
                    v = Mathf.Min(v, d[i + 1, j + 1] + diagonal);
                    if (i > 0) v = Mathf.Min(v, d[i - 1, j + 1] + diagonal);
                    d[i, j] = v;
                }
            }
            return d;
        }

        // How much ground the marked points stand for, in square metres.
        public float Area(bool[,] cells)
        {
            int count = 0;
            foreach (bool cell in cells) if (cell) count++;
            return count * Cell * Cell;
        }

        // Around the marked points, from y up.
        public Bounds BoundsOf(bool[,] cells, float y)
        {
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            for (int i = 0; i < Nx; i++)
            {
                for (int j = 0; j < Nz; j++)
                {
                    if (!cells[i, j]) continue;
                    minX = Mathf.Min(minX, X0 + i * Cell);
                    maxX = Mathf.Max(maxX, X0 + i * Cell);
                    minZ = Mathf.Min(minZ, Z0 + j * Cell);
                    maxZ = Mathf.Max(maxZ, Z0 + j * Cell);
                }
            }
            var bounds = new Bounds();
            bounds.SetMinMax(new Vector3(minX - Cell, y - 1f, minZ - Cell), new Vector3(maxX + Cell, y, maxZ + Cell));
            return bounds;
        }
    }
}
