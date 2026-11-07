using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Paves groups of hand-placed scene slabs (the sideWalkAlch cubes in SampleScene) like the
// imported sidewalk: the material of IslandDemo:sideWalk, metre UVs, and a Sidewalk Spill sloping
// from every top edge down to the ground. The map's own slabs get this when the model imports
// (IslandLampLights), but these live in the scene, so their meshes are baked into
// Assets/Generated/<group> and rebuilt whenever a slab in the group moves, is added or removed.
[InitializeOnLoad]
public static class ScenePaving
{
    private static readonly string[] GroupNames = { "sideWalkAlch" };
    // The paved slab whose material the groups copy (the model's IslandDemo:pCube2, renamed in SampleScene).
    private const string SourceName = "IslandDemo:sideWalk";
    private const string SpillPrefix = "Sidewalk Spill";
    private const string GeneratedFolder = "Assets/Generated";
    // Bump when the paving changes, so every group is rebuilt.
    private const int Version = 1;
    private const double PollInterval = 0.5;

    private static double nextPoll;

    static ScenePaving()
    {
        EditorApplication.update += Poll;
    }

    [MenuItem("Tools/Orca/Repave Scene Sidewalks")]
    private static void RepaveFromMenu()
    {
        foreach (Transform group in FindGroups())
        {
            Repave(group);
        }
    }

    // A few times a second in edit mode: rebuild any group whose slabs changed since it was
    // last paved this editor session (and each group once per session or scene load).
    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll)
        {
            return;
        }
        nextPoll = EditorApplication.timeSinceStartup + PollInterval;
        // Never in play mode, and not halfway through dragging a slab in the scene view.
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
            EditorApplication.isUpdating || GUIUtility.hotControl != 0)
        {
            return;
        }
        foreach (Transform group in FindGroups())
        {
            if (SessionState.GetString(Key(group), string.Empty) != Signature(group).ToString())
            {
                Repave(group);
            }
        }
    }

    private static void Repave(Transform group)
    {
        SessionState.SetString(Key(group), Signature(group).ToString());
        Scene scene = group.gameObject.scene;
        var renderers = new List<MeshRenderer>();
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            renderers.AddRange(root.GetComponentsInChildren<MeshRenderer>(true));
        }
        Material material = null;
        foreach (MeshRenderer renderer in renderers)
        {
            if (renderer.name == SourceName)
            {
                material = renderer.sharedMaterial;
                break;
            }
        }
        if (material == null)
        {
            Debug.LogWarning("[ScenePaving] No " + SourceName + " in " + scene.path + " to copy the sidewalk material from; " + group.name + " is not paved.");
            return;
        }

        // Gravel slopes onto the island's ground block, or onto any other paved slab below it.
        var grounds = new List<MeshFilter>();
        bool island = false;
        foreach (MeshRenderer renderer in renderers)
        {
            if (renderer.transform.IsChildOf(group) || renderer.name.StartsWith(SpillPrefix, StringComparison.Ordinal))
            {
                continue;
            }
            bool isGround = renderer.name == IslandLampLights.GroundName;
            if (!isGround && renderer.sharedMaterial != material)
            {
                continue;
            }
            if (renderer.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null)
            {
                grounds.Add(filter);
                island |= isGround;
            }
        }
        if (!island)
        {
            Debug.LogWarning("[ScenePaving] No " + IslandLampLights.GroundName + " ground in " + scene.path + " for " + group.name + "'s gravel to spill onto.");
        }
        if (Pave(group, material, grounds, GeneratedFolder + "/" + FileName(group.name)))
        {
            EditorSceneManager.MarkSceneDirty(scene);
        }
    }

    // Paves the group's slabs (its children with a mesh) with material in metre UVs, and spills
    // gravel from each active one down onto grounds or another slab. The generated meshes live in
    // folder and are rebuilt in place, so the scene's references to them stay valid. Returns
    // whether any scene object changed.
    internal static bool Pave(Transform group, Material material, IReadOnlyList<MeshFilter> grounds, string folder)
    {
        EnsureFolder(folder);
        List<MeshFilter> slabs = Slabs(group);
        // The group's frame scaled to metres, like the importer's map space.
        float scale = group.lossyScale.x;
        Matrix4x4 toMetres = Matrix4x4.Scale(Vector3.one * scale) * group.worldToLocalMatrix;
        var names = new List<string>();
        var used = new HashSet<string>();
        bool changed = false;
        foreach (MeshFilter slab in slabs)
        {
            string name = UniqueName(slab.name, names);
            Mesh paved = UnityEngine.Object.Instantiate(slab.sharedMesh);
            paved.name = name + " Paving";
            paved.uv = SidewalkSpill.MetreUVs(paved, toMetres * slab.transform.localToWorldMatrix);
            paved.RecalculateTangents();
            paved = Store(paved, folder, used);
            if (slab.sharedMesh != paved)
            {
                slab.sharedMesh = paved;
                changed = true;
            }
            changed |= Paint(slab.GetComponent<MeshRenderer>(), material);
        }

        var spills = new HashSet<Transform>();
        for (int i = 0; i < slabs.Count; i++)
        {
            MeshFilter slab = slabs[i];
            var below = new List<(Mesh, Matrix4x4)>();
            foreach (MeshFilter ground in grounds)
            {
                if (ground != null && ground.sharedMesh != null)
                {
                    below.Add((ground.sharedMesh, toMetres * ground.transform.localToWorldMatrix));
                }
            }
            foreach (MeshFilter other in slabs)
            {
                if (other != slab && other.gameObject.activeSelf)
                {
                    below.Add((other.sharedMesh, toMetres * other.transform.localToWorldMatrix));
                }
            }
            Mesh spill = SidewalkSpill.Build(slab.sharedMesh, toMetres * slab.transform.localToWorldMatrix, below, scale);
            if (spill == null)
            {
                continue;
            }
            string label = SpillPrefix + " (" + names[i] + ")";
            spill.name = label;
            spill = Store(spill, folder, used);
            Transform holder = FindChild(group, label);
            if (holder == null)
            {
                holder = new GameObject(label).transform;
                holder.SetParent(group, false);
                changed = true;
            }
            changed |= FitSpill(holder, spill, material, slab.gameObject);
            spills.Add(holder);
        }

        // Spills and meshes of slabs that are gone.
        for (int i = group.childCount - 1; i >= 0; i--)
        {
            Transform child = group.GetChild(i);
            if (child.name.StartsWith(SpillPrefix, StringComparison.Ordinal) && !spills.Contains(child))
            {
                UnityEngine.Object.DestroyImmediate(child.gameObject);
                changed = true;
            }
        }
        foreach (string guid in AssetDatabase.FindAssets("t:Mesh", new[] { folder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!used.Contains(path))
            {
                AssetDatabase.DeleteAsset(path);
            }
        }
        return changed;
    }

    // Its vertices are already in the group's frame, so it sits at the group's origin. It
    // shows and hides with its slab, and has a collider so the player walks up the slope.
    private static bool FitSpill(Transform holder, Mesh spill, Material material, GameObject slab)
    {
        bool changed = false;
        if (holder.localPosition != Vector3.zero || holder.localRotation != Quaternion.identity || holder.localScale != Vector3.one)
        {
            holder.localPosition = Vector3.zero;
            holder.localRotation = Quaternion.identity;
            holder.localScale = Vector3.one;
            changed = true;
        }
        if (holder.gameObject.layer != slab.layer)
        {
            holder.gameObject.layer = slab.layer;
            changed = true;
        }
        if (holder.gameObject.activeSelf != slab.activeSelf)
        {
            holder.gameObject.SetActive(slab.activeSelf);
            changed = true;
        }
        MeshFilter filter = Ensure<MeshFilter>(holder.gameObject, ref changed);
        if (filter.sharedMesh != spill)
        {
            filter.sharedMesh = spill;
            changed = true;
        }
        MeshRenderer renderer = Ensure<MeshRenderer>(holder.gameObject, ref changed);
        if (Paint(renderer, material))
        {
            changed = true;
        }
        MeshCollider collider = Ensure<MeshCollider>(holder.gameObject, ref changed);
        // The mesh may have been rebuilt in place; reassigning it recooks the collision.
        collider.sharedMesh = null;
        collider.sharedMesh = spill;
        return changed;
    }

    private static T Ensure<T>(GameObject target, ref bool changed) where T : Component
    {
        if (target.TryGetComponent(out T component))
        {
            return component;
        }
        changed = true;
        return target.AddComponent<T>();
    }

    // Every material slot gets the sidewalk material, as the importer does.
    private static bool Paint(Renderer renderer, Material material)
    {
        Material[] materials = renderer.sharedMaterials;
        bool painted = materials.Length > 0;
        foreach (Material current in materials)
        {
            painted &= current == material;
        }
        if (painted)
        {
            return false;
        }
        var paved = new Material[Mathf.Max(1, materials.Length)];
        for (int i = 0; i < paved.Length; i++) paved[i] = material;
        renderer.sharedMaterials = paved;
        return true;
    }

    // Saves the mesh as folder/<name>.asset, copying it over an existing asset there so
    // references to it survive a rebuild.
    private static Mesh Store(Mesh mesh, string folder, HashSet<string> used)
    {
        string path = folder + "/" + mesh.name + ".asset";
        used.Add(path);
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
        EditorUtility.CopySerialized(mesh, existing);
        UnityEngine.Object.DestroyImmediate(mesh);
        EditorUtility.SetDirty(existing);
        AssetDatabase.SaveAssetIfDirty(existing);
        return existing;
    }

    private static List<MeshFilter> Slabs(Transform group)
    {
        var slabs = new List<MeshFilter>();
        foreach (Transform child in group)
        {
            if (child.name.StartsWith(SpillPrefix, StringComparison.Ordinal))
            {
                continue;
            }
            if (child.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null && child.TryGetComponent(out MeshRenderer _))
            {
                slabs.Add(filter);
            }
        }
        return slabs;
    }

    // What the paving depends on: the group itself (a reloaded scene is a new object), its
    // frame, and each slab's name, visibility, pose and size.
    private static Hash128 Signature(Transform group)
    {
        var hash = new Hash128();
        hash.Append(Version);
        hash.Append(group.GetHashCode());
        Matrix4x4 frame = group.localToWorldMatrix;
        hash.Append(ref frame);
        foreach (MeshFilter slab in Slabs(group))
        {
            hash.Append(slab.name);
            hash.Append(slab.gameObject.activeSelf ? 1 : 0);
            Matrix4x4 pose = slab.transform.localToWorldMatrix;
            hash.Append(ref pose);
            Bounds bounds = slab.sharedMesh.bounds;
            hash.Append(ref bounds);
        }
        return hash;
    }

    private static string Key(Transform group)
    {
        return "OrcaGame.ScenePaving." + group.gameObject.scene.path + "/" + group.name;
    }

    private static IEnumerable<Transform> FindGroups()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded)
            {
                continue;
            }
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (Array.IndexOf(GroupNames, root.name) >= 0)
                {
                    yield return root.transform;
                }
            }
        }
    }

    private static Transform FindChild(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name) return child;
        }
        return null;
    }

    private static string UniqueName(string name, List<string> taken)
    {
        string baseName = FileName(name);
        string unique = baseName;
        for (int n = 2; taken.Contains(unique); n++) unique = baseName + " " + n;
        taken.Add(unique);
        return unique;
    }

    private static string FileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        return name;
    }

    private static void EnsureFolder(string folder)
    {
        string[] parts = folder.Split('/');
        string path = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = path + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(path, parts[i]);
            }
            path = next;
        }
    }
}
