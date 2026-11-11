using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Tools > Orca > Set Up Mouse Trap: puts the lighthouse's final game (LighthouseMouseTrap) into
// the open scene and makes what it needs:
// - "Lighthouse island/Mouse trap" with the game, wired to the stair up the lighthouse
//   (Lighthouse-aiStandardSurface6 12), a "Mouse trap entrance" at the stair's foot where the
//   [E] prompt shows, and the lantern (Supplied lighthouse/pCylinder3) the player looks out from;
// - its materials in Resources/MouseTrap (the seams, the cleared view, the hex beam);
// - readable copies of the big paved meshes on the main island a player build can't read back
//   (Assets/Generated/MouseTrap), so they can be cut into hexes too.
// Re-runnable: it replaces its own output.
public static class MouseTrapSetup
{
    public const string ResourcesFolder = "Assets/Resources/MouseTrap";
    public const string GeneratedFolder = "Assets/Generated/MouseTrap";
    public const string TriggerName = "Lighthouse-aiStandardSurface6 12";
    public const string LookoutPath = "Supplied lighthouse/pCylinder3";

    [MenuItem("Tools/Orca/Set Up Mouse Trap")]
    public static void SetUpOpenScene()
    {
        LighthouseMouseTrap trap = SetUp(SceneManager.GetActiveScene());
        if (trap == null)
        {
            Debug.LogError("[MouseTrapSetup] Couldn't find Lighthouse island, its stair or its lantern in the open scene.");
            return;
        }
        EditorSceneManager.MarkSceneDirty(trap.gameObject.scene);
        Selection.activeObject = trap.gameObject;
        Debug.Log("[MouseTrapSetup] Mouse trap set up on " + trap.name + ".");
    }

    public static LighthouseMouseTrap SetUp(Scene scene)
    {
        MakeMaterials();
        GameObject island = null;
        foreach (GameObject root in scene.GetRootGameObjects())
            if (root.name == "Lighthouse island") island = root;
        if (island == null) return null;
        Transform trigger = island.transform.Find(TriggerName);
        Transform lookout = island.transform.Find(LookoutPath);
        if (trigger == null || lookout == null) return null;

        Transform holder = island.transform.Find("Mouse trap");
        if (holder == null)
        {
            holder = new GameObject("Mouse trap").transform;
            holder.SetParent(island.transform, false);
        }
        Transform entrance = holder.Find("Mouse trap entrance");
        if (entrance == null)
        {
            entrance = new GameObject("Mouse trap entrance").transform;
            entrance.SetParent(holder, false);
        }
        entrance.position = StairFoot(trigger);
        LighthouseMouseTrap trap = holder.GetComponent<LighthouseMouseTrap>();
        if (trap == null) trap = holder.gameObject.AddComponent<LighthouseMouseTrap>();
        trap.Configure(trigger, entrance, lookout);
        BakeReadableCopies(scene, trap);
        EditorUtility.SetDirty(trap);
        return trap;
    }

    // The low end of the stair: the middle of its lowest points.
    private static Vector3 StairFoot(Transform trigger)
    {
        var filter = trigger.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null) return trigger.position;
        Vector3[] vertices = filter.sharedMesh.vertices;
        float low = float.MaxValue;
        var world = new Vector3[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            world[i] = trigger.TransformPoint(vertices[i]);
            low = Mathf.Min(low, world[i].y);
        }
        Vector3 sum = Vector3.zero;
        int count = 0;
        foreach (Vector3 p in world)
        {
            if (p.y > low + 1.5f) continue;
            sum += p;
            count++;
        }
        return count > 0 ? new Vector3(sum.x / count, low, sum.z / count) : trigger.position;
    }

    private static void MakeMaterials()
    {
        Directory.CreateDirectory(ResourcesFolder);
        MakeMaterial("MouseTrapSeam", "Orca/Mouse Trap Glow", m =>
        {
            m.SetColor("_Color", new Color(0.01f, 0.008f, 0.005f, 0.9f));
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        });
        MakeMaterial("SightCone", "Orca/Sight Cone", m => { });
        MakeMaterial("HexBeam", "Orca/Hex Beam", m => { });
        AssetDatabase.SaveAssets();
    }

    private static void MakeMaterial(string name, string shaderName, System.Action<Material> tune)
    {
        string path = ResourcesFolder + "/" + name + ".mat";
        Shader shader = Shader.Find(shaderName);
        if (shader == null)
        {
            Debug.LogError("[MouseTrapSetup] Missing shader " + shaderName);
            return;
        }
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = shader;
        tune(material);
        EditorUtility.SetDirty(material);
    }

    // Big meshes on the main island the game cuts into hexes but a build couldn't read: the
    // paved slabs (pCube86, Front Path, the plaza). Fence rails and the houses are never cut.
    private static void BakeReadableCopies(Scene scene, LighthouseMouseTrap trap)
    {
        GameObject ground = null;
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == MouseTrapIsland.GroundName) ground = t.gameObject;
        if (ground == null || !ground.TryGetComponent(out Renderer groundRenderer)) return;
        float width = Mathf.Sqrt(3f) * trap.HexRadius;
        Bounds area = groundRenderer.bounds;
        area.Expand(new Vector3(width * 4f, 400f, width * 4f));
        var houses = new HashSet<Transform>();
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == "building3") houses.Add(root.transform);
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == "world")
                    foreach (Transform child in t)
                        if (!child.name.StartsWith("fence", System.StringComparison.OrdinalIgnoreCase) &&
                            child.name != "Fence Picket" && child.name != "Picket Fence Rail" &&
                            child.name != "island_demo_map" && child.name != "ocean") houses.Add(child);
        }
        Renderer ocean = OceanHorizon.FindOcean(scene);
        Directory.CreateDirectory(GeneratedFolder);
        var originals = new List<Mesh>();
        var copies = new List<Mesh>();
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == "Lighthouse island") continue;
            foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r == ocean || !area.Intersects(r.bounds) || Mathf.Max(r.bounds.size.x, r.bounds.size.z) <= width) continue;
                if (!r.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null || filter.sharedMesh.isReadable) continue;
                if (UnderAny(r.transform, houses) || IsFence(r.transform) || r.GetComponentInParent<PlayerController>() != null) continue;
                Mesh source = filter.sharedMesh;
                if (originals.Contains(source)) continue;
                originals.Add(source);
                copies.Add(SaveCopy(source));
            }
        }
        trap.SetReadableCopies(originals, copies);
        AssetDatabase.SaveAssets();
    }

    private static bool UnderAny(Transform t, HashSet<Transform> roots)
    {
        for (; t != null; t = t.parent)
            if (roots.Contains(t)) return true;
        return false;
    }

    private static bool IsFence(Transform t)
    {
        for (; t != null; t = t.parent)
            if (t.name.StartsWith("fence", System.StringComparison.OrdinalIgnoreCase) || t.name == "Fence Picket" || t.name == "Picket Fence Rail")
                return true;
        return false;
    }

    private static Mesh SaveCopy(Mesh source)
    {
        string name = Safe(source.name) + " " + source.vertexCount + " (readable)";
        string path = GeneratedFolder + "/" + name + ".asset";
        var copy = new Mesh { name = name };
        copy.indexFormat = source.indexFormat;
        copy.vertices = source.vertices;
        if (source.normals.Length > 0) copy.normals = source.normals;
        if (source.tangents.Length > 0) copy.tangents = source.tangents;
        if (source.colors.Length > 0) copy.colors = source.colors;
        if (source.uv.Length > 0) copy.uv = source.uv;
        if (source.uv2.Length > 0) copy.uv2 = source.uv2;
        copy.subMeshCount = source.subMeshCount;
        for (int s = 0; s < source.subMeshCount; s++) copy.SetTriangles(source.GetTriangles(s), s);
        copy.RecalculateBounds();
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null)
        {
            EditorUtility.CopySerialized(copy, existing);
            Object.DestroyImmediate(copy);
            return existing;
        }
        AssetDatabase.CreateAsset(copy, path);
        return copy;
    }

    private static string Safe(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Replace(':', '_');
    }
}
