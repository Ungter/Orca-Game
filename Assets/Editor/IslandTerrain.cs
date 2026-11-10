using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Gives the flat island its relief: whole districts sit at different heights, joined by slopes.
//
//   The harbour (the start house, the main road, the dock)             stays at sea level.
//   The lower town (the well, the square and its fountain)             rises 5 m.
//   The upper town (both big houses, the north road, the magician's dell) rises 9.5 m.
//   Wooded heights around the dell, and a ridge on the south-west lobe   rise 5.5 m more.
//   East of the east road the town falls away to the beach.
//
// The model's ground block is a 122-vertex slab, so it is re-cut finely and reshaped here (the
// model itself is untouched: the scene's ground just points at the new mesh), its steep sides
// drawn as rock. Everything standing on the island then follows the ground: buildings, trees,
// lamps, posts and props are lifted whole by the rise under them, a hedge is lifted and leant to
// the slope it stands on, and whatever runs along the ground across a slope (the paths and their
// gravel, fence rails) is bent to it.
//
// A level is a border drawn on the map and then blurred, so each slope is an S-curve: flat at
// the top and bottom, steepest halfway. Borders cross the paths at right angles, so paths climb
// head-on and never lean sideways, and they keep clear of every building, so floors stay level.
//
// Run once on the flat island (Tools > Orca > Raise Island Terrain). To reshape it, discard the
// changes to the scene and to ScenePaving's meshes, which it re-lays on the slopes
// (git checkout Assets/Scenes/SampleScene.unity Assets/Generated/sideWalkAlch), edit the numbers
// and run again; it refuses to raise an island that is already raised.
public static class IslandTerrain
{
    public const string GroundName = "IslandDemo:pCube1";
    public const string Folder = "Assets/Generated/IslandTerrain";
    private const string GroundMeshPath = Folder + "/Ground.asset";
    private const string CliffMaterialPath = "Assets/Materials/CliffMat.mat";
    private const string CliffTexturePath = "Assets/Materials/Cliff Texture.png";

    // A level: everything inside its border rises by Rise. The border is box-blurred twice by
    // Blend metres either side, so the slope is about 4 * Blend long, the ground is level again
    // 2 * Blend from the border, and the steepest point is Rise / (2 * Blend + 1).
    private readonly struct Level
    {
        public readonly float Rise;
        public readonly int Blend;
        public readonly Func<float, float, bool> Inside;

        public Level(float rise, int blend, Func<float, float, bool> inside)
        {
            Rise = rise;
            Blend = blend;
            Inside = inside;
        }
    }

    // The shore side of the east road: east of this the town falls to the beach.
    private const float ShoreX = 99f;

    // The lower town's edge. It crosses the west diagonal road and the path from the square to
    // the east gate at right angles, runs south of the well, then turns north along the shore
    // side of the east road.
    private static readonly Vector2[] LowerTownEdge =
    {
        new Vector2(-117.5f, -117f), new Vector2(-31.2f, -57f), new Vector2(36.2f, -57f),
        new Vector2(54.7f, -32f), new Vector2(ShoreX, -32f), new Vector2(ShoreX, 100f),
        new Vector2(400f, 100f), new Vector2(400f, 600f), new Vector2(-400f, 600f), new Vector2(-400f, -117f),
    };

    private static readonly Level[] Levels =
    {
        // Lower town: 18 degrees at the steepest.
        new Level(5f, 7, (x, z) => InsideOutline(LowerTownEdge, x, z)),
        // Upper town, from between the square and the two big houses northwards: 22 degrees.
        new Level(4.5f, 5, (x, z) => z > 26.5f && (x < ShoreX || z > 100f)),
        // The heights west, north and east of the magician's dell, and the south-west ridge: 23 degrees.
        new Level(5.5f, 6, (x, z) => (x < -18f && z > 124f) || z > 178f || (x > 65f && z > 124f)
            || (x < -72f && z < -30f && z > -150f)),
    };

    private static bool InsideOutline(Vector2[] outline, float x, float z)
    {
        bool inside = false;
        for (int i = 0, j = outline.Length - 1; i < outline.Length; j = i++)
        {
            Vector2 a = outline[i], b = outline[j];
            if ((a.y > z) != (b.y > z) && x < (b.x - a.x) * (z - a.y) / (b.y - a.y) + a.x) inside = !inside;
        }
        return inside;
    }

    // Ground and path edges are cut no longer than these, so the surfaces follow the curves.
    private const float GroundEdge = 3f;
    // Corners of the slab closer together than this are one corner.
    private const float GroundWeld = 0.4f;
    private const float PathEdge = 2.5f;
    // Only meshes this simple are re-cut; anything finer already follows a curve as it is.
    private const int CoarseMesh = 2000;
    private const int MostTriangles = 1500000;
    // The slab's top is above this, its underwater rim below RimY; the sides stretch between.
    private const float TopY = -1.2f;
    private const float RimY = -8f;
    // Faces steeper than this (normal.y) are rock.
    private const float RockNormalY = 0.62f;

    // Things this small move as one piece; so does anything named below, up to MaxRigidSpan.
    private const float SmallSpan = 8f;
    private const float MaxRigidSpan = 95f;
    // A single mesh longer than this that crosses a slope is bent rather than lifted.
    private const float BendSpan = 3f;
    private const float LevelTolerance = 0.04f;
    private static readonly string[] RigidNames = { "tree", "lamp", "building", "testmodel", "house", "statue", "grave" };
    private static readonly string[] SkippedNames = { "ocean", "main camera", "directional light", "global volume", "inventory system", "island scenery" };
    // Each child of these is one plant. A shrub is lifted whole however wide it sprawls; a hedge
    // is a long row, so it is also leant to the slope under it rather than left jutting out.
    private const string ShrubGroup = "shrubs";
    private const string HedgeGroup = "hedges";
    // ScenePaving re-lays these itself once its slabs have moved.
    private const string ScenePavingGroup = "sideWalkAlch";
    private const string SpillPrefix = "Sidewalk Spill";

    public static bool IsRaised()
    {
        GameObject ground = GameObject.Find(GroundName);
        MeshFilter filter = ground != null ? ground.GetComponent<MeshFilter>() : null;
        return filter != null && AssetDatabase.GetAssetPath(filter.sharedMesh) == GroundMeshPath;
    }

    [MenuItem("Tools/Orca/Raise Island Terrain")]
    public static void Raise()
    {
        Debug.Log(Apply());
    }

    // Returns a report of what was done.
    public static string Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "[IslandTerrain] Not in play mode: nothing would be kept.";
        GameObject groundObject = GameObject.Find(GroundName);
        MeshFilter groundFilter = groundObject != null ? groundObject.GetComponent<MeshFilter>() : null;
        if (groundFilter == null || groundFilter.sharedMesh == null) return "[IslandTerrain] No " + GroundName + " ground in the open scene.";
        if (IsRaised()) return "[IslandTerrain] The island is already raised. Discard the scene's changes first to reshape it.";
        GameObject paved = GameObject.Find(ScenePavingGroup);
        if (paved != null)
        {
            foreach (MeshFilter slab in paved.GetComponentsInChildren<MeshFilter>(true))
            {
                // A slab is a plain box until this bends it; one that isn't was bent by an earlier run.
                if (!slab.name.StartsWith(SpillPrefix, StringComparison.Ordinal) && slab.sharedMesh != null && slab.sharedMesh.vertexCount > 24)
                {
                    return "[IslandTerrain] " + ScenePavingGroup + "/" + slab.name + " is still bent from an earlier run. Discard the changes under Assets/Generated/" +
                        ScenePavingGroup + " as well as the scene's, then run again.";
                }
            }
        }

        var report = new StringBuilder("[IslandTerrain] ");
        Bounds land = groundObject.GetComponent<Renderer>().bounds;
        var field = new HeightField(land);

        var plan = new Plan();
        foreach (GameObject root in groundObject.scene.GetRootGameObjects())
        {
            Visit(root.transform, groundObject.transform, field, plan, false);
        }

        if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
        Directory.CreateDirectory(Folder);
        AssetDatabase.Refresh();

        Undo.SetCurrentGroupName("Raise Island Terrain");
        int group = Undo.GetCurrentGroup();

        // The ground.
        Mesh ground = BuildGround(groundFilter, field);
        AssetDatabase.CreateAsset(ground, GroundMeshPath);
        MeshRenderer groundRenderer = groundObject.GetComponent<MeshRenderer>();
        Undo.RecordObject(groundFilter, "Raise Island Terrain");
        Undo.RecordObject(groundRenderer, "Raise Island Terrain");
        groundFilter.sharedMesh = ground;
        groundRenderer.sharedMaterials = new[] { groundRenderer.sharedMaterial, CliffMaterial(groundRenderer.sharedMaterial) };
        if (groundObject.TryGetComponent(out MeshCollider groundCollider))
        {
            Undo.RecordObject(groundCollider, "Raise Island Terrain");
            groundCollider.sharedMesh = ground;
        }
        report.Append("Ground re-cut to ").Append(ground.vertexCount).Append(" vertices, ")
            .Append(ground.GetSubMesh(1).indexCount / 3).Append(" rock faces. ");

        // What runs along the ground across a slope is bent to it.
        var usedNames = new HashSet<string>();
        var relaid = new List<string>();
        foreach (MeshFilter filter in plan.Bends)
        {
            Mesh source = filter.sharedMesh;
            if (filter.transform.parent != null && filter.transform.parent.name == ScenePavingGroup) relaid.Add(AssetDatabase.GetAssetPath(source));
            Mesh bent = Bend(filter, field);
            AssetDatabase.CreateAsset(bent, Folder + "/" + UniqueFileName(filter, usedNames) + ".asset");
            Undo.RecordObject(filter, "Raise Island Terrain");
            filter.sharedMesh = bent;
            if (filter.TryGetComponent(out MeshCollider collider) && collider.sharedMesh == source)
            {
                Undo.RecordObject(collider, "Raise Island Terrain");
                collider.sharedMesh = bent;
            }
            else if (filter.TryGetComponent(out BoxCollider box))
            {
                // A box can't follow the bend; the bent mesh itself is walked on instead.
                Undo.DestroyObjectImmediate(box);
                Undo.AddComponent<MeshCollider>(filter.gameObject).sharedMesh = bent;
            }
        }

        // Everything else is lifted whole. Positions are worked out from where each thing stood
        // before any of them moved, parents first, so a child never rides its parent's lift.
        var stood = new Dictionary<Transform, Vector3>();
        var moved = new List<UnityEngine.Object>();
        foreach ((Transform transform, float rise) in plan.Lifts)
        {
            stood[transform] = transform.position;
            moved.Add(transform);
        }
        Undo.RecordObjects(moved.ToArray(), "Raise Island Terrain");
        int lifted = 0;
        foreach ((Transform transform, float rise) in plan.Lifts)
        {
            transform.position = stood[transform] + Vector3.up * rise;
            if (rise > 0.001f) lifted++;
        }
        foreach ((Transform transform, Vector3 pivot, float rise, Vector3 normal) in plan.Leans)
        {
            Undo.RecordObject(transform, "Raise Island Terrain");
            Quaternion lean = Quaternion.FromToRotation(Vector3.up, normal);
            Vector3 from = transform.position;
            transform.rotation = lean * transform.rotation;
            transform.position = pivot + Vector3.up * rise + lean * (from - pivot);
            if (rise > 0.001f) lifted++;
        }
        Physics.SyncTransforms();
        Undo.CollapseUndoOperations(group);

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(groundObject.scene);

        // ScenePaving rebuilds its meshes inside the files they already have, and the editor goes
        // on drawing the old shape of a mesh rebuilt that way until it restarts (the path would
        // lie under the lawn it now climbs). Its gravel, and the paving of any slab bent above,
        // are removed first, so it lays them as new meshes.
        if (paved != null)
        {
            foreach (MeshFilter spill in paved.GetComponentsInChildren<MeshFilter>(true))
            {
                if (spill.name.StartsWith(SpillPrefix, StringComparison.Ordinal) && spill.sharedMesh != null) relaid.Add(AssetDatabase.GetAssetPath(spill.sharedMesh));
            }
        }
        foreach (string path in relaid)
        {
            if (path.StartsWith("Assets/Generated/", StringComparison.Ordinal) && !path.StartsWith(Folder + "/", StringComparison.Ordinal)) AssetDatabase.DeleteAsset(path);
        }
        EditorApplication.ExecuteMenuItem("Tools/Orca/Repave Scene Sidewalks");

        report.Append(lifted).Append(" objects lifted, ").Append(plan.Bends.Count).Append(" meshes bent to the slopes.");
        if (plan.Tilted.Count > 0)
        {
            report.Append("\nStanding across a slope (lifted whole by the rise at their middle):");
            foreach (string line in plan.Tilted) report.Append("\n  ").Append(line);
        }
        return report.ToString();
    }

    // The rise at a spot, for anything placed on the island afterwards.
    public static float RiseAt(Bounds land, float x, float z)
    {
        return new HeightField(land).At(x, z);
    }

    // ---- Heights ----------------------------------------------------------------------

    public sealed class HeightField
    {
        private readonly float x0, z0;
        private readonly int nx, nz;
        private readonly float[] heights;

        public HeightField(Bounds land)
        {
            const int margin = 40;
            x0 = Mathf.Floor(land.min.x) - margin;
            z0 = Mathf.Floor(land.min.z) - margin;
            nx = Mathf.CeilToInt(land.size.x) + 2 * margin + 2;
            nz = Mathf.CeilToInt(land.size.z) + 2 * margin + 2;
            heights = new float[nx * nz];
            foreach (Level level in Levels)
            {
                var layer = new float[nx * nz];
                for (int j = 0; j < nz; j++)
                {
                    for (int i = 0; i < nx; i++)
                    {
                        layer[j * nx + i] = level.Inside(x0 + i, z0 + j) ? level.Rise : 0f;
                    }
                }
                layer = BoxBlur(BoxBlur(layer, nx, nz, level.Blend), nx, nz, level.Blend);
                for (int k = 0; k < heights.Length; k++) heights[k] += layer[k];
            }
        }

        public float At(float x, float z)
        {
            float fx = Mathf.Clamp(x - x0, 0f, nx - 1.001f), fz = Mathf.Clamp(z - z0, 0f, nz - 1.001f);
            int i = (int)fx, j = (int)fz;
            float tx = fx - i, tz = fz - j;
            float a = Mathf.Lerp(heights[j * nx + i], heights[j * nx + i + 1], tx);
            float b = Mathf.Lerp(heights[(j + 1) * nx + i], heights[(j + 1) * nx + i + 1], tx);
            return Mathf.Lerp(a, b, tz);
        }

        // The least rise within reach of a spot.
        public float Lowest(float x, float z, float reach)
        {
            float lowest = At(x, z);
            for (int i = 0; i < 16; i++)
            {
                float around = i * Mathf.PI / 4f, out_ = i < 8 ? reach : reach * 0.5f;
                lowest = Mathf.Min(lowest, At(x + Mathf.Cos(around) * out_, z + Mathf.Sin(around) * out_));
            }
            return lowest;
        }

        // How fast the rise changes along x and z.
        public Vector2 SlopeAt(float x, float z)
        {
            return new Vector2((At(x + 0.5f, z) - At(x - 0.5f, z)), (At(x, z + 0.5f) - At(x, z - 0.5f)));
        }

        // The spread of rises under a footprint.
        public float Spread(Bounds bounds)
        {
            float low = float.MaxValue, high = float.MinValue;
            int stepsX = Mathf.Clamp(Mathf.CeilToInt(bounds.size.x / 2f), 1, 40), stepsZ = Mathf.Clamp(Mathf.CeilToInt(bounds.size.z / 2f), 1, 40);
            for (int j = 0; j <= stepsZ; j++)
            {
                for (int i = 0; i <= stepsX; i++)
                {
                    float h = At(bounds.min.x + bounds.size.x * i / stepsX, bounds.min.z + bounds.size.z * j / stepsZ);
                    low = Mathf.Min(low, h);
                    high = Mathf.Max(high, h);
                }
            }
            return high - low;
        }

        private static float[] BoxBlur(float[] from, int nx, int nz, int reach)
        {
            var across = new float[from.Length];
            for (int j = 0; j < nz; j++)
            {
                for (int i = 0; i < nx; i++)
                {
                    float sum = 0f;
                    for (int u = i - reach; u <= i + reach; u++) sum += from[j * nx + Mathf.Clamp(u, 0, nx - 1)];
                    across[j * nx + i] = sum / (2 * reach + 1);
                }
            }
            var to = new float[from.Length];
            for (int j = 0; j < nz; j++)
            {
                for (int i = 0; i < nx; i++)
                {
                    float sum = 0f;
                    for (int v = j - reach; v <= j + reach; v++) sum += across[Mathf.Clamp(v, 0, nz - 1) * nx + i];
                    to[j * nx + i] = sum / (2 * reach + 1);
                }
            }
            return to;
        }
    }

    // ---- What follows the ground, and how ---------------------------------------------

    private sealed class Plan
    {
        public readonly List<(Transform transform, float rise)> Lifts = new List<(Transform, float)>();
        public readonly List<(Transform transform, Vector3 pivot, float rise, Vector3 normal)> Leans = new List<(Transform, Vector3, float, Vector3)>();
        public readonly List<MeshFilter> Bends = new List<MeshFilter>();
        public readonly List<string> Tilted = new List<string>();
    }

    private static void Visit(Transform node, Transform ground, HeightField field, Plan plan, bool rigidParent)
    {
        if (node == ground || IsSkipped(node)) return;
        if (!TryFootprint(node, true, out Bounds footprint))
        {
            // Nothing to see or bump into: a light or a marker rides the rise where it stands,
            // a plain folder is looked through.
            if (node.GetComponents<Component>().Length > 1)
            {
                plan.Lifts.Add((node, field.At(node.position.x, node.position.z)));
                return;
            }
            foreach (Transform child in node) Visit(child, ground, field, plan, rigidParent);
            return;
        }

        float span = new Vector2(footprint.size.x, footprint.size.z).magnitude;
        string group = node.parent != null ? node.parent.name : string.Empty;
        if (group == HedgeGroup)
        {
            Vector3 c = footprint.center;
            float reachX = Mathf.Max(footprint.extents.x, 0.5f), reachZ = Mathf.Max(footprint.extents.z, 0.5f);
            float slopeX = (field.At(c.x + reachX, c.z) - field.At(c.x - reachX, c.z)) / (2f * reachX);
            float slopeZ = (field.At(c.x, c.z + reachZ) - field.At(c.x, c.z - reachZ)) / (2f * reachZ);
            float middle = 0.5f * (field.At(c.x, c.z + reachZ) + field.At(c.x, c.z - reachZ));
            plan.Leans.Add((node, c, Mathf.Max(middle, field.At(c.x, c.z) - 0.4f), new Vector3(-slopeX, 1f, -slopeZ).normalized));
            return;
        }
        bool named = IsNamedRigid(node.name) && span <= MaxRigidSpan;
        if (node.childCount > 0 && (named || span <= SmallSpan || group == ShrubGroup))
        {
            float rise = field.At(footprint.center.x, footprint.center.z);
            // A trunk is cut off flat: on a slope it goes by the lowest ground under it, so its
            // downhill side stays in the earth.
            bool tree = TrunkOf(node, out Bounds trunk);
            if (tree) rise = field.Lowest(trunk.center.x, trunk.center.z, Mathf.Max(trunk.extents.x, trunk.extents.z) + 0.3f);
            plan.Lifts.Add((node, rise));
            // Worth a look afterwards: a house or a lamp that ended up across a slope.
            float spread = field.Spread(footprint);
            if (named && !tree && spread > 0.1f) plan.Tilted.Add(Path(node) + ": ground differs by " + spread.ToString("0.00") + " m across it");
            return;
        }

        if (TryFootprint(node, false, out Bounds own))
        {
            float ownSpan = new Vector2(own.size.x, own.size.z).magnitude;
            MeshFilter filter = node.GetComponent<MeshFilter>();
            float low = 0f, high = 0f, mean = field.At(own.center.x, own.center.z);
            bool measured = filter != null && filter.sharedMesh != null && Measure(filter, field, out low, out high, out mean);
            if (!measured || high - low <= LevelTolerance)
            {
                plan.Lifts.Add((node, mean));
            }
            else if (ownSpan <= BendSpan || rigidParent || named)
            {
                plan.Lifts.Add((node, field.At(own.center.x, own.center.z)));
                if (ownSpan > SmallSpan) plan.Tilted.Add(Path(node) + ": ground differs by " + (high - low).ToString("0.00") + " m across it");
            }
            else
            {
                plan.Bends.Add(filter);
                // Pinned where it is; its vertices do the moving.
                plan.Lifts.Add((node, 0f));
            }
        }
        foreach (Transform child in node) Visit(child, ground, field, plan, rigidParent || IsNamedRigid(node.name));
    }

    // The trunk of one of the model's trees: the part it calls pCylinder1.
    private static bool TrunkOf(Transform plant, out Bounds trunk)
    {
        trunk = default;
        if (!plant.name.ToLowerInvariant().Contains("tree")) return false;
        foreach (Transform part in plant.GetComponentsInChildren<Transform>(true))
        {
            if (!part.name.EndsWith("pCylinder1", StringComparison.Ordinal)) continue;
            if (part.TryGetComponent(out Collider collider)) trunk = collider.bounds;
            else if (part.TryGetComponent(out Renderer renderer)) trunk = renderer.bounds;
            else continue;
            return true;
        }
        return false;
    }

    private static bool IsSkipped(Transform node)
    {
        string name = node.name.ToLowerInvariant();
        foreach (string skipped in SkippedNames)
        {
            if (name.Contains(skipped)) return true;
        }
        return node.name.StartsWith(SpillPrefix, StringComparison.Ordinal) && node.parent != null && node.parent.name == ScenePavingGroup;
    }

    private static bool IsNamedRigid(string name)
    {
        string lower = name.ToLowerInvariant();
        foreach (string rigid in RigidNames)
        {
            if (lower.Contains(rigid)) return true;
        }
        return false;
    }

    // The bounds of what a node (and, if deep, everything under it) draws or collides with.
    private static bool TryFootprint(Transform node, bool deep, out Bounds bounds)
    {
        bounds = default;
        bool any = false;
        Renderer[] renderers = deep ? node.GetComponentsInChildren<Renderer>(true) : node.GetComponents<Renderer>();
        foreach (Renderer renderer in renderers)
        {
            if (renderer is ParticleSystemRenderer) continue;
            if (any) bounds.Encapsulate(renderer.bounds);
            else bounds = renderer.bounds;
            any = true;
        }
        if (any) return true;
        Collider[] colliders = deep ? node.GetComponentsInChildren<Collider>(true) : node.GetComponents<Collider>();
        foreach (Collider collider in colliders)
        {
            Bounds box = collider.bounds;
            if (box.size == Vector3.zero) box = new Bounds(collider.transform.position, Vector3.one);
            if (any) bounds.Encapsulate(box);
            else bounds = box;
            any = true;
        }
        return any;
    }

    private static bool Measure(MeshFilter filter, HeightField field, out float low, out float high, out float mean)
    {
        low = float.MaxValue;
        high = float.MinValue;
        mean = 0f;
        Vector3[] vertices = filter.sharedMesh.vertices;
        if (vertices.Length == 0) return false;
        Matrix4x4 toWorld = filter.transform.localToWorldMatrix;
        double sum = 0;
        foreach (Vector3 vertex in vertices)
        {
            Vector3 world = toWorld.MultiplyPoint3x4(vertex);
            float h = field.At(world.x, world.z);
            low = Mathf.Min(low, h);
            high = Mathf.Max(high, h);
            sum += h;
        }
        mean = (float)(sum / vertices.Length);
        return true;
    }

    private static string Path(Transform node)
    {
        string path = node.name;
        for (Transform parent = node.parent; parent != null; parent = parent.parent) path = parent.name + "/" + path;
        return path;
    }

    private static string UniqueFileName(MeshFilter filter, HashSet<string> used)
    {
        var name = new StringBuilder();
        string raw = (filter.transform.parent != null ? filter.transform.parent.name + " " : "") + filter.name;
        foreach (char c in raw) name.Append(char.IsLetterOrDigit(c) || c == ' ' || c == '(' || c == ')' ? c : '_');
        string unique = name.ToString();
        for (int n = 2; !used.Add(unique); n++) unique = name + " " + n;
        return unique;
    }

    // ---- Meshes -----------------------------------------------------------------------

    // The slab re-cut finely: its top takes the rise, its underwater rim stays put and the
    // sides stretch between them. Level-ish faces stay grass (submesh 0), steep ones are rock.
    private static Mesh BuildGround(MeshFilter filter, HeightField field)
    {
        var work = new WorkMesh(filter.sharedMesh, filter.transform);
        // The model's slab has a hairline wedge open across its top (east from the square: two
        // corners at the shore a hand's width apart that should be one). Closed here, or the
        // lawn would split along it once it is raised.
        work.Weld(GroundWeld);
        work.RepairJunctions();
        work.Refine(GroundEdge, (a, b) => Mathf.Max(a.y, b.y) > RimY + 0.5f);
        for (int i = 0; i < work.Positions.Count; i++)
        {
            Vector3 p = work.Positions[i];
            float weight = Mathf.Clamp01((p.y - RimY) / (TopY - RimY));
            p.y += weight * field.At(p.x, p.z);
            work.Positions[i] = p;
        }

        var grass = new List<int>();
        var rock = new List<int>();
        List<int> all = work.Triangles[0];
        for (int t = 0; t < all.Count; t += 3)
        {
            Vector3 a = work.Positions[all[t]], b = work.Positions[all[t + 1]], c = work.Positions[all[t + 2]];
            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            // Underwater faces keep the ground's material; nobody sees them.
            bool steep = Mathf.Abs(normal.y) < RockNormalY && Mathf.Max(a.y, Mathf.Max(b.y, c.y)) > RimY + 0.5f;
            List<int> list = steep ? rock : grass;
            list.Add(all[t]); list.Add(all[t + 1]); list.Add(all[t + 2]);
        }
        work.Triangles.Clear();
        work.Triangles.Add(grass);
        work.Triangles.Add(rock);
        Mesh mesh = work.ToMesh(filter.sharedMesh.name + " Raised", filter.transform, true);
        return mesh;
    }

    // A copy of a mesh with every vertex raised by the rise under it. Vertical lines stay
    // vertical, so a fence rail or a hedge crossing a slope leans with it but nothing tips over.
    private static Mesh Bend(MeshFilter filter, HeightField field)
    {
        var work = new WorkMesh(filter.sharedMesh, filter.transform);
        bool cut = filter.sharedMesh.vertexCount <= CoarseMesh && work.HasEdgeLongerThan(PathEdge);
        if (cut)
        {
            work.RepairJunctions();
            work.Refine(PathEdge, (a, b) => true);
        }
        for (int i = 0; i < work.Positions.Count; i++)
        {
            Vector3 p = work.Positions[i];
            Vector2 slope = field.SlopeAt(p.x, p.z);
            p.y += field.At(p.x, p.z);
            work.Positions[i] = p;
            if (work.Normals != null)
            {
                Vector3 n = work.Normals[i];
                work.Normals[i] = new Vector3(n.x - slope.x * n.y, n.y, n.z - slope.y * n.y).normalized;
            }
            if (work.Tangents != null)
            {
                Vector4 t = work.Tangents[i];
                Vector3 leaned = new Vector3(t.x, t.y + slope.x * t.x + slope.y * t.z, t.z).normalized;
                work.Tangents[i] = new Vector4(leaned.x, leaned.y, leaned.z, t.w);
            }
        }
        if (cut) return work.ToMesh(filter.sharedMesh.name + " Bent", filter.transform, false);

        // Nothing was re-cut: keep the mesh's own layout (and its detail levels), move the points.
        Mesh mesh = UnityEngine.Object.Instantiate(filter.sharedMesh);
        mesh.name = filter.sharedMesh.name + " Bent";
        work.WriteInto(mesh, filter.transform);
        return mesh;
    }

    // A mesh held in world space so it can be re-cut and reshaped by where its points are.
    private sealed class WorkMesh
    {
        public readonly List<Vector3> Positions = new List<Vector3>();
        public readonly List<Vector3> Normals;
        public readonly List<Vector2> Uvs;
        public readonly List<Vector4> Tangents;
        public readonly List<List<int>> Triangles = new List<List<int>>();

        public WorkMesh(Mesh mesh, Transform transform)
        {
            Matrix4x4 toWorld = transform.localToWorldMatrix;
            Matrix4x4 normalToWorld = toWorld.inverse.transpose;
            foreach (Vector3 vertex in mesh.vertices) Positions.Add(toWorld.MultiplyPoint3x4(vertex));
            Vector3[] normals = mesh.normals;
            if (normals.Length == Positions.Count)
            {
                Normals = new List<Vector3>(normals.Length);
                foreach (Vector3 normal in normals) Normals.Add(normalToWorld.MultiplyVector(normal).normalized);
            }
            Vector2[] uvs = mesh.uv;
            if (uvs.Length == Positions.Count) Uvs = new List<Vector2>(uvs);
            Vector4[] tangents = mesh.tangents;
            if (tangents.Length == Positions.Count)
            {
                Tangents = new List<Vector4>(tangents.Length);
                foreach (Vector4 tangent in tangents)
                {
                    Vector3 world = toWorld.MultiplyVector(tangent).normalized;
                    Tangents.Add(new Vector4(world.x, world.y, world.z, tangent.w));
                }
            }
            for (int s = 0; s < mesh.subMeshCount; s++) Triangles.Add(new List<int>(mesh.GetTriangles(s)));
        }

        // Moves corners that are nearly in the same place to exactly the same place.
        public void Weld(float distance)
        {
            float limit = distance * distance;
            for (int i = 0; i < Positions.Count; i++)
            {
                for (int j = i + 1; j < Positions.Count; j++)
                {
                    float apart = (Positions[i] - Positions[j]).sqrMagnitude;
                    if (apart > 0f && apart < limit) Positions[j] = Positions[i];
                }
            }
        }

        public bool HasEdgeLongerThan(float maxEdge)
        {
            float limit = maxEdge * maxEdge;
            foreach (List<int> triangles in Triangles)
            {
                for (int t = 0; t < triangles.Count; t += 3)
                {
                    Vector3 a = Positions[triangles[t]], b = Positions[triangles[t + 1]], c = Positions[triangles[t + 2]];
                    if ((a - b).sqrMagnitude > limit || (b - c).sqrMagnitude > limit || (c - a).sqrMagnitude > limit) return true;
                }
            }
            return false;
        }

        // Modellers leave corners sitting partway along a neighbouring triangle's edge. Flat,
        // that shows nothing; reshaped, the two sides part and the surface cracks. Every edge
        // with a corner on it is cut there first, so each edge is shared end to end.
        public void RepairJunctions()
        {
            const float tolerance = 0.01f;
            var corners = new List<Vector3>();
            var seen = new HashSet<Vector3Int>();
            foreach (Vector3 position in Positions)
            {
                if (seen.Add(new Vector3Int(Mathf.RoundToInt(position.x * 100f), Mathf.RoundToInt(position.y * 100f), Mathf.RoundToInt(position.z * 100f)))) corners.Add(position);
            }
            for (int s = 0; s < Triangles.Count; s++)
            {
                var queue = new List<int>(Triangles[s]);
                var whole = new List<int>(queue.Count);
                // A sound mesh needs a handful of cuts; the budget only stops a broken one running away.
                int budget = queue.Count * 8 + 3000;
                for (int t = 0; t < queue.Count; t += 3)
                {
                    int a = queue[t], b = queue[t + 1], c = queue[t + 2];
                    if (queue.Count < budget && (Cut(a, b, c) || Cut(b, c, a) || Cut(c, a, b))) continue;
                    Add(whole, a, b, c);
                }
                Triangles[s] = whole;

                // Cuts the triangle at the corner nearest from that sits on its edge from-to.
                bool Cut(int from, int to, int apex)
                {
                    Vector3 start = Positions[from], along = Positions[to] - start;
                    float lengthSquared = along.sqrMagnitude;
                    if (lengthSquared < 0.0001f) return false;
                    float nearest = float.MaxValue;
                    Vector3 corner = default;
                    foreach (Vector3 candidate in corners)
                    {
                        float share = Vector3.Dot(candidate - start, along) / lengthSquared;
                        if (share <= 0.001f || share >= 0.999f || share >= nearest) continue;
                        if ((start + along * share - candidate).sqrMagnitude > tolerance * tolerance) continue;
                        nearest = share;
                        corner = candidate;
                    }
                    if (nearest == float.MaxValue) return false;
                    int middle = Positions.Count;
                    Positions.Add(corner);
                    if (Normals != null) Normals.Add(Vector3.Lerp(Normals[from], Normals[to], nearest).normalized);
                    if (Uvs != null) Uvs.Add(Vector2.Lerp(Uvs[from], Uvs[to], nearest));
                    if (Tangents != null)
                    {
                        Vector3 tangent = Vector3.Lerp(Tangents[from], Tangents[to], nearest).normalized;
                        Tangents.Add(new Vector4(tangent.x, tangent.y, tangent.z, Tangents[from].w));
                    }
                    Add(queue, from, middle, apex);
                    Add(queue, middle, to, apex);
                    return true;
                }
            }
        }

        // Halves every edge longer than maxEdge that wanted says to cut, until none is left.
        // An edge is judged by its two ends alone, so both triangles sharing it agree and the
        // surface stays closed. Returns whether anything was cut.
        public bool Refine(float maxEdge, Func<Vector3, Vector3, bool> wanted)
        {
            bool any = false;
            float limit = maxEdge * maxEdge;
            for (int pass = 0; pass < 24; pass++)
            {
                var middles = new Dictionary<long, int>();
                bool cut = false;
                for (int s = 0; s < Triangles.Count; s++)
                {
                    List<int> from = Triangles[s];
                    var to = new List<int>(from.Count * 2);
                    for (int t = 0; t < from.Count; t += 3)
                    {
                        int a = from[t], b = from[t + 1], c = from[t + 2];
                        bool ab = Long(a, b), bc = Long(b, c), ca = Long(c, a);
                        int count = (ab ? 1 : 0) + (bc ? 1 : 0) + (ca ? 1 : 0);
                        if (count == 0)
                        {
                            to.Add(a); to.Add(b); to.Add(c);
                            continue;
                        }
                        cut = true;
                        // Turn the triangle so the cases below need only one layout each.
                        if (count == 1)
                        {
                            if (bc) { int k = a; a = b; b = c; c = k; }
                            else if (ca) { int k = c; c = b; b = a; a = k; }
                            int m = Middle(a, b);
                            Add(to, a, m, c);
                            Add(to, m, b, c);
                        }
                        else if (count == 2)
                        {
                            // Turn so the uncut edge is ca.
                            if (!ab) { int k = a; a = b; b = c; c = k; }
                            else if (!bc) { int k = c; c = b; b = a; a = k; }
                            int m1 = Middle(a, b), m2 = Middle(b, c);
                            Add(to, m1, b, m2);
                            if ((Positions[a] - Positions[m2]).sqrMagnitude <= (Positions[c] - Positions[m1]).sqrMagnitude)
                            {
                                Add(to, a, m1, m2);
                                Add(to, a, m2, c);
                            }
                            else
                            {
                                Add(to, a, m1, c);
                                Add(to, m1, m2, c);
                            }
                        }
                        else
                        {
                            int m1 = Middle(a, b), m2 = Middle(b, c), m3 = Middle(c, a);
                            Add(to, a, m1, m3);
                            Add(to, m1, b, m2);
                            Add(to, m3, m2, c);
                            Add(to, m1, m2, m3);
                        }
                    }
                    Triangles[s] = to;
                }
                if (!cut) break;
                any = true;
                int total = 0;
                foreach (List<int> triangles in Triangles) total += triangles.Count / 3;
                if (total > MostTriangles) break;

                bool Long(int a, int b)
                {
                    Vector3 p = Positions[a], q = Positions[b];
                    return (p - q).sqrMagnitude > limit && wanted(p, q);
                }

                int Middle(int a, int b)
                {
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (middles.TryGetValue(key, out int index)) return index;
                    index = Positions.Count;
                    Positions.Add((Positions[a] + Positions[b]) * 0.5f);
                    if (Normals != null) Normals.Add((Normals[a] + Normals[b]).normalized);
                    if (Uvs != null) Uvs.Add((Uvs[a] + Uvs[b]) * 0.5f);
                    if (Tangents != null)
                    {
                        Vector3 t = ((Vector3)Tangents[a] + (Vector3)Tangents[b]).normalized;
                        Tangents.Add(new Vector4(t.x, t.y, t.z, Tangents[a].w));
                    }
                    middles[key] = index;
                    return index;
                }
            }
            return any;
        }

        private static void Add(List<int> list, int a, int b, int c)
        {
            list.Add(a); list.Add(b); list.Add(c);
        }

        public Mesh ToMesh(string name, Transform transform, bool recalculateNormals)
        {
            var mesh = new Mesh { name = name };
            if (Positions.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            WriteInto(mesh, transform);
            if (Uvs != null) mesh.SetUVs(0, Uvs);
            mesh.subMeshCount = Triangles.Count;
            for (int s = 0; s < Triangles.Count; s++) mesh.SetTriangles(Triangles[s], s);
            if (recalculateNormals)
            {
                mesh.RecalculateNormals();
                mesh.RecalculateTangents();
            }
            mesh.RecalculateBounds();
            return mesh;
        }

        // Positions, normals and tangents back in the transform's own space.
        public void WriteInto(Mesh mesh, Transform transform)
        {
            Matrix4x4 toLocal = transform.worldToLocalMatrix;
            Matrix4x4 normalToLocal = transform.localToWorldMatrix.transpose;
            var positions = new List<Vector3>(Positions.Count);
            foreach (Vector3 position in Positions) positions.Add(toLocal.MultiplyPoint3x4(position));
            mesh.SetVertices(positions);
            if (Normals != null)
            {
                var normals = new List<Vector3>(Normals.Count);
                foreach (Vector3 normal in Normals) normals.Add(normalToLocal.MultiplyVector(normal).normalized);
                mesh.SetNormals(normals);
            }
            if (Tangents != null)
            {
                var tangents = new List<Vector4>(Tangents.Count);
                foreach (Vector4 tangent in Tangents)
                {
                    Vector3 local = toLocal.MultiplyVector(tangent).normalized;
                    tangents.Add(new Vector4(local.x, local.y, local.z, tangent.w));
                }
                mesh.SetTangents(tangents);
            }
            mesh.RecalculateBounds();
        }
    }

    // ---- Rock -------------------------------------------------------------------------

    // Matte banded rock in the grass texture's chunky pixels, for the island's steep sides.
    private static Material CliffMaterial(Material grass)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(CliffMaterialPath);
        if (material != null) return material;

        const int size = 128;
        var texture = new Texture2D(size, size, TextureFormat.RGB24, false);
        var dark = new Color32(86, 78, 70, 255);
        var mid = new Color32(112, 102, 90, 255);
        var light = new Color32(136, 126, 110, 255);
        var crack = new Color32(60, 54, 50, 255);
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // Blocky strata that wander up and down, in 2-pixel steps like the grass.
                int bx = x / 2, by = y / 2;
                float wander = Mathf.PerlinNoise(bx * 0.11f, 3.7f) * 10f;
                float band = Mathf.Repeat((by + wander) / 9f, 1f);
                float grain = Mathf.PerlinNoise(bx * 0.37f + 11f, by * 0.37f + 5f);
                Color32 color = band < 0.12f ? crack : band < 0.5f ? (grain > 0.62f ? light : mid) : (grain > 0.7f ? mid : dark);
                if (Hash(bx, by) > 0.955f) color = light;
                pixels[y * size + x] = color;
            }
        }
        texture.SetPixels32(pixels);
        File.WriteAllBytes(CliffTexturePath, texture.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(CliffTexturePath);
        if (AssetImporter.GetAtPath(CliffTexturePath) is TextureImporter importer)
        {
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
        }

        material = new Material(grass) { name = "CliffMat" };
        Texture2D rock = AssetDatabase.LoadAssetAtPath<Texture2D>(CliffTexturePath);
        if (material.HasProperty("_BaseMap"))
        {
            material.SetTexture("_BaseMap", rock);
            // The slab's sides carry metre UVs: one repeat every 8 m.
            material.SetTextureScale("_BaseMap", new Vector2(0.125f, 0.125f));
        }
        if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", rock);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
        AssetDatabase.CreateAsset(material, CliffMaterialPath);
        return material;
    }

    private static float Hash(int x, int y)
    {
        uint h = (uint)(x * 374761393 + y * 668265263);
        h = (h ^ (h >> 13)) * 1274126177u;
        return ((h ^ (h >> 16)) & 0xffff) / 65535f;
    }
}
