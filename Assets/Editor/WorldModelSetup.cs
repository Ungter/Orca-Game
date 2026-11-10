using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Builds runtime-safe URP prefabs from the supplied models without changing their geometry.
// The lighthouse's many small meshes are combined by material instead of adding 1,913 renderers.
public static class WorldModelSetup
{
    public const string BoatPath = "Assets/Resources/WorldModels/StartingBoat.prefab";
    public const string CounterPath = "Assets/Resources/WorldModels/DolphinCounter.prefab";
    public const string IslandPath = "Assets/Generated/WorldModels/LighthouseIsland.prefab";
    public const string IslandName = "Lighthouse island";
    public const string SectionPath = "Assets/Resources/WorldModels/LighthouseSection.prefab";
    public const string LanternPath = "Assets/Resources/WorldModels/LighthouseLantern.prefab";
    private const string Generated = "Assets/Generated/WorldModels";
    private const string Materials = "Assets/Materials/WorldModels";

    [MenuItem("Tools/Orca/Build Boat, Lighthouse Island And Dolphin Counter")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Build the world models outside Play Mode.");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/SampleScene.unity" || scene.isDirty)
            throw new InvalidOperationException("Open a saved SampleScene before placing the lighthouse island.");
        Folder(Generated);
        Folder(Materials);
        Folder("Assets/Resources/WorldModels");
        BuildBoat();
        BuildCounter();
        BuildIsland();
        PlaceIsland();
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(scene);
    }

    private static void BuildBoat()
    {
        GameObject root = new GameObject("Starting boat");
        try
        {
            GameObject model = Model("Assets/Models/gameboat.fbx", root.transform, "Boat");
            model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            Bounds bounds = BoundsOf(model);
            float scale = OpeningVoyage.BoatLength / bounds.size.z;
            // Stand on the aft bench inside the hull, not on the rim or in the mast.
            Bounds deck = BoatFit.RidingBench(root).bounds;
            model.transform.localScale = Vector3.one * scale;
            model.transform.localPosition = new Vector3(-bounds.center.x * scale,
                0.4f - deck.max.y * scale, -bounds.center.z * scale);
            BoxCollider hull = root.AddComponent<BoxCollider>();
            hull.size = new Vector3(bounds.size.x * scale, 0.8f, OpeningVoyage.BoatLength);
            root.AddComponent<LighthouseFerry>();
            PrefabUtility.SaveAsPrefabAsset(root, BoatPath);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static void BuildCounter()
    {
        GameObject root = new GameObject("Dolphin counter");
        try
        {
            GameObject model = Model("Assets/Sprites/DolphinStatue.fbx", root.transform, "Dolphin");
            Bounds bounds = BoundsOf(model);
            float scale = Mathf.Min(0.18f / bounds.size.y, 0.12f / Mathf.Max(bounds.size.x, bounds.size.z));
            model.transform.localScale = Vector3.one * scale;
            // A bottom-centred pivot keeps the statue on the felt throughout each hop.
            model.transform.localPosition = -new Vector3(bounds.center.x, bounds.min.y, bounds.center.z) * scale;
            foreach (Transform part in root.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = TwentyOneTable.Layer;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                renderer.renderingLayerMask = 1u << TwentyOneTable.LightLayer;
            PrefabUtility.SaveAsPrefabAsset(root, CounterPath);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static void BuildIsland()
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath("Assets/Models/lighthouse island.fbx");
        if (!importer.isReadable)
        {
            importer.isReadable = true;
            importer.SaveAndReimport();
        }
        GameObject root = new GameObject(IslandName);
        try
        {
            GameObject model = Model("Assets/Models/lighthouse island.fbx", root.transform, "Lighthouse");
            Bounds bounds = BoundsOf(model);
            model.transform.localScale = Vector3.one * 100f;
            // The model's dock is at y=0; preserve that waterline and centre the footprint.
            model.transform.localPosition = -new Vector3(bounds.center.x, 0f, bounds.center.z) * 100f;
            Renderer[] towerParts = model.GetComponentsInChildren<Renderer>()
                .Where(r => r.transform.parent == model.transform &&
                    new[] { "pCylinder1", "pCylinder2", "pCylinder3", "pCylinder4" }.Contains(r.name)).ToArray();
            Renderer body = towerParts.Single(r => r.name == "pCylinder1");
            Bounds bodyBounds = body.bounds;
            BuildStackPrefab(SectionPath, new[] { body }, new Vector3(LighthouseStackMatch.StartingWidth,
                LighthouseStackView.SectionHeight, LighthouseStackMatch.StartingWidth));
            BuildStackPrefab(LanternPath, towerParts.Where(r => r != body).ToArray(), new Vector3(8.8f, 4.8f, 8.8f));
            var tower = new GameObject("Supplied lighthouse");
            tower.transform.SetParent(root.transform, false);
            tower.transform.position = new Vector3(bodyBounds.center.x, bodyBounds.min.y, bodyBounds.center.z);
            foreach (Renderer part in towerParts)
            {
                part.transform.SetParent(tower.transform, true);
                part.gameObject.AddComponent<MeshCollider>().sharedMesh = part.GetComponent<MeshFilter>().sharedMesh;
            }
            Renderer dock = model.GetComponentsInChildren<Renderer>().Where(r => r.name == "pCube1")
                .OrderByDescending(r => r.bounds.size.x * r.bounds.size.z).First();
            Bounds pierBounds = dock.bounds;
            Transform entrance = Anchor(root.transform, "Lighthouse entrance",
                new Vector3(bodyBounds.max.x + 2.5f, bodyBounds.min.y, bodyBounds.center.z));
            Transform pier = Anchor(root.transform, "Lighthouse ferry landing",
                new Vector3(pierBounds.center.x, pierBounds.max.y, pierBounds.center.z));
            Transform berth = Anchor(root.transform, "Lighthouse ferry berth",
                new Vector3(pierBounds.min.x, 0f, pierBounds.center.z));
            var groups = new Dictionary<Material, List<CombineInstance>>();
            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                Material[] materials = filter.GetComponent<Renderer>().sharedMaterials;
                for (int sub = 0; sub < filter.sharedMesh.subMeshCount; sub++)
                {
                    Material material = materials[Mathf.Min(sub, materials.Length - 1)];
                    if (!groups.TryGetValue(material, out List<CombineInstance> parts))
                        groups.Add(material, parts = new List<CombineInstance>());
                    parts.Add(new CombineInstance
                    {
                        mesh = filter.sharedMesh, subMeshIndex = sub,
                        transform = root.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix
                    });
                }
            }
            int index = 0;
            foreach (var group in groups)
            foreach (CombineInstance[] batch in Batches(group.Value))
            {
                var mesh = new Mesh { name = "Lighthouse-" + index, indexFormat = IndexFormat.UInt32 };
                mesh.CombineMeshes(batch, true, true);
                mesh.RecalculateBounds();
                string path = Generated + "/Lighthouse-" + index++ + ".asset";
                Mesh saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (saved == null) { AssetDatabase.CreateAsset(mesh, path); saved = mesh; }
                else
                {
                    EditorUtility.CopySerialized(mesh, saved);
                    EditorUtility.SetDirty(saved);
                    UnityEngine.Object.DestroyImmediate(mesh);
                }
                var part = new GameObject(group.Key.name + " " + (index - 1), typeof(MeshFilter), typeof(MeshRenderer));
                part.transform.SetParent(root.transform, false);
                part.GetComponent<MeshFilter>().sharedMesh = saved;
                part.GetComponent<MeshRenderer>().sharedMaterial = group.Key;
                // Hedges are scenery; the land, lighthouse and dock remain solid.
                if (group.Key.name.IndexOf("hedge", StringComparison.OrdinalIgnoreCase) < 0)
                    part.AddComponent<MeshCollider>().sharedMesh = saved;
            }
            UnityEngine.Object.DestroyImmediate(model);
            // Put interaction and disembarking points on the actual model surfaces.
            bool foundEntrance = false;
            float radius = Mathf.Max(bodyBounds.extents.x, bodyBounds.extents.z) + 2.5f;
            for (int i = 0; i < 16 && !foundEntrance; i++)
            {
                float angle = (180f + i * 22.5f) * Mathf.Deg2Rad;
                entrance.position = bodyBounds.center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                foundEntrance = SnapAnchorToSurface(root, entrance, bodyBounds.min.y + 5f, tower.transform);
            }
            if (!foundEntrance || !SnapAnchorToSurface(root, pier))
                throw new InvalidOperationException("Could not find safe lighthouse interaction and ferry landing surfaces.");
            // Rotated source bounds are conservative; centre the combined geometry itself.
            Bounds combined = BoundsOf(root);
            foreach (Transform part in root.transform)
                part.localPosition -= new Vector3(combined.center.x, 0f, combined.center.z);
            root.AddComponent<LighthouseStacker>().Configure(tower.transform, entrance, pier, berth);
            PrefabUtility.SaveAsPrefabAsset(root, IslandPath);
            // Remove only stale numbered outputs from previous builds of this generated model.
            string stale;
            while (AssetDatabase.LoadAssetAtPath<Mesh>(stale = Generated + "/Lighthouse-" + index++ + ".asset") != null)
                AssetDatabase.DeleteAsset(stale);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static Transform Anchor(Transform parent, string name, Vector3 position)
    {
        var anchor = new GameObject(name).transform;
        anchor.SetParent(parent, false);
        anchor.position = position;
        return anchor;
    }

    private static bool SnapAnchorToSurface(GameObject root, Transform anchor, float maxHeight = float.PositiveInfinity, Transform exclude = null)
    {
        Physics.SyncTransforms();
        var ray = new Ray(new Vector3(anchor.position.x, 100f, anchor.position.z), Vector3.down);
        float height = float.NegativeInfinity;
        foreach (Collider collider in root.GetComponentsInChildren<Collider>())
            if ((exclude == null || !collider.transform.IsChildOf(exclude)) &&
                collider.Raycast(ray, out RaycastHit hit, 200f) && hit.normal.y > 0.65f && hit.point.y <= maxHeight)
                height = Mathf.Max(height, hit.point.y);
        if (float.IsNegativeInfinity(height)) return false;
        anchor.position = new Vector3(anchor.position.x, height, anchor.position.z);
        return true;
    }

    private static void BuildStackPrefab(string path, Renderer[] parts, Vector3 size)
    {
        var root = new GameObject(System.IO.Path.GetFileNameWithoutExtension(path));
        try
        {
            var geometry = new GameObject("Supplied model geometry").transform;
            geometry.SetParent(root.transform, false);
            foreach (Renderer part in parts)
            {
                GameObject copy = UnityEngine.Object.Instantiate(part.gameObject, geometry, true);
                foreach (Collider collider in copy.GetComponentsInChildren<Collider>()) UnityEngine.Object.DestroyImmediate(collider);
            }
            Bounds bounds = BoundsOf(root);
            Vector3 scale = new Vector3(size.x / bounds.size.x, size.y / bounds.size.y, size.z / bounds.size.z);
            geometry.localScale = scale;
            geometry.localPosition = -Vector3.Scale(new Vector3(bounds.center.x, bounds.min.y, bounds.center.z), scale);
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    // Keep generated text assets comfortably below source-control file size limits.
    private static IEnumerable<CombineInstance[]> Batches(List<CombineInstance> parts)
    {
        var batch = new List<CombineInstance>();
        int vertices = 0;
        foreach (CombineInstance part in parts)
        {
            if (batch.Count > 0 && vertices + part.mesh.vertexCount > 200000)
            {
                yield return batch.ToArray();
                batch.Clear();
                vertices = 0;
            }
            batch.Add(part);
            vertices += part.mesh.vertexCount;
        }
        if (batch.Count > 0) yield return batch.ToArray();
    }

    private static GameObject Model(string path, Transform parent, string prefix)
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (asset == null) throw new InvalidOperationException("Missing model: " + path);
        GameObject model = UnityEngine.Object.Instantiate(asset, parent, false);
        model.name = asset.name;
        // FBX roots can carry an authored translation, including on the statue's mesh itself.
        // Bounds used for scaling and centring must be measured relative to the new root.
        model.transform.localPosition = Vector3.zero;
        var converted = new Dictionary<Material, Material>();
        foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                Material source = materials[i];
                if (!converted.TryGetValue(source, out Material material))
                {
                    string materialPath = Materials + "/" + prefix + "-" + source.name.Replace(':', '-') + ".mat";
                    material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                    if (material == null)
                    {
                        Color color = source.HasProperty("_BASE_COLOR") ? source.GetColor("_BASE_COLOR") : source.color;
                        material = PlaceholderMaterials.Lit(color);
                        material.name = prefix + " " + source.name;
                        material.SetFloat("_Smoothness", 0.15f);
                        // The sail is a single sheet and must show from either side of the boat.
                        material.SetFloat("_Cull", (float)CullMode.Off);
                        AssetDatabase.CreateAsset(material, materialPath);
                    }
                    converted.Add(source, material);
                }
                materials[i] = material;
            }
            renderer.sharedMaterials = materials;
        }
        return model;
    }

    private static void PlaceIsland()
    {
        GameObject existing = GameObject.Find(IslandName);
        if (existing != null) return;
        GameObject map = GameObject.Find("IslanddemoV3");
        Renderer ground = map.GetComponentsInChildren<Renderer>().First(r => r.name == OpeningVoyage.GroundName);
        Renderer ocean = map.GetComponentsInChildren<Renderer>().First(r => r.name == OpeningVoyage.OceanName);
        GameObject island = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(IslandPath));
        Undo.RegisterCreatedObjectUndo(island, "Add lighthouse island");
        island.name = IslandName;
        island.transform.position = new Vector3(ground.bounds.max.x + 85f, ocean.bounds.max.y,
            ground.bounds.center.z + 75f);
    }

    private static Bounds BoundsOf(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        Bounds bounds = renderers[0].bounds;
        foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
        return bounds;
    }

    private static void Folder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        Folder(path.Substring(0, slash));
        AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }
}
