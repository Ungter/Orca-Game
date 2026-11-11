using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Dresses the island: woods grown thick with new trees, undergrowth on the lawns, boulders on
// the slopes, and a rocky shore that runs from the island's edge down into the sea.
//
// The model's own trees are all one big and one medium tree, every copy facing the same way, so
// each is first turned, resized a little and leant a touch. The new trees are grown one by one
// (no two alike): broadleaves with a flared trunk, boughs and a crown of lumpy leaf masses, tall
// narrow poplars, and pines in drooping tiers. Everything is shaded in bands from a small
// palette, lighter where it faces the sky, to stay with the game's flat cartoon look.
//
// Nothing here is a separate object to draw: plants are baked into one mesh per patch of the
// island (like the model's merged foliage), all sharing one material, so the woods cost a
// handful of draw calls. Trees, bushes and boulders still get simple colliders of their own,
// each with a MergedRenderer naming the mesh that draws it, as the model's plants have.
//
// Tools > Orca > Regrow Island Scenery rebuilds all of it from the same seed; it keeps clear of
// paths, buildings, lamps and everything the player has to find. Run it after IslandTerrain.
// LighthouseScenery.cs grows the same plants on the lighthouse island.
public static partial class IslandScenery
{
    public const string RootName = "Island Scenery";
    private const string Folder = "Assets/Generated/IslandScenery";
    private const string MeshesPath = Folder + "/Scenery.asset";
    private const string PalettePath = "Assets/Materials/Scenery Palette.png";
    private const string MaterialPath = "Assets/Materials/SceneryMat.mat";
    private const string GroundName = "IslandDemo:pCube1";
    private const int Seed = 4217;
    private const float PatchSize = 48f;

    // ---- Palette ----------------------------------------------------------------------

    // One row of four shades per material, dark to light. A vertex picks its row and how far
    // along it sits; the texture is point-sampled, so shading steps in four flat bands.
    private const int LeafDeep = 0, LeafOlive = 1, LeafPine = 2, LeafFresh = 3, BarkBrown = 4, BarkGrey = 5,
        Grass = 6, Fern = 7, PetalWhite = 8, PetalYellow = 9, PetalViolet = 10, PetalPink = 11,
        Rock = 12, RockWet = 13, Wood = 14, RampCount = 16;

    private static readonly string[] Ramps =
    {
        "0F220D 14300F 1B3F14 25521A", // deep leaves, the model's own greens and a little lighter
        "1A2A0E 223711 2D4716 3A5A1C", // olive leaves
        "0C2219 10301F 153F27 1C5030", // pine
        "163214 1E441A 285820 346E28", // fresh leaves
        "1E150B 2A1D10 3A2916 4A361E", // brown bark
        "2B2620 3A342B 4C4538 5F5746", // grey bark
        "2E6E32 3E8A3A 58A03A 7DB542", // grass, from the ground texture
        "173D1A 1F5222 2A682A 388034", // fern
        "A9A696 C9C6B4 E0DCC8 F0ECDA", // white petals
        "9A7C22 B8962E DDB93A F0D25A", // yellow petals
        "4A3C82 5B4A9A 7C68C2 9A88DA", // violet petals
        "8A3A4A A8485A CF6A7A E590A0", // pink petals
        "47474B 5C5C60 727276 8A8A8E", // rock
        "2C2E32 383B40 46494F 54575D", // wet rock at the waterline
        "4A3A24 6B5434 8A6E46 A58A5C", // cut wood
        "3F3F43 525256 68686C 808084", // rock in shade, a spare
    };

    private static Vector2 Shade(int ramp, float along)
    {
        return new Vector2(Mathf.Clamp(along, 0.03f, 0.97f), (ramp + 0.5f) / RampCount);
    }

    // ---- Where things grow ------------------------------------------------------------

    // Per 100 square metres. Trees are split between broadleaf, poplar and pine by the three shares.
    private readonly struct Zone
    {
        public readonly Rect Area;
        public readonly float Trees, Broadleaf, Poplar, Bushes, Ferns, Flowers;

        public Zone(float xMin, float xMax, float zMin, float zMax, float trees, float broadleaf, float poplar,
            float bushes, float ferns, float flowers)
        {
            Area = Rect.MinMaxRect(xMin, zMin, xMax, zMax);
            Trees = trees;
            Broadleaf = broadleaf;
            Poplar = poplar;
            Bushes = bushes;
            Ferns = ferns;
            Flowers = flowers;
        }
    }

    private static readonly Zone Elsewhere = new Zone(0, 0, 0, 0, 0.05f, 0.5f, 0.3f, 0.15f, 0.3f, 0.15f);
    private static readonly Zone[] Zones =
    {
        new Zone(-68, -8, 112, 182, 1.6f, 0.45f, 0.15f, 0.5f, 2.5f, 0.05f),    // north-west wood
        new Zone(-45, 64, 166, 226, 1.6f, 0.25f, 0.10f, 0.4f, 2.5f, 0.05f),    // north head
        new Zone(50, 100, 110, 192, 0.7f, 0.50f, 0.30f, 0.4f, 1.0f, 0.5f),     // north-east grove
        new Zone(-8, 52, 110, 166, 0.25f, 0.60f, 0.30f, 0.3f, 0.8f, 0.3f),     // the magician's dell
        new Zone(-54, 6, -24, 40, 1.0f, 0.70f, 0.20f, 0.5f, 2.0f, 0.1f),       // west grove
        new Zone(-36, 66, -72, -18, 0.45f, 0.60f, 0.25f, 0.4f, 1.0f, 0.25f),   // south lawn
        new Zone(-100, -36, -158, -16, 1.5f, 0.30f, 0.10f, 0.5f, 2.5f, 0.05f), // south-west wood
        new Zone(-40, 82, -158, -84, 0.3f, 0.50f, 0.20f, 0.4f, 0.8f, 0.2f),    // harbour side
        new Zone(86, 120, -40, 106, 0.12f, 0.20f, 0.60f, 0.5f, 0.6f, 0.3f),    // east bank
        new Zone(-54, 66, 36, 102, 0.1f, 0.60f, 0.40f, 0.2f, 0.3f, 0.4f),      // town greens
        new Zone(16, 66, -20, 38, 0.12f, 0.60f, 0.40f, 0.3f, 0.3f, 0.4f),      // lawns east of the square
    };
    // A zone this thick with trees is a wood: it grows in clumps with clearings between.
    private const float WoodDensity = 0.4f;
    private const float GrassTufts = 2f;

    // Things the player has to find or use: nothing grows this close to them.
    private static readonly (string name, float radius)[] KeptClear =
    {
        ("Player", 7f), ("MagicianEntrance", 10f), ("Clue Letter", 4f), ("Lost Dice", 4f), ("MagicWand", 4f),
        ("digSiteMinigame2", 4f), ("graveStone1", 5f), ("al_cachud", 10f), ("DolphinStatue", 7f),
    };
    private const string DockName = "IslandDemo:Dock";
    private const float DockClear = 12f;
    // Headroom under a tree's crown.
    private const float WalkUnder = 2.4f;
    // How deep a model tree's trunk is set into the ground.
    private const float TrunkDepth = 0.3f;
    private const float LampClear = 7f;

    // ---- Shore ------------------------------------------------------------------------

    private const float ShoreStep = 2f;
    private const int ShoreRings = 7;
    // How far out the rocks reach: this much, plus ShoreRunPerMetre for each metre of cliff.
    private const float ShoreRun = 2.2f;
    private const float ShoreRunPerMetre = 0.5f;
    private const float ShoreDepth = 1.7f;
    // Breaks in the ground's outline narrower than this are stepped across.
    private const float CoastGap = 1.5f;

    private enum Layer { Trees, Cover, Shore }

    // ---- Run --------------------------------------------------------------------------

    [MenuItem("Tools/Orca/Regrow Island Scenery")]
    public static void Regrow()
    {
        Debug.Log(Grow());
    }

    public static string Grow()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "[IslandScenery] Not in play mode: nothing would be kept.";
        GameObject groundObject = GameObject.Find(GroundName);
        if (groundObject == null || !groundObject.TryGetComponent(out MeshCollider ground) || !groundObject.TryGetComponent(out MeshFilter groundFilter))
        {
            return "[IslandScenery] No " + GroundName + " ground in the open scene.";
        }

        Undo.SetCurrentGroupName("Regrow Island Scenery");
        int undo = Undo.GetCurrentGroup();
        GameObject old = GameObject.Find(RootName);
        if (old != null) Undo.DestroyObjectImmediate(old);
        int varied = VaryModelTrees(groundObject.transform.root);
        Physics.SyncTransforms();
        int seated = SeatModelTrees(groundObject.transform.root, ground);
        Physics.SyncTransforms();

        var site = new Site(ground, groundObject.transform.root);
        var patches = new Dictionary<(int, int, Layer), Builder>();
        var plants = new List<Plant>();
        var random = new System.Random(Seed);

        int shore = BuildShore(site, groundFilter, patches, plants);
        int[] counts = GrowPlants(site, patches, plants, random);

        // Meshes, one file.
        if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
        Directory.CreateDirectory(Folder);
        AssetDatabase.Refresh();
        Material material = SceneryMaterial(site.Island);

        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Regrow Island Scenery");
        int vertices = MakeSceneryObjects(root.transform, patches, plants, MeshesPath, false, material, site.FoliageRenderer);
        Physics.SyncTransforms();
        Undo.CollapseUndoOperations(undo);
        EditorSceneManager.MarkSceneDirty(root.scene);

        return "[IslandScenery] " + varied + " of the model's trees turned and resized, " + seated + " set down into the ground. Grew " + counts[0] + " broadleaves, " +
            counts[1] + " poplars, " + counts[2] + " pines, " + counts[3] + " bushes, " + counts[4] + " ferns, " + counts[5] +
            " grass tufts, " + counts[6] + " flowers, " + counts[7] + " boulders and " + counts[8] + " stumps; " + shore +
            " m of shore rocked. " + patches.Count + " meshes, " + vertices + " vertices.";
    }

    // One mesh per patch, saved into meshesPath (which already exists when assetExists), each
    // drawn by a child of root, and the plants' colliders under a "Plants" child. The patches
    // were built in world space and are moved into root's space. Returns the vertices made.
    private static int MakeSceneryObjects(Transform root, Dictionary<(int, int, Layer), Builder> patches, List<Plant> plants,
        string meshesPath, bool assetExists, Material material, Renderer like)
    {
        var drawn = new Dictionary<(int, int, Layer), MeshRenderer>();
        Matrix4x4 toRoot = root.worldToLocalMatrix;
        bool first = !assetExists;
        int vertices = 0;
        foreach (KeyValuePair<(int, int, Layer), Builder> patch in patches)
        {
            if (patch.Value.Triangles.Count == 0) continue;
            (int px, int pz, Layer layer) = patch.Key;
            string label = (layer == Layer.Trees ? "Trees " : layer == Layer.Cover ? "Ground Cover " : "Shore Rocks ") + px + "_" + pz;
            if (!toRoot.isIdentity) patch.Value.Move(toRoot);
            Mesh mesh = patch.Value.ToMesh(label);
            vertices += mesh.vertexCount;
            if (first) AssetDatabase.CreateAsset(mesh, meshesPath);
            else AssetDatabase.AddObjectToAsset(mesh, meshesPath);
            first = false;

            var holder = new GameObject(label);
            holder.transform.SetParent(root, false);
            holder.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = holder.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = layer == Layer.Cover ? ShadowCastingMode.Off : ShadowCastingMode.On;
            if (like != null)
            {
                holder.layer = like.gameObject.layer;
                renderer.renderingLayerMask = like.renderingLayerMask;
                renderer.lightProbeUsage = like.lightProbeUsage;
                renderer.reflectionProbeUsage = like.reflectionProbeUsage;
            }
            // The shore is walked on as it is drawn.
            if (layer == Layer.Shore) holder.AddComponent<MeshCollider>().sharedMesh = mesh;
            drawn[patch.Key] = renderer;
        }
        AssetDatabase.SaveAssets();

        // What blocks the way: a trunk, a crown overhead, a bush, a boulder.
        var blockers = new GameObject("Plants");
        blockers.transform.SetParent(root, false);
        var numbering = new Dictionary<string, int>();
        foreach (Plant plant in plants)
        {
            // A tree's crown only blocks overhead: it is kept clear of anyone walking under it.
            float crownRadius = plant.CrownRadius;
            if (plant.TrunkHeight > 0f) crownRadius = Mathf.Min(crownRadius, plant.Crown.y - plant.Base.y - WalkUnder);
            if (crownRadius < 0.5f) crownRadius = 0f;
            if (plant.TrunkHeight <= 0f && crownRadius <= 0f) continue;

            numbering.TryGetValue(plant.Name, out int number);
            numbering[plant.Name] = ++number;
            var blocker = new GameObject(plant.Name + " " + number);
            blocker.transform.SetParent(blockers.transform, false);
            blocker.transform.position = plant.Base;
            if (plant.TrunkHeight > 0f)
            {
                var trunk = blocker.AddComponent<CapsuleCollider>();
                trunk.radius = plant.TrunkRadius;
                trunk.height = Mathf.Max(plant.TrunkHeight, plant.TrunkRadius * 2f);
                trunk.center = Vector3.up * (plant.TrunkHeight * 0.5f);
            }
            if (crownRadius > 0f)
            {
                var crown = blocker.AddComponent<SphereCollider>();
                crown.radius = crownRadius;
                crown.center = plant.Crown - plant.Base;
            }
            if (drawn.TryGetValue(PatchOf(plant.Base, Layer.Trees), out MeshRenderer target))
            {
                blocker.AddComponent<MergedRenderer>().Target = target;
            }
        }
        return vertices;
    }

    private static (int, int, Layer) PatchOf(Vector3 at, Layer layer)
    {
        return (Mathf.FloorToInt(at.x / PatchSize), Mathf.FloorToInt(at.z / PatchSize), layer);
    }

    private static Builder For(Dictionary<(int, int, Layer), Builder> patches, Vector3 at, Layer layer)
    {
        (int, int, Layer) key = PatchOf(at, layer);
        if (!patches.TryGetValue(key, out Builder builder)) patches[key] = builder = new Builder();
        return builder;
    }

    // ---- The model's own trees --------------------------------------------------------

    // Every big tree is the same tree facing the same way, and so is every medium one. Each is
    // turned about its trunk, resized and leant a touch. A tree already turned is left alone.
    private static int VaryModelTrees(Transform island)
    {
        List<Transform> trees = ModelTrees(island);
        int varied = 0;
        foreach (Transform tree in trees)
        {
            Transform source = PrefabUtility.GetCorrespondingObjectFromSource(tree);
            Quaternion asModelled = source != null ? source.localRotation : Quaternion.identity;
            if (Quaternion.Angle(tree.localRotation, asModelled) > 0.5f) continue;
            if (!TrunkFoot(tree, out Vector3 foot, out _)) continue;

            var random = new System.Random(StableHash(tree.name + "/" + tree.GetSiblingIndex()));
            bool big = tree.name.Contains("big");
            float size = big ? Range(random, 0.8f, 1.08f) : Range(random, 0.82f, 1.22f);
            float turn = Range(random, 25f, 335f);
            float lean = Range(random, 0f, 3.5f);
            float leanTowards = Range(random, 0f, 360f);

            Undo.RecordObject(tree, "Regrow Island Scenery");
            tree.position = foot + (tree.position - foot) * size;
            tree.localScale *= size;
            tree.RotateAround(foot, Vector3.up, turn);
            tree.RotateAround(foot, Quaternion.Euler(0f, leanTowards, 0f) * Vector3.right, lean);
            varied++;
        }
        return varied;
    }

    private static List<Transform> ModelTrees(Transform island)
    {
        var trees = new List<Transform>();
        foreach (Transform child in island.GetComponentsInChildren<Transform>(true))
        {
            if ((child.name == "bigtrees" || child.name == "mediumtrees") && child.parent == island)
            {
                foreach (Transform tree in child) trees.Add(tree);
            }
        }
        foreach (GameObject root in island.gameObject.scene.GetRootGameObjects())
        {
            if (root.name.StartsWith("treebig", StringComparison.Ordinal) || root.name.StartsWith("treemedium", StringComparison.Ordinal)) trees.Add(root.transform);
        }
        return trees;
    }

    // The model stands every big tree at one height, which leaves some a little clear of the
    // ground where it dips. Each is set down until its trunk's foot is TrunkDepth in the earth
    // all the way round. Trees are only ever lowered, so running this again changes nothing.
    private static int SeatModelTrees(Transform island, MeshCollider ground)
    {
        int seated = 0;
        foreach (Transform tree in ModelTrees(island))
        {
            if (!TrunkFoot(tree, out Vector3 foot, out float radius)) continue;
            float lowest = float.MaxValue;
            for (int i = 0; i < 9; i++)
            {
                Vector3 at = i == 0 ? foot : foot + new Vector3(Mathf.Cos(i * Mathf.PI / 4f), 0f, Mathf.Sin(i * Mathf.PI / 4f)) * radius;
                if (ground.Raycast(new Ray(new Vector3(at.x, foot.y + 100f, at.z), Vector3.down), out RaycastHit hit, 300f)) lowest = Mathf.Min(lowest, hit.point.y);
            }
            if (lowest == float.MaxValue) continue;
            float drop = foot.y - (lowest - TrunkDepth);
            if (drop <= 0.02f) continue;
            Undo.RecordObject(tree, "Regrow Island Scenery");
            tree.position += Vector3.down * drop;
            seated++;
        }
        return seated;
    }

    // Where the trunk meets the ground: the foot of the part the model calls pCylinder1, and
    // how far the trunk reaches from its middle there.
    private static bool TrunkFoot(Transform tree, out Vector3 foot, out float radius)
    {
        foot = default;
        radius = 0f;
        foreach (Transform part in tree.GetComponentsInChildren<Transform>(true))
        {
            if (!part.name.EndsWith("pCylinder1", StringComparison.Ordinal)) continue;
            Bounds bounds;
            if (part.TryGetComponent(out Collider collider)) bounds = collider.bounds;
            else if (part.TryGetComponent(out Renderer renderer)) bounds = renderer.bounds;
            else continue;
            foot = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            radius = Mathf.Max(bounds.extents.x, bounds.extents.z);
            return true;
        }
        return false;
    }

    // ---- Site: the ground and what already stands on it -------------------------------

    private sealed class Site
    {
        public readonly Transform Island;
        public readonly Bounds Land;
        public readonly float SeaLevel;
        public readonly Renderer FoliageRenderer;
        public Bounds? Dock;
        // What grows where. Another island brings its own.
        public Zone[] ZoneList = Zones;
        public Zone Fallback = Elsewhere;
        public float WoodDensity = IslandScenery.WoodDensity;
        public float TuftDensity = GrassTufts;
        public float LampClearance = LampClear;
        // Also in the way, for things with no collider (the lighthouse island's hedges): whether
        // any of it is within reach of a spot.
        public Func<Vector3, float, bool> Occupied;
        // Plants stand on these; nothing else under them counts as ground.
        private readonly List<Collider> grounds = new List<Collider>();
        private readonly List<Bounds> buildings = new List<Bounds>();
        private readonly List<Vector2> lamps = new List<Vector2>();
        private readonly List<(Vector2 at, float radius)> keptClear = new List<(Vector2, float)>();
        private readonly Collider[] overlaps = new Collider[32];

        // Another island: the ground plants may stand on, how far they may spread and the sea.
        // Its buildings, lamps and kept-clear spots are added after.
        public Site(IEnumerable<Collider> ground, Transform island, Bounds land, float seaLevel, Renderer foliageLike)
        {
            grounds.AddRange(ground);
            Island = island;
            Land = land;
            SeaLevel = seaLevel;
            FoliageRenderer = foliageLike;
        }

        public void AddBuilding(Bounds bounds) => buildings.Add(bounds);
        public void AddLamp(Vector3 at) => lamps.Add(new Vector2(at.x, at.z));
        public void KeepClear(Vector3 at, float radius) => keptClear.Add((new Vector2(at.x, at.z), radius));

        public Site(MeshCollider ground, Transform island)
        {
            grounds.Add(ground);
            Island = island;
            Land = ground.bounds;
            SeaLevel = -1.83f;
            foreach (Transform child in island.GetComponentsInChildren<Transform>(true))
            {
                string name = child.name.ToLowerInvariant();
                if (name == "ocean" && child.TryGetComponent(out Renderer sea)) SeaLevel = sea.bounds.max.y;
                if (child.name == DockName && child.TryGetComponent(out Renderer dock)) Dock = dock.bounds;
                if (name.StartsWith("lamp", StringComparison.Ordinal) && child.parent == island && Footprint(child, out Bounds lamp)) lamps.Add(new Vector2(lamp.center.x, lamp.center.z));
                if (FoliageRenderer == null && child.parent != null && child.parent.parent != null && child.parent.parent.name == "bigtrees" &&
                    name.StartsWith("leaves", StringComparison.Ordinal) && child.TryGetComponent(out Renderer leaves)) FoliageRenderer = leaves;
                // The houses: named ones, and the loose walls and roofs of the start house.
                bool looseWall = child.parent != null && child.parent.name == "world" && !name.StartsWith("fence", StringComparison.Ordinal) &&
                    !name.Contains("ocean") && name != "island_demo_map";
                if ((looseWall || name.StartsWith("building", StringComparison.Ordinal)) && Footprint(child, out Bounds house)) buildings.Add(house);
            }
            foreach (GameObject root in island.gameObject.scene.GetRootGameObjects())
            {
                string name = root.name.ToLowerInvariant();
                if ((name.StartsWith("building", StringComparison.Ordinal) || name == "testmodel") && Footprint(root.transform, out Bounds house)) buildings.Add(house);
            }
            foreach ((string name, float radius) in KeptClear)
            {
                GameObject found = GameObject.Find(name);
                if (found == null) continue;
                Vector3 at = Footprint(found.transform, out Bounds bounds) ? bounds.center : found.transform.position;
                keptClear.Add((new Vector2(at.x, at.z), radius));
            }
        }

        private static bool Footprint(Transform node, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            foreach (Renderer renderer in node.GetComponentsInChildren<Renderer>(true))
            {
                if (any) bounds.Encapsulate(renderer.bounds);
                else bounds = renderer.bounds;
                any = true;
            }
            return any;
        }

        // The highest ground straight down from above the island.
        public bool GroundAt(float x, float z, out Vector3 point, out Vector3 normal)
        {
            var ray = new Ray(new Vector3(x, Land.max.y + 50f, z), Vector3.down);
            bool found = false;
            point = normal = default;
            foreach (Collider ground in grounds)
            {
                if (ground.Raycast(ray, out RaycastHit hit, Land.size.y + 100f) && hit.normal.y > 0f && (!found || hit.point.y > point.y))
                {
                    point = hit.point;
                    normal = hit.normal;
                    found = true;
                }
            }
            return found;
        }

        // Whether anything but the ground (a path, a wall, a trunk, a post) is within reach of
        // a spot, from just above the grass to overhead.
        public bool Blocked(Vector3 at, float reach)
        {
            if (Occupied != null && Occupied(at, reach)) return true;
            int count = Physics.OverlapCapsuleNonAlloc(at + Vector3.up * 0.4f, at + Vector3.up * 3f, reach, overlaps,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (!grounds.Contains(overlaps[i]) && !overlaps[i].name.ToLowerInvariant().Contains("ocean")) return true;
            }
            return count == overlaps.Length;
        }

        public bool NearBuilding(Vector3 at, float margin)
        {
            foreach (Bounds box in buildings)
            {
                if (at.x > box.min.x - margin && at.x < box.max.x + margin && at.z > box.min.z - margin && at.z < box.max.z + margin) return true;
            }
            return false;
        }

        public bool NearLamp(Vector3 at, float margin)
        {
            var spot = new Vector2(at.x, at.z);
            foreach (Vector2 lamp in lamps)
            {
                if ((lamp - spot).sqrMagnitude < margin * margin) return true;
            }
            return false;
        }

        // scale shrinks the rings for small things like grass, which may come closer.
        public bool KeptClearAt(Vector3 at, float scale)
        {
            var spot = new Vector2(at.x, at.z);
            foreach ((Vector2 centre, float radius) in keptClear)
            {
                if ((centre - spot).sqrMagnitude < radius * radius * scale * scale) return true;
            }
            return NearDock(at, DockClear * scale);
        }

        public bool NearDock(Vector3 at, float margin)
        {
            if (!Dock.HasValue) return false;
            Bounds dock = Dock.Value;
            return at.x > dock.min.x - margin && at.x < dock.max.x + margin && at.z > dock.min.z - margin && at.z < dock.max.z + margin;
        }

        public Zone ZoneAt(float x, float z)
        {
            Zone best = Fallback;
            var spot = new Vector2(x, z);
            foreach (Zone zone in ZoneList)
            {
                if (zone.Area.Contains(spot) && zone.Trees > best.Trees) best = zone;
            }
            return best;
        }
    }

    private readonly struct Plant
    {
        public readonly string Name;
        public readonly Vector3 Base, Crown;
        public readonly float TrunkRadius, TrunkHeight, CrownRadius;

        public Plant(string name, Vector3 at, float trunkRadius, float trunkHeight, Vector3 crown, float crownRadius)
        {
            Name = name;
            Base = at;
            TrunkRadius = trunkRadius;
            TrunkHeight = trunkHeight;
            Crown = crown;
            CrownRadius = crownRadius;
        }
    }

    // ---- Planting ---------------------------------------------------------------------

    // Returns how many of each were grown: broadleaves, poplars, pines, bushes, ferns, grass
    // tufts, flowers, boulders, stumps.
    private static int[] GrowPlants(Site site, Dictionary<(int, int, Layer), Builder> patches, List<Plant> plants, System.Random random)
    {
        var counts = new int[9];
        var trees = new List<(Vector2 at, float room)>();
        var bushes = new List<(Vector2 at, float room)>();

        // Trees.
        Scatter(site, random, 3f, (x, z) =>
        {
            Zone zone = site.ZoneAt(x, z);
            return zone.Trees * (zone.Trees >= site.WoodDensity ? Clump(x, z) : 1f);
        }, (point, normal) =>
        {
            if (normal.y < 0.86f || point.y < site.SeaLevel + 0.8f || site.KeptClearAt(point, 1f) || site.NearLamp(point, site.LampClearance)) return;
            Zone zone = site.ZoneAt(point.x, point.z);
            float pick = Next(random);
            int kind = pick < zone.Broadleaf ? 0 : pick < zone.Broadleaf + zone.Poplar ? 1 : 2;
            bool young = Next(random) < (kind == 2 ? 0.2f : kind == 0 ? 0.25f : 0f);
            float height = kind == 0 ? (young ? Range(random, 4.5f, 7f) : Range(random, 9f, 15f))
                : kind == 1 ? Range(random, 12f, 19f)
                : (young ? Range(random, 5f, 8f) : Range(random, 10f, 19f));
            float room = (kind == 0 ? 0.26f : kind == 1 ? 0.15f : 0.17f) * height + 0.6f;
            if (site.Blocked(point, Mathf.Min(room, 2.6f)) || site.NearBuilding(point, room + 1f) || Crowded(trees, point, room)) return;
            trees.Add((new Vector2(point.x, point.z), room));
            Builder builder = For(patches, point, Layer.Trees);
            if (kind == 0) plants.Add(Broadleaf(builder, point, random, height));
            else if (kind == 1) plants.Add(Poplar(builder, point, random, height));
            else plants.Add(Pine(builder, point, random, height));
            counts[kind]++;
        });

        // Stumps, where the woods are thick.
        Scatter(site, random, 6f, (x, z) => site.ZoneAt(x, z).Trees >= 0.9f ? 0.15f : 0f, (point, normal) =>
        {
            if (normal.y < 0.86f || site.KeptClearAt(point, 0.7f) || site.Blocked(point, 1.4f) || Crowded(trees, point, 1.2f)) return;
            trees.Add((new Vector2(point.x, point.z), 1.2f));
            plants.Add(Stump(For(patches, point, Layer.Trees), point, random));
            counts[8]++;
        });

        // Bushes.
        Scatter(site, random, 2.5f, (x, z) => site.ZoneAt(x, z).Bushes, (point, normal) =>
        {
            float size = Range(random, 1.1f, 2.2f);
            if (normal.y < 0.8f || point.y < site.SeaLevel + 0.6f || site.KeptClearAt(point, 0.7f) || site.Blocked(point, size * 0.6f + 0.9f) ||
                site.NearBuilding(point, 1.5f) || Crowded(bushes, point, size) || Crowded(trees, point, 0.2f)) return;
            bushes.Add((new Vector2(point.x, point.z), size));
            plants.Add(Bush(For(patches, point, Layer.Trees), point, random, size));
            counts[3]++;
        });

        // Boulders: a few anywhere, more where the ground slopes.
        Scatter(site, random, 4f, (x, z) => 0.1f, (point, normal) =>
        {
            float size = Range(random, 0.5f, 1.6f);
            if (site.KeptClearAt(point, 0.6f) || site.Blocked(point, size + 0.8f) || site.NearBuilding(point, 1f) || Crowded(trees, point, size * 0.5f)) return;
            plants.Add(Boulder(For(patches, point, Layer.Trees), point, random, size, site.SeaLevel));
            counts[7]++;
        });
        Scatter(site, random, 3f, (x, z) => 0.9f, (point, normal) =>
        {
            if (normal.y > 0.955f) return;
            float size = Range(random, 0.5f, 1.5f);
            if (site.KeptClearAt(point, 0.6f) || site.Blocked(point, size + 0.8f) || site.NearBuilding(point, 1f) || Crowded(trees, point, size * 0.5f)) return;
            plants.Add(Boulder(For(patches, point, Layer.Trees), point, random, size, site.SeaLevel));
            counts[7]++;
        });

        // Ferns, in little stands.
        Scatter(site, random, 2f, (x, z) => site.ZoneAt(x, z).Ferns * (0.4f + Clump(x, z)) * 0.5f, (point, normal) =>
        {
            int stand = 1 + random.Next(3);
            for (int i = 0; i < stand; i++)
            {
                Vector2 offset = i == 0 ? Vector2.zero : RandomInCircle(random) * 1.2f;
                if (!site.GroundAt(point.x + offset.x, point.z + offset.y, out Vector3 at, out Vector3 slope) || slope.y < 0.7f ||
                    at.y < site.SeaLevel + 0.4f || site.KeptClearAt(at, 0.4f) || site.Blocked(at, 0.7f)) continue;
                FernPlant(For(patches, at, Layer.Cover), at, random);
                counts[4]++;
            }
        });

        // Grass tufts, everywhere there is grass.
        Scatter(site, random, 2f, (x, z) => site.TuftDensity, (point, normal) =>
        {
            if (normal.y < 0.75f || point.y < site.SeaLevel + 0.4f || site.KeptClearAt(point, 0.25f) || site.Blocked(point, 0.5f)) return;
            GrassTuft(For(patches, point, Layer.Cover), point, random);
            counts[5]++;
        });

        // Flowers, a patch of one colour at a time.
        Scatter(site, random, 4f, (x, z) => site.ZoneAt(x, z).Flowers, (point, normal) =>
        {
            int ramp = PetalWhite + random.Next(4);
            int blooms = 5 + random.Next(6);
            for (int i = 0; i < blooms; i++)
            {
                Vector2 offset = RandomInCircle(random) * 1.4f;
                if (!site.GroundAt(point.x + offset.x, point.z + offset.y, out Vector3 at, out Vector3 slope) || slope.y < 0.75f ||
                    at.y < site.SeaLevel + 0.4f || site.KeptClearAt(at, 0.25f) || site.Blocked(at, 0.4f)) continue;
                Flower(For(patches, at, Layer.Cover), at, random, ramp);
                counts[6]++;
            }
        });
        return counts;
    }

    // Visits the island on a jittered grid, calling grow at each spot the density picks.
    private static void Scatter(Site site, System.Random random, float cell, Func<float, float, float> perHundredSquareMetres,
        Action<Vector3, Vector3> grow)
    {
        Bounds land = site.Land;
        for (float gz = land.min.z; gz < land.max.z; gz += cell)
        {
            for (float gx = land.min.x; gx < land.max.x; gx += cell)
            {
                float x = gx + Next(random) * cell, z = gz + Next(random) * cell;
                float chance = perHundredSquareMetres(x, z) * cell * cell / 100f;
                if (Next(random) >= chance) continue;
                if (site.GroundAt(x, z, out Vector3 point, out Vector3 normal)) grow(point, normal);
            }
        }
    }

    // 0 in a clearing, up to 1.7 in the thick of a wood.
    private static float Clump(float x, float z)
    {
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 0.65f, Mathf.PerlinNoise(x * 0.03f + 17.3f, z * 0.03f + 41.7f))) * 1.7f;
    }

    private static bool Crowded(List<(Vector2 at, float room)> taken, Vector3 point, float room)
    {
        var spot = new Vector2(point.x, point.z);
        foreach ((Vector2 at, float theirs) in taken)
        {
            float apart = room + theirs;
            if ((at - spot).sqrMagnitude < apart * apart) return true;
        }
        return false;
    }

    // ---- Plants -----------------------------------------------------------------------

    // The side trunks and boughs are lit from, so bark shades in a band around them.
    private static readonly Vector3 LitSide = new Vector3(0.55f, 0f, -0.83f);

    // A flared, slightly bent trunk forking into boughs, a lumpy leaf mass on the end of each
    // and a crown on top.
    private static Plant Broadleaf(Builder b, Vector3 at, System.Random random, float height)
    {
        int leaves = random.Next(3) == 0 ? LeafOlive : random.Next(2) == 0 ? LeafDeep : LeafFresh;
        int bark = random.Next(4) == 0 ? BarkGrey : BarkBrown;
        float girth = height * 0.05f + 0.1f;
        float trunkHeight = height * Range(random, 0.42f, 0.52f);
        Vector3 bend = Quaternion.Euler(0f, Range(random, 0f, 360f), 0f) * Vector3.forward * (height * Range(random, 0f, 0.07f));
        Vector3 fork = at + Vector3.up * trunkHeight + bend;
        Tube(b, new[] { at + Vector3.down * 0.7f, at, at + Vector3.up * (trunkHeight * 0.12f), at + Vector3.up * (trunkHeight * 0.5f) + bend * 0.3f, fork },
            new[] { girth * 1.9f, girth * 1.5f, girth * 1.08f, girth * 0.95f, girth * 0.7f }, 8, bark, 0.15f, 0.8f, false, 0);

        float seed = Range(random, 0f, 100f);
        int boughs = 3 + random.Next(3);
        float spin = Range(random, 0f, 360f);
        float reach = 0f;
        for (int i = 0; i < boughs; i++)
        {
            float around = spin + i * 360f / boughs + Range(random, -22f, 22f);
            float up = Range(random, 22f, 48f);
            float length = height * Range(random, 0.2f, 0.3f);
            Vector3 direction = Quaternion.Euler(0f, around, 0f) * (Quaternion.Euler(-up, 0f, 0f) * Vector3.forward);
            Vector3 from = at + Vector3.up * (trunkHeight * Range(random, 0.72f, 0.95f)) + bend * 0.8f;
            Vector3 tip = from + direction * length;
            Tube(b, new[] { from, Vector3.Lerp(from, tip, 0.5f) + Vector3.up * (length * 0.06f), tip },
                new[] { girth * 0.42f, girth * 0.32f, girth * 0.2f }, 5, bark, 0.1f, 0.7f, false, 0);
            float mass = height * Range(random, 0.17f, 0.23f);
            Blob(b, tip + Vector3.up * (mass * 0.35f), new Vector3(mass, mass * Range(random, 0.72f, 0.86f), mass), 2, 0.2f, seed + i * 7.3f, leaves, -0.08f);
            reach = Mathf.Max(reach, length + mass);
        }
        float top = height * Range(random, 0.22f, 0.28f);
        Vector3 crown = fork + Vector3.up * (height - trunkHeight - top * 0.75f);
        Blob(b, crown, new Vector3(top, top * 0.8f, top), 2, 0.2f, seed + 51f, leaves, 0.06f);
        Vector2 aside = RandomInCircle(random) * (height * 0.12f);
        Blob(b, fork + new Vector3(aside.x, height * 0.14f, aside.y), Vector3.one * (height * 0.19f), 2, 0.22f, seed + 77f, leaves, -0.04f);

        Vector3 middle = fork + Vector3.up * ((height - trunkHeight) * 0.45f);
        return new Plant("Tree Broadleaf", at, girth * 1.1f, trunkHeight, middle, Mathf.Max(reach * 0.8f, height * 0.26f));
    }

    // Tall and narrow: leaf masses stacked up the trunk, each smaller than the one below.
    private static Plant Poplar(Builder b, Vector3 at, System.Random random, float height)
    {
        int leaves = random.Next(2) == 0 ? LeafFresh : LeafOlive;
        float girth = height * 0.035f + 0.1f;
        float trunkHeight = height * Range(random, 0.26f, 0.34f);
        Tube(b, new[] { at + Vector3.down * 0.7f, at, at + Vector3.up * (trunkHeight * 0.2f), at + Vector3.up * (height * 0.8f) },
            new[] { girth * 1.7f, girth * 1.35f, girth, girth * 0.3f }, 7, BarkGrey, 0.15f, 0.8f, false, 0);
        float seed = Range(random, 0f, 100f);
        int masses = 4 + random.Next(2);
        float widest = 0f;
        for (int i = 0; i < masses; i++)
        {
            float share = i / (float)(masses - 1);
            float width = height * Mathf.Lerp(0.15f, 0.07f, share) * Range(random, 0.9f, 1.12f);
            Vector2 aside = RandomInCircle(random) * (width * 0.25f);
            Vector3 centre = at + new Vector3(aside.x, Mathf.Lerp(trunkHeight + height * 0.1f, height * 0.9f, share), aside.y);
            Blob(b, centre, new Vector3(width, width * 1.45f, width), 2, 0.16f, seed + i * 9.1f, leaves, share * 0.14f - 0.07f);
            widest = Mathf.Max(widest, width);
        }
        return new Plant("Tree Poplar", at, girth * 1.1f, trunkHeight, at + Vector3.up * (height * 0.6f), widest * 1.3f);
    }

    // Tiers of drooping, scalloped boughs, each a cone a little smaller than the one below.
    private static Plant Pine(Builder b, Vector3 at, System.Random random, float height)
    {
        float girth = height * 0.035f + 0.1f;
        Tube(b, new[] { at + Vector3.down * 0.7f, at, at + Vector3.up * (height * 0.15f), at + Vector3.up * (height * 0.9f) },
            new[] { girth * 1.6f, girth * 1.3f, girth, girth * 0.25f }, 7, BarkBrown, 0.1f, 0.7f, false, 0);
        int tiers = 4 + random.Next(3);
        float spread = height * Range(random, 0.22f, 0.28f);
        float spin = Range(random, 0f, 360f);
        for (int i = 0; i < tiers; i++)
        {
            float share = i / (float)tiers;
            float radius = spread * (1f - 0.8f * share);
            float foot = height * (0.2f + 0.72f * share);
            float rise = height * 0.8f / tiers * 1.8f;
            if (i == tiers - 1) rise = height - foot;
            int points = 9 + random.Next(3);
            Vector3 centre = at + Vector3.up * foot;
            Vector3 apex = centre + Vector3.up * rise;
            Vector3 under = centre + Vector3.up * (radius * 0.3f);
            var rim = new Vector3[points];
            for (int p = 0; p < points; p++)
            {
                bool tip = p % 2 == 0;
                float around = (spin + i * 23f + p * 360f / points) * Mathf.Deg2Rad;
                float r = radius * (tip ? 1f : 0.72f) * Range(random, 0.92f, 1.08f);
                rim[p] = centre + new Vector3(Mathf.Cos(around) * r, -radius * (tip ? 0.3f : 0.08f), Mathf.Sin(around) * r);
            }
            float light = share * 0.12f;
            for (int p = 0; p < points; p++)
            {
                Vector3 a = rim[p], c = rim[(p + 1) % points];
                Vector3 outward = (a + c) * 0.5f - centre;
                b.Flat(a, c, apex, Shade(LeafPine, 0.2f + light), Shade(LeafPine, 0.2f + light), Shade(LeafPine, 0.9f), outward + Vector3.up * radius);
                b.Flat(a, c, under, Shade(LeafPine, 0.05f), Shade(LeafPine, 0.05f), Shade(LeafPine, 0.05f), Vector3.down);
            }
        }
        float crownRadius = spread * 0.7f;
        return new Plant("Tree Pine", at, girth * 1.1f, height * 0.25f, at + Vector3.up * (height * 0.5f), crownRadius);
    }

    private static Plant Bush(Builder b, Vector3 at, System.Random random, float size)
    {
        int leaves = random.Next(3) == 0 ? LeafOlive : random.Next(2) == 0 ? LeafFresh : LeafDeep;
        float seed = Range(random, 0f, 100f);
        int masses = 3 + random.Next(3);
        for (int i = 0; i < masses; i++)
        {
            Vector2 aside = i == 0 ? Vector2.zero : RandomInCircle(random) * (size * 0.55f);
            float width = size * Range(random, 0.45f, 0.7f);
            Blob(b, at + new Vector3(aside.x, width * Range(random, 0.35f, 0.6f), aside.y), new Vector3(width, width * 0.85f, width), 1, 0.22f,
                seed + i * 5.7f, leaves, 0f);
        }
        return new Plant("Bush", at, 0f, 0f, at + Vector3.up * (size * 0.4f), size * 0.7f);
    }

    private static Plant Stump(Builder b, Vector3 at, System.Random random)
    {
        float radius = Range(random, 0.45f, 0.85f), height = Range(random, 0.45f, 0.9f);
        Vector3 top = at + Vector3.up * height;
        Tube(b, new[] { at + Vector3.down * 0.5f, at, at + Vector3.up * (height * 0.3f), top },
            new[] { radius * 1.6f, radius * 1.35f, radius * 1.02f, radius * 0.95f }, 8, BarkBrown, 0.15f, 0.8f, true, Wood);
        return new Plant("Stump", at, radius, height, default, 0f);
    }

    private static Plant Boulder(Builder b, Vector3 at, System.Random random, float size, float seaLevel)
    {
        var radii = new Vector3(size * Range(random, 0.8f, 1.2f), size * Range(random, 0.5f, 0.8f), size * Range(random, 0.8f, 1.2f));
        Vector3 centre = at + Vector3.up * (radii.y * 0.3f);
        RockShape(b, centre, radii, Quaternion.Euler(Range(random, -12f, 12f), Range(random, 0f, 360f), Range(random, -12f, 12f)),
            random.Next(), Rock, seaLevel, Range(random, -0.1f, 0.1f));
        // Only the bigger ones are in the way.
        return new Plant("Rock", at, 0f, 0f, centre, size >= 0.9f ? Mathf.Min(radii.x, radii.z) * 0.85f : 0f);
    }

    // Arching fronds from one root.
    private static void FernPlant(Builder b, Vector3 at, System.Random random)
    {
        int fronds = 6 + random.Next(5);
        float spin = Range(random, 0f, 360f);
        for (int i = 0; i < fronds; i++)
        {
            Vector3 direction = Quaternion.Euler(0f, spin + i * 360f / fronds + Range(random, -15f, 15f), 0f) * Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, direction);
            float length = Range(random, 0.7f, 1.4f), arch = length * Range(random, 0.35f, 0.6f), width = length * 0.16f;
            const int cuts = 5;
            var left = new Vector3[cuts];
            var right = new Vector3[cuts];
            var shades = new Vector2[cuts];
            for (int k = 0; k < cuts; k++)
            {
                float along = k / (float)(cuts - 1);
                Vector3 spine = at + direction * (length * along) + Vector3.up * (arch * Mathf.Sin(along * Mathf.PI * 0.8f));
                float half = width * Mathf.Sin(Mathf.PI * (0.12f + 0.88f * along));
                left[k] = spine - side * half;
                right[k] = spine + side * half;
                shades[k] = Shade(Fern, 0.15f + 0.75f * along);
            }
            b.Strip(left, right, shades, (Vector3.up + direction * 0.3f).normalized);
        }
    }

    private static void GrassTuft(Builder b, Vector3 at, System.Random random)
    {
        int blades = 5 + random.Next(5);
        for (int i = 0; i < blades; i++)
        {
            Vector2 aside = RandomInCircle(random) * 0.28f;
            Blade(b, at + new Vector3(aside.x, -0.03f, aside.y), random, Range(random, 0.3f, 0.8f), Range(random, 0.06f, 0.11f), Grass);
        }
    }

    private static void Flower(Builder b, Vector3 at, System.Random random, int petals)
    {
        float height = Range(random, 0.3f, 0.6f);
        Vector3 head = Blade(b, at, random, height, 0.035f, Grass);
        float size = Range(random, 0.08f, 0.13f);
        Vector3 top = head + Vector3.up * size, bottom = head + Vector3.down * (size * 0.6f);
        const int sides = 5;
        float spin = Range(random, 0f, Mathf.PI);
        for (int i = 0; i < sides; i++)
        {
            float a0 = spin + i * Mathf.PI * 2f / sides, a1 = spin + (i + 1) * Mathf.PI * 2f / sides;
            Vector3 p0 = head + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * (size * 1.4f);
            Vector3 p1 = head + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * (size * 1.4f);
            Vector3 outward = (p0 + p1) * 0.5f - head;
            b.Flat(p0, p1, top, Shade(petals, 0.6f), Shade(petals, 0.6f), Shade(petals, 0.95f), outward + Vector3.up);
            b.Flat(p0, p1, bottom, Shade(petals, 0.15f), Shade(petals, 0.15f), Shade(petals, 0.15f), outward + Vector3.down);
        }
    }

    // One tapering blade leaning a random way; returns its tip.
    private static Vector3 Blade(Builder b, Vector3 at, System.Random random, float height, float width, int ramp)
    {
        Vector3 lean = Quaternion.Euler(0f, Range(random, 0f, 360f), 0f) * Vector3.forward;
        Vector3 side = Vector3.Cross(Vector3.up, lean);
        float droop = Range(random, 0.15f, 0.5f);
        Vector3 mid = at + Vector3.up * (height * 0.55f) + lean * (height * droop * 0.3f);
        Vector3 tip = at + Vector3.up * height + lean * (height * droop);
        b.Strip(new[] { at - side * width, mid - side * (width * 0.6f), tip }, new[] { at + side * width, mid + side * (width * 0.6f), tip },
            new[] { Shade(ramp, 0.12f), Shade(ramp, 0.55f), Shade(ramp, 0.95f) }, (Vector3.up + lean * 0.25f).normalized);
        return tip;
    }

    // ---- Shapes -----------------------------------------------------------------------

    // A ring of sides around each centre, joined into a tube. Rings are level, so it suits
    // trunks and boughs that mostly go up. capRamp caps the top (a stump's cut face).
    private static void Tube(Builder b, Vector3[] centres, float[] radii, int sides, int ramp, float shadeLow, float shadeHigh, bool cap, int capRamp)
    {
        int first = b.Positions.Count;
        int ring = sides + 1;
        for (int k = 0; k < centres.Length; k++)
        {
            for (int j = 0; j <= sides; j++)
            {
                float around = j * Mathf.PI * 2f / sides;
                var outward = new Vector3(Mathf.Cos(around), 0f, Mathf.Sin(around));
                float lit = 0.5f + 0.5f * Vector3.Dot(outward, LitSide);
                b.Add(centres[k] + outward * radii[k], outward, Shade(ramp, Mathf.Lerp(shadeLow, shadeHigh, lit)));
            }
        }
        for (int k = 0; k + 1 < centres.Length; k++)
        {
            for (int j = 0; j < sides; j++)
            {
                int lower = first + k * ring + j, upper = lower + ring;
                b.Triangle(lower, upper, lower + 1);
                b.Triangle(lower + 1, upper, upper + 1);
            }
        }
        if (!cap) return;
        int last = centres.Length - 1;
        int hub = b.Add(centres[last], Vector3.up, Shade(capRamp, 0.8f));
        int rim = b.Positions.Count;
        for (int j = 0; j <= sides; j++)
        {
            float around = j * Mathf.PI * 2f / sides;
            b.Add(centres[last] + new Vector3(Mathf.Cos(around), 0f, Mathf.Sin(around)) * radii[last], Vector3.up, Shade(capRamp, 0.45f));
        }
        for (int j = 0; j < sides; j++) b.Triangle(hub, rim + j + 1, rim + j);
    }

    // A lumpy ball of leaves, flatter underneath, shaded lighter towards the sky.
    private static void Blob(Builder b, Vector3 centre, Vector3 radii, int detail, float lumpiness, float seed, int ramp, float lighter)
    {
        Sphere sphere = Sphere.Of(detail);
        int first = b.Positions.Count, firstTriangle = b.Triangles.Count;
        foreach (Vector3 direction in sphere.Points)
        {
            float lump = Mathf.PerlinNoise(direction.x * 1.7f + seed, direction.y * 1.7f + seed * 0.37f)
                + Mathf.PerlinNoise(direction.z * 1.7f + seed * 0.71f, direction.y * 1.3f - seed) - 1f;
            Vector3 point = direction * (1f + lump * lumpiness);
            if (point.y < 0f) point.y *= 0.6f;
            b.Add(centre + Vector3.Scale(point, radii), direction, Shade(ramp, 0.5f + 0.42f * direction.y + 0.14f * lump + lighter));
        }
        foreach (int index in sphere.Triangles) b.Triangles.Add(first + index);
        b.SmoothNormals(first, firstTriangle);
    }

    // A faceted lump of rock: a coarse ball with every corner pushed in or out.
    private static void RockShape(Builder b, Vector3 centre, Vector3 radii, Quaternion turn, int seed, int ramp, float seaLevel, float tone)
    {
        Sphere sphere = Sphere.Of(1);
        var points = new Vector3[sphere.Points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            float push = 1f + (Hash(seed, i, 0) * 2f - 1f) * 0.26f;
            points[i] = centre + turn * Vector3.Scale(sphere.Points[i] * push, radii);
        }
        for (int t = 0; t < sphere.Triangles.Length; t += 3)
        {
            Vector3 a = points[sphere.Triangles[t]], c = points[sphere.Triangles[t + 1]], d = points[sphere.Triangles[t + 2]];
            Vector3 middle = (a + c + d) / 3f;
            Vector3 normal = Vector3.Cross(c - a, d - a).normalized;
            if (Vector3.Dot(normal, middle - centre) < 0f) normal = -normal;
            Vector2 shade = Shade(middle.y < seaLevel + 0.45f ? RockWet : ramp, 0.3f + 0.45f * Mathf.Clamp01(normal.y * 0.6f + 0.45f) + tone);
            b.Flat(a, c, d, shade, shade, shade, middle - centre);
        }
    }

    // A unit ball of triangles, cut finer with each level of detail.
    private sealed class Sphere
    {
        public Vector3[] Points;
        public int[] Triangles;
        private static readonly Dictionary<int, Sphere> made = new Dictionary<int, Sphere>();

        public static Sphere Of(int detail)
        {
            if (made.TryGetValue(detail, out Sphere sphere)) return sphere;
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            var points = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            for (int i = 0; i < points.Count; i++) points[i] = points[i].normalized;
            var triangles = new List<int>
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };
            for (int level = 0; level < detail; level++)
            {
                var middles = new Dictionary<long, int>();
                var finer = new List<int>(triangles.Count * 4);
                for (int i = 0; i < triangles.Count; i += 3)
                {
                    int a = triangles[i], c = triangles[i + 1], d = triangles[i + 2];
                    int ac = Middle(a, c), cd = Middle(c, d), da = Middle(d, a);
                    finer.AddRange(new[] { a, ac, da, c, cd, ac, d, da, cd, ac, cd, da });
                }
                triangles = finer;

                int Middle(int a, int c)
                {
                    long key = a < c ? ((long)a << 32) | (uint)c : ((long)c << 32) | (uint)a;
                    if (middles.TryGetValue(key, out int index)) return index;
                    index = points.Count;
                    points.Add(((points[a] + points[c]) * 0.5f).normalized);
                    middles[key] = index;
                    return index;
                }
            }
            // Faces out, the way Unity draws them.
            for (int i = 0; i < triangles.Count; i += 3)
            {
                Vector3 a = points[triangles[i]], c = points[triangles[i + 1]], d = points[triangles[i + 2]];
                if (Vector3.Dot(Vector3.Cross(c - a, d - a), a + c + d) < 0f)
                {
                    int swap = triangles[i + 1];
                    triangles[i + 1] = triangles[i + 2];
                    triangles[i + 2] = swap;
                }
            }
            sphere = new Sphere { Points = points.ToArray(), Triangles = triangles.ToArray() };
            made[detail] = sphere;
            return sphere;
        }
    }

    // ---- Shore ------------------------------------------------------------------------

    // Rocks from the island's edge down into the sea, all the way round: a faceted bank that
    // steps out further the higher the cliff above it, with boulders along the waterline.
    // The dock's foot is left smooth. Returns the length of shore covered, in metres.
    private static int BuildShore(Site site, MeshFilter ground, Dictionary<(int, int, Layer), Builder> patches, List<Plant> plants)
    {
        float covered = 0f;
        int loopSeed = 0;
        foreach (List<(Vector3 point, Vector3 outward)> loop in Coast(ground, site.SeaLevel))
        {
            loopSeed += 1000;
            int count = loop.Count;
            covered += count * ShoreStep;
            var rings = new Vector3[count, ShoreRings + 1];
            var calm = new bool[count];
            var runs = new float[count];
            for (int k = 0; k < count; k++)
            {
                // The run is eased along the shore so the bank doesn't jump where the cliff does.
                float sum = 0f;
                for (int w = -2; w <= 2; w++) sum += ShoreRun + ShoreRunPerMetre * Mathf.Max(0f, loop[(k + w + count) % count].point.y - site.SeaLevel);
                runs[k] = sum / 5f;
                calm[k] = site.NearDock(loop[k].point, 3f);
            }
            for (int k = 0; k < count; k++)
            {
                (Vector3 edge, Vector3 outward) = loop[k];
                float cliff = Mathf.Max(0f, edge.y - site.SeaLevel);
                int block = loopSeed + k / 2;
                float lip = calm[k] ? -0.05f : 0.15f + 0.35f * Hash(block, 0, 9);
                float rough = calm[k] ? 0.1f : Mathf.Min(1.2f, 0.35f + 0.08f * cliff);

                Vector3 inland = edge - outward * 1f;
                inland.y = (site.GroundAt(inland.x, inland.z, out Vector3 turf, out _) ? turf.y : edge.y) - 0.25f;
                rings[k, 0] = inland;
                rings[k, 1] = edge + outward * 0.15f + Vector3.up * lip;
                for (int j = 2; j <= ShoreRings; j++)
                {
                    float down = (j - 1) / (float)(ShoreRings - 1);
                    float reach = 0.15f + runs[k] * Mathf.Pow(down, 0.9f) + (Hash(block, j, 1) - 0.5f) * rough;
                    float y = Mathf.Lerp(edge.y + lip * 0.6f, site.SeaLevel - ShoreDepth, Mathf.Pow(down, 1.2f)) + (Hash(block, j, 2) - 0.5f) * rough;
                    if (j == ShoreRings) y = Mathf.Min(y, site.SeaLevel - 1f);
                    Vector3 rock = edge + outward * reach;
                    rock.y = y;
                    rings[k, j] = rock;
                }
            }

            for (int k = 0; k < count; k++)
            {
                int next = (k + 1) % count;
                Builder builder = For(patches, loop[k].point, Layer.Shore);
                Vector3 facing = loop[k].outward + Vector3.up * 0.5f;
                for (int j = 0; j < ShoreRings; j++)
                {
                    Vector3 a = rings[k, j], c = rings[next, j], d = rings[k, j + 1], e = rings[next, j + 1];
                    float tone = (Hash(loopSeed + k / 2, j, 3) - 0.5f) * 0.25f;
                    ShoreFace(builder, a, c, d, facing, tone, site.SeaLevel);
                    ShoreFace(builder, c, e, d, facing, tone, site.SeaLevel);
                }

                if (calm[k]) continue;
                // Boulders: along the waterline, and partway up the higher cliffs.
                float cliff = Mathf.Max(0f, loop[k].point.y - site.SeaLevel);
                if (Hash(loopSeed + k, 0, 5) < 0.42f)
                {
                    float size = Mathf.Lerp(0.9f, 2.3f, Hash(loopSeed + k, 0, 6) * Hash(loopSeed + k, 0, 7));
                    Vector3 at = loop[k].point + loop[k].outward * (0.15f + runs[k] * Mathf.Lerp(0.7f, 1f, Hash(loopSeed + k, 0, 8)));
                    at.y = site.SeaLevel + size * Mathf.Lerp(-0.35f, 0.1f, Hash(loopSeed + k, 1, 8));
                    RockShape(builder, at, new Vector3(size, size * Mathf.Lerp(0.55f, 0.8f, Hash(loopSeed + k, 2, 8)), size * Mathf.Lerp(0.8f, 1.2f, Hash(loopSeed + k, 3, 8))),
                        Quaternion.Euler(0f, Hash(loopSeed + k, 4, 8) * 360f, 0f), loopSeed + k, Rock, site.SeaLevel, 0f);
                }
                if (cliff > 3.5f && Hash(loopSeed + k, 0, 10) < 0.3f)
                {
                    int ring = 2 + (int)(Hash(loopSeed + k, 1, 10) * (ShoreRings - 3));
                    float size = Mathf.Lerp(0.8f, 1.7f, Hash(loopSeed + k, 2, 10));
                    RockShape(builder, Vector3.Lerp(rings[k, ring], rings[k, ring + 1], 0.5f), new Vector3(size, size * 0.7f, size),
                        Quaternion.Euler(0f, Hash(loopSeed + k, 3, 10) * 360f, 0f), loopSeed + k + 500, Rock, site.SeaLevel, 0.05f);
                }
            }
        }
        return Mathf.RoundToInt(covered);
    }

    private static void ShoreFace(Builder b, Vector3 a, Vector3 c, Vector3 d, Vector3 facing, float tone, float seaLevel)
    {
        Vector3 normal = Vector3.Cross(c - a, d - a).normalized;
        if (Vector3.Dot(normal, facing) < 0f) normal = -normal;
        float middle = (a.y + c.y + d.y) / 3f;
        Vector2 shade = Shade(middle < seaLevel + 0.45f ? RockWet : Rock, 0.25f + 0.5f * Mathf.Clamp01(normal.y) + tone);
        b.Flat(a, c, d, shade, shade, shade, facing);
    }

    // The outline of the island's top, as closed loops of points ShoreStep apart, each with
    // the way out to sea. Found from the ground mesh: the edges its top faces don't share.
    private static List<List<(Vector3 point, Vector3 outward)>> Coast(MeshFilter ground, float seaLevel)
    {
        Mesh mesh = ground.sharedMesh;
        Matrix4x4 toWorld = ground.transform.localToWorldMatrix;
        Vector3[] local = mesh.vertices;
        var points = new Vector3[local.Length];
        for (int i = 0; i < local.Length; i++) points[i] = toWorld.MultiplyPoint3x4(local[i]);

        // Each top face's edges, in the order it winds them; an edge two faces share cancels.
        var open = new Dictionary<(Vector3Int, Vector3Int), (Vector3 from, Vector3 to, Vector3 inside)>();
        int[] triangles = mesh.GetTriangles(0);
        for (int t = 0; t < triangles.Length; t += 3)
        {
            Vector3 a = points[triangles[t]], b = points[triangles[t + 1]], c = points[triangles[t + 2]];
            if (Vector3.Cross(b - a, c - a).normalized.y < 0.6f || Mathf.Min(a.y, Mathf.Min(b.y, c.y)) < seaLevel + 0.2f) continue;
            Edge(a, b, c);
            Edge(b, c, a);
            Edge(c, a, b);
        }

        void Edge(Vector3 from, Vector3 to, Vector3 inside)
        {
            (Vector3Int, Vector3Int) key = (Key(from), Key(to));
            if (!open.Remove((key.Item2, key.Item1))) open[key] = (from, to, inside);
        }

        // Where two stretches of shore touch at a corner, more than one edge leaves it.
        var leaving = new Dictionary<Vector3Int, List<(Vector3 from, Vector3 to, Vector3 inside)>>();
        foreach (KeyValuePair<(Vector3Int, Vector3Int), (Vector3 from, Vector3 to, Vector3 inside)> edge in open)
        {
            if (!leaving.TryGetValue(edge.Key.Item1, out List<(Vector3 from, Vector3 to, Vector3 inside)> list)) leaving[edge.Key.Item1] = list = new List<(Vector3, Vector3, Vector3)>();
            list.Add(edge.Value);
        }

        var loops = new List<List<(Vector3, Vector3)>>();
        while (leaving.Count > 0)
        {
            Vector3Int start = default;
            foreach (Vector3Int key in leaving.Keys) { start = key; break; }
            var corners = new List<(Vector3 point, Vector3 outward)>();
            Vector3Int at = start;
            float length = 0f;
            while (leaving.TryGetValue(at, out List<(Vector3 from, Vector3 to, Vector3 inside)> choices))
            {
                (Vector3 from, Vector3 to, Vector3 inside) edge = choices[choices.Count - 1];
                choices.RemoveAt(choices.Count - 1);
                if (choices.Count == 0) leaving.Remove(at);
                Vector3 along = edge.to - edge.from;
                var outward = new Vector3(along.z, 0f, -along.x);
                if (Vector3.Dot(outward, edge.inside - edge.from) > 0f) outward = -outward;
                corners.Add((edge.from, outward.normalized));
                length += along.magnitude;
                at = Key(edge.to);
                if (at == start) break;
                if (leaving.ContainsKey(at)) continue;
                // A gap in the outline: carry on from the nearest loose end, if one is close.
                float nearest = CoastGap * CoastGap;
                Vector3Int across = at;
                foreach (KeyValuePair<Vector3Int, List<(Vector3 from, Vector3 to, Vector3 inside)>> other in leaving)
                {
                    float apart = (other.Value[0].from - edge.to).sqrMagnitude;
                    if (apart < nearest)
                    {
                        nearest = apart;
                        across = other.Key;
                    }
                }
                at = across;
                if (at == start) break;
            }
            // Only whole loops of real shore; stray edges are seams in the mesh.
            if (at != start || length < 60f) continue;

            // Even steps along the loop, the way out to sea eased across each corner.
            int count = Mathf.Max(8, Mathf.RoundToInt(length / ShoreStep));
            var stepped = new List<(Vector3, Vector3)>(count);
            int corner = 0;
            float walked = 0f;
            for (int k = 0; k < count; k++)
            {
                float target = length * k / count;
                while (true)
                {
                    float span = (corners[(corner + 1) % corners.Count].point - corners[corner].point).magnitude;
                    if (walked + span >= target || corner == corners.Count - 1) break;
                    walked += span;
                    corner++;
                }
                Vector3 from = corners[corner].point, to = corners[(corner + 1) % corners.Count].point;
                float share = Mathf.Clamp01((target - walked) / Mathf.Max((to - from).magnitude, 0.001f));
                stepped.Add((Vector3.Lerp(from, to, share), corners[corner].outward));
            }
            var eased = new List<(Vector3, Vector3)>(count);
            for (int k = 0; k < count; k++)
            {
                Vector3 sum = Vector3.zero;
                for (int w = -3; w <= 3; w++) sum += stepped[(k + w + count) % count].Item2;
                sum.y = 0f;
                eased.Add((stepped[k].Item1, sum.sqrMagnitude > 0.0001f ? sum.normalized : stepped[k].Item2));
            }
            loops.Add(eased);
        }
        return loops;
    }

    private static Vector3Int Key(Vector3 point)
    {
        return new Vector3Int(Mathf.RoundToInt(point.x * 20f), Mathf.RoundToInt(point.y * 20f), Mathf.RoundToInt(point.z * 20f));
    }

    // ---- Mesh -------------------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    private struct PackedVertex
    {
        public Vector3 Position;
        public sbyte NormalX, NormalY, NormalZ, NormalW;
        public ushort U, V;
    }

    private sealed class Builder
    {
        public readonly List<Vector3> Positions = new List<Vector3>();
        public readonly List<Vector3> Normals = new List<Vector3>();
        public readonly List<Vector2> Uvs = new List<Vector2>();
        public readonly List<int> Triangles = new List<int>();

        public int Add(Vector3 position, Vector3 normal, Vector2 uv)
        {
            Positions.Add(position);
            Normals.Add(normal);
            Uvs.Add(uv);
            return Positions.Count - 1;
        }

        public void Triangle(int a, int b, int c)
        {
            Triangles.Add(a);
            Triangles.Add(b);
            Triangles.Add(c);
        }

        // A flat-shaded triangle with corners of its own, turned to face the way given.
        public void Flat(Vector3 a, Vector3 b, Vector3 c, Vector2 uvA, Vector2 uvB, Vector2 uvC, Vector3 facing)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            if (Vector3.Dot(normal, facing) < 0f)
            {
                normal = -normal;
                (b, c) = (c, b);
                (uvB, uvC) = (uvC, uvB);
            }
            Triangle(Add(a, normal, uvA), Add(b, normal, uvB), Add(c, normal, uvC));
        }

        // A ribbon between two edges, seen from both sides, lit as if it faced one way (so
        // grass and fronds shade like the ground they grow from). The edges may meet at a tip.
        public void Strip(Vector3[] left, Vector3[] right, Vector2[] uvs, Vector3 normal)
        {
            int first = Positions.Count;
            for (int k = 0; k < left.Length; k++)
            {
                Add(left[k], normal, uvs[k]);
                Add(right[k], normal, uvs[k]);
            }
            for (int k = 0; k + 1 < left.Length; k++)
            {
                int a = first + k * 2, b = a + 1, c = a + 2, d = a + 3;
                Triangle(a, c, b);
                Triangle(b, c, d);
                Triangle(a, b, c);
                Triangle(b, d, c);
            }
        }

        // Rounds off the shading of everything added since first.
        public void SmoothNormals(int first, int firstTriangle)
        {
            for (int i = first; i < Normals.Count; i++) Normals[i] = Vector3.zero;
            for (int t = firstTriangle; t < Triangles.Count; t += 3)
            {
                int a = Triangles[t], b = Triangles[t + 1], c = Triangles[t + 2];
                Vector3 normal = Vector3.Cross(Positions[b] - Positions[a], Positions[c] - Positions[a]);
                Normals[a] += normal;
                Normals[b] += normal;
                Normals[c] += normal;
            }
            for (int i = first; i < Normals.Count; i++) Normals[i] = Normals[i].sqrMagnitude > 0f ? Normals[i].normalized : Vector3.up;
        }

        // Into another space (a parent that is moved or turned, but not scaled).
        public void Move(Matrix4x4 into)
        {
            for (int i = 0; i < Positions.Count; i++)
            {
                Positions[i] = into.MultiplyPoint3x4(Positions[i]);
                Normals[i] = into.MultiplyVector(Normals[i]).normalized;
            }
        }

        // Packed small (byte normals, 16-bit UVs): these meshes are saved with the project.
        public Mesh ToMesh(string name)
        {
            int count = Positions.Count;
            var mesh = new Mesh { name = name };
            mesh.SetVertexBufferParams(count,
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.SNorm8, 4),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.UNorm16, 2));
            var packed = new PackedVertex[count];
            for (int i = 0; i < count; i++)
            {
                Vector3 n = Normals[i];
                packed[i] = new PackedVertex
                {
                    Position = Positions[i],
                    NormalX = (sbyte)Mathf.RoundToInt(Mathf.Clamp(n.x, -1f, 1f) * 127f),
                    NormalY = (sbyte)Mathf.RoundToInt(Mathf.Clamp(n.y, -1f, 1f) * 127f),
                    NormalZ = (sbyte)Mathf.RoundToInt(Mathf.Clamp(n.z, -1f, 1f) * 127f),
                    U = (ushort)Mathf.RoundToInt(Mathf.Clamp01(Uvs[i].x) * 65535f),
                    V = (ushort)Mathf.RoundToInt(Mathf.Clamp01(Uvs[i].y) * 65535f),
                };
            }
            mesh.SetVertexBufferData(packed, 0, 0, count);
            mesh.indexFormat = count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetTriangles(Triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    // ---- Material ---------------------------------------------------------------------

    private static Material SceneryMaterial(Transform island)
    {
        var texture = new Texture2D(4, RampCount, TextureFormat.RGB24, false);
        for (int ramp = 0; ramp < RampCount; ramp++)
        {
            string[] shades = Ramps[ramp].Split(' ');
            for (int i = 0; i < 4; i++)
            {
                ColorUtility.TryParseHtmlString("#" + shades[i], out Color color);
                texture.SetPixel(i, ramp, color);
            }
        }
        texture.Apply();
        File.WriteAllBytes(PalettePath, texture.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(PalettePath);
        if (AssetImporter.GetAtPath(PalettePath) is TextureImporter importer &&
            (importer.filterMode != FilterMode.Point || importer.mipmapEnabled || importer.textureCompression != TextureImporterCompression.Uncompressed))
        {
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
        Texture2D palette = AssetDatabase.LoadAssetAtPath<Texture2D>(PalettePath);

        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            // Matte like the model's leaves: start from one of them.
            Material leaves = null;
            foreach (Renderer renderer in island.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.sharedMaterial != null && renderer.sharedMaterial.name.StartsWith("Leaves", StringComparison.Ordinal))
                {
                    leaves = renderer.sharedMaterial;
                    break;
                }
            }
            material = leaves != null ? new Material(leaves) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.name = "SceneryMat";
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        if (material.HasProperty("_BaseMap"))
        {
            material.SetTexture("_BaseMap", palette);
            material.SetTextureScale("_BaseMap", Vector2.one);
            material.SetTextureOffset("_BaseMap", Vector2.zero);
        }
        if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", palette);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
        if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0f);
        EditorUtility.SetDirty(material);
        return material;
    }

    // ---- Chance -----------------------------------------------------------------------

    private static float Next(System.Random random)
    {
        return (float)random.NextDouble();
    }

    private static float Range(System.Random random, float low, float high)
    {
        return low + (high - low) * (float)random.NextDouble();
    }

    private static Vector2 RandomInCircle(System.Random random)
    {
        float around = Range(random, 0f, Mathf.PI * 2f), out_ = Mathf.Sqrt(Next(random));
        return new Vector2(Mathf.Cos(around), Mathf.Sin(around)) * out_;
    }

    // 0..1 from three whole numbers, the same every run.
    private static float Hash(int a, int b, int c)
    {
        unchecked
        {
            uint h = (uint)(a * 374761393 + b * 668265263 + c * 2147483629);
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xffff) / 65535f;
        }
    }

    // FNV-1a: the same across sessions, unlike string.GetHashCode.
    private static int StableHash(string text)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char c in text) hash = (hash ^ c) * 16777619;
            return (int)hash;
        }
    }
}
