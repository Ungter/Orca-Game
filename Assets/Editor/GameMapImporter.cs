using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class GameMapImporter
{
    const string MapPath = "Assets/gameprodmap.fbx";
    const string ScenePath = "Assets/Scenes/SampleScene.unity";
    const string MapName = "gameprodmap";
    const string DoneKey = "OrcaGame.GameMapImported";

    static GameMapImporter()
    {
        if (EditorPrefs.GetBool(DoneKey, false)) return;
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
            if (Run()) EditorPrefs.SetBool(DoneKey, true);
        };
    }

    [MenuItem("Tools/Orca/Import Game Map Into SampleScene")]
    public static void RunFromMenu() => Run();

    static bool Run()
    {
        var importer = AssetImporter.GetAtPath(MapPath) as ModelImporter;
        if (importer == null)
        {
            Debug.LogWarning($"[GameMapImporter] {MapPath} not found or not a model.");
            return false;
        }

        if (!importer.addCollider)
        {
            importer.addCollider = true;
            importer.SaveAndReimport();
        }

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MapPath);
        if (prefab == null) return false;

        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        GameObject map = null;
        foreach (var root in scene.GetRootGameObjects())
            if (root.name == MapName) { map = root; break; }

        if (map == null)
        {
            map = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            map.name = MapName;
            map.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Undo.RegisterCreatedObjectUndo(map, "Import Game Map");
        }

        int added = 0;
        foreach (var filter in map.GetComponentsInChildren<MeshFilter>(true))
        {
            var go = filter.gameObject;
            GameObjectUtility.SetStaticEditorFlags(go,
                StaticEditorFlags.BatchingStatic | StaticEditorFlags.NavigationStatic |
                StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic |
                StaticEditorFlags.ContributeGI | StaticEditorFlags.ReflectionProbeStatic);

            if (filter.sharedMesh == null) continue;
            var col = go.GetComponent<MeshCollider>();
            if (col == null) { col = go.AddComponent<MeshCollider>(); added++; }
            col.sharedMesh = filter.sharedMesh;
            col.convex = false;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = map;
        Debug.Log($"[GameMapImporter] '{MapName}' placed in {ScenePath}; {added} MeshCollider(s) added.");
        return true;
    }
}
