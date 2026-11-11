using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Lights the lighthouse island like the main island. Its lamps get the main island's glowing
// heads (LampHeadGlow) and a LampGlow light inside each, with the same shadow tricks for their
// poles. The lantern at the top of the tower glows with the same material, holds a warm light
// of the same colour (lightHouseBigLight), and sends out a warm yellow beam, a cone of light
// with a spot light inside it, that LighthouseBeam turns round the tower in play mode.
//
// The island is one prefab (WorldModelSetup) with every lamp head's lit panels merged into one
// mesh and every lamp's pole and frame into another, so the lamps are found as the separate
// clumps of the panel mesh. They are smaller than the main island's lamps, so each light takes
// the main island's lamp settings scaled by how much smaller its panels are: the range with
// the ratio and the intensity with its square, which lights the same pool under a smaller lamp.
//
// Everything is added to the island in the open scene (the prefab's tests count its renderers).
// Tools > Orca > Light Lighthouse Island can be run again: it replaces what it made before,
// finding the lantern wherever the tower now puts it, and keeps the beam's size and speed.
public static class LighthouseLights
{
    public const string BigLightName = "lightHouseBigLight";
    public const string BeamName = "Beam";
    private const string LampLightName = "Lamp Light";
    private const string PoleShadowName = "Pole Shadow";
    // The island's merged parts, which WorldModelSetup names after their material.
    private const string LampPanelsPrefix = "Lighthouse-maplamp_aiStandardSurface2";
    private const string LampFramesPrefix = "Lighthouse-maplamp_aiStandardSurface1";
    private const string LanternName = "pCylinder3";
    private const string LanternGlassMaterial = "Lighthouse-aiStandardSurface5";
    private const string MainMapName = "IslanddemoV3";
    private const string LampHeadMaterialPath = "Assets/Materials/LampHeadGlow.mat";
    private const string BeamMaterialPath = "Assets/Materials/LighthouseBeam.mat";
    private const string BeamShaderName = "Orca/Lighthouse Beam";
    private const string Folder = "Assets/Generated/LighthouseLights";
    private const string BeamMeshPath = Folder + "/Lighthouse Beam.asset";

    // The main island's lamps, should none be found to read them from.
    private const float MainIntensity = 500f, MainRange = 64f, MainPanelHeight = 2.5f;
    private static readonly Color MainColour = new Color(1f, 0.66f, 0.3f);

    // The lantern's own light: a warm glow on the gallery, the cap and the top of the tower,
    // gone well before the ground. No shadows: it sits inside the glass.
    private const float LanternIntensity = 40f, LanternRange = 14f;

    // The beam: from inside the lantern out over the sea, tipped a little below the horizon,
    // this wide (before it spreads out and diffuses at the far end).
    private const float BeamLength = 70f, BeamStartRadius = 1f, BeamEndRadius = 9f, BeamTilt = 3f;
    // The cone mesh is wider than the light, which fills BeamCore of it, so the light can thin
    // away softly inside it instead of being cut off at its side.
    private const float BeamCore = 0.5f;
    private const int BeamSides = 32;
    private const string BeamConeName = "Beam Cone";
    // The spot light's colour; the cone's is the material's.
    private static readonly Color BeamColour = new Color(1f, 0.72f, 0.32f);
    // Its light reaches across to the main island's shore.
    private const float BeamLightIntensity = 2500f, BeamLightRange = 140f;

    [MenuItem("Tools/Orca/Light Lighthouse Island")]
    public static void LightMenu()
    {
        Debug.Log(Apply());
    }

    public static string Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "[LighthouseLights] Not in play mode: nothing would be kept.";
        GameObject island = GameObject.Find(WorldModelSetup.IslandName);
        LighthouseStacker stacker = island != null ? island.GetComponent<LighthouseStacker>() : null;
        if (stacker == null) return "[LighthouseLights] No " + WorldModelSetup.IslandName + " in the open scene.";
        Material glow = AssetDatabase.LoadAssetAtPath<Material>(LampHeadMaterialPath);
        if (glow == null) return "[LighthouseLights] Missing " + LampHeadMaterialPath;

        MeshRenderer panels = null, frames = null;
        foreach (Transform part in island.transform)
        {
            if (part.name.StartsWith(LampPanelsPrefix, StringComparison.Ordinal)) part.TryGetComponent(out panels);
            else if (part.name.StartsWith(LampFramesPrefix, StringComparison.Ordinal)) part.TryGetComponent(out frames);
        }
        Transform tower = IslandScenery.LighthouseTower(stacker);
        Transform lantern = tower != null ? tower.Find(LanternName) : null;
        if (panels == null || frames == null || lantern == null) return "[LighthouseLights] The lighthouse island's lamps or lantern are missing.";

        Undo.SetCurrentGroupName("Light Lighthouse Island");
        int undo = Undo.GetCurrentGroup();
        MainLamp(out float mainIntensity, out float mainRange, out Color colour, out float mainPanel);

        // Lamps: glowing panels, a light in each head, poles that don't shade their own lamp.
        RemoveChildren(panels.transform, LampLightName);
        Undo.RecordObject(panels, "Light Lighthouse Island");
        var panelMaterials = new Material[panels.sharedMaterials.Length];
        for (int i = 0; i < panelMaterials.Length; i++) panelMaterials[i] = glow;
        panels.sharedMaterials = panelMaterials;
        panels.shadowCastingMode = ShadowCastingMode.Off;
        var lampNotes = new List<string>();
        foreach (Bounds head in Clumps(panels.GetComponent<MeshFilter>()))
        {
            float ratio = head.size.y / mainPanel;
            var light = new GameObject(LampLightName);
            Undo.RegisterCreatedObjectUndo(light, "Light Lighthouse Island");
            light.transform.SetParent(panels.transform, false);
            light.transform.position = head.center;
            light.AddComponent<Light>();
            light.AddComponent<UniversalAdditionalLightData>();
            var lamp = light.AddComponent<LampGlow>();
            var settings = new SerializedObject(lamp);
            settings.FindProperty("color").colorValue = colour;
            settings.FindProperty("intensity").floatValue = mainIntensity * ratio * ratio;
            settings.FindProperty("range").floatValue = mainRange * ratio;
            settings.ApplyModifiedPropertiesWithoutUndo();
            lampNotes.Add(Mathf.RoundToInt(mainIntensity * ratio * ratio) + "/" + Mathf.RoundToInt(mainRange * ratio) + "m");
        }

        RemoveChildren(frames.transform, PoleShadowName);
        Undo.RecordObject(frames, "Light Lighthouse Island");
        frames.shadowCastingMode = ShadowCastingMode.Off;
        var caster = new GameObject(PoleShadowName);
        Undo.RegisterCreatedObjectUndo(caster, "Light Lighthouse Island");
        caster.transform.SetParent(frames.transform, false);
        caster.AddComponent<MeshFilter>().sharedMesh = frames.GetComponent<MeshFilter>().sharedMesh;
        var shadow = caster.AddComponent<MeshRenderer>();
        shadow.sharedMaterials = frames.sharedMaterials;
        shadow.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
        shadow.receiveShadows = false;
        shadow.renderingLayerMask = 1u << LampGlow.PoleShadowLayer;

        // The lantern: its glass glows like the lamp heads.
        var lanternRenderer = lantern.GetComponent<MeshRenderer>();
        Mesh lanternMesh = lantern.GetComponent<MeshFilter>().sharedMesh;
        Material[] lanternMaterials = lanternRenderer.sharedMaterials;
        Bounds glass = default;
        bool foundGlass = false;
        for (int i = 0; i < lanternMaterials.Length && i < lanternMesh.subMeshCount; i++)
        {
            if (lanternMaterials[i] == null || (lanternMaterials[i] != glow && lanternMaterials[i].name != LanternGlassMaterial)) continue;
            Bounds part = WorldBounds(lanternMesh, i, lantern.localToWorldMatrix);
            if (foundGlass) glass.Encapsulate(part);
            else glass = part;
            foundGlass = true;
            lanternMaterials[i] = glow;
        }
        if (!foundGlass) return "[LighthouseLights] No " + LanternGlassMaterial + " glass in the lantern.";
        Undo.RecordObject(lanternRenderer, "Light Lighthouse Island");
        lanternRenderer.sharedMaterials = lanternMaterials;
        // The glass runs on down inside the tower: only the part between the gallery's top and
        // the cap shows, and the light sits in the middle of that.
        float lower = glass.min.y, upper = glass.max.y;
        foreach (Transform part in tower)
        {
            if (part != lantern && part.TryGetComponent(out Renderer other) && other.bounds.max.y < glass.max.y) lower = Mathf.Max(lower, other.bounds.max.y);
        }
        foreach (Transform part in tower)
        {
            if (part != lantern && part.TryGetComponent(out Renderer other) && other.bounds.min.y > lower) upper = Mathf.Min(upper, other.bounds.min.y);
        }
        glass.SetMinMax(new Vector3(glass.min.x, lower, glass.min.z), new Vector3(glass.max.x, upper, glass.max.z));

        // lightHouseBigLight: the lantern's light, and the beam turning round it. The beam keeps
        // the size (the cone's scale) and turning speed it was given in the scene.
        Vector3 beamScale = Vector3.one;
        float beamSpeed = -1f;
        Transform previous = tower.Find(BigLightName);
        if (previous != null)
        {
            Transform oldCone = previous.Find(BeamName + "/" + BeamConeName);
            if (oldCone != null) beamScale = oldCone.localScale;
            LighthouseBeam oldBeam = previous.GetComponentInChildren<LighthouseBeam>(true);
            if (oldBeam != null) beamSpeed = oldBeam.DegreesPerSecond;
        }
        RemoveChildren(tower, BigLightName);
        var bigLight = new GameObject(BigLightName);
        Undo.RegisterCreatedObjectUndo(bigLight, "Light Lighthouse Island");
        bigLight.transform.SetParent(tower, false);
        bigLight.transform.SetPositionAndRotation(glass.center, Quaternion.identity);
        var lanternLight = bigLight.AddComponent<Light>();
        lanternLight.type = LightType.Point;
        lanternLight.color = colour;
        lanternLight.intensity = LanternIntensity;
        lanternLight.range = LanternRange;
        lanternLight.shadows = LightShadows.None;

        var pivot = new GameObject(BeamName);
        pivot.transform.SetParent(bigLight.transform, false);
        // At rest it points west, back over the water towards the main island: the ocean ends
        // close by to the east, and the island (beam and all) has to stay over the ocean mesh
        // (WorldModelTests). In play mode it turns all the way round.
        pivot.transform.rotation = Quaternion.Euler(0f, -90f, 0f);
        var turner = pivot.AddComponent<LighthouseBeam>();
        if (beamSpeed >= 0f)
        {
            var speed = new SerializedObject(turner);
            speed.FindProperty("degreesPerSecond").floatValue = beamSpeed;
            speed.ApplyModifiedPropertiesWithoutUndo();
        }

        var cone = new GameObject(BeamConeName);
        cone.transform.SetParent(pivot.transform, false);
        cone.transform.localRotation = Quaternion.Euler(BeamTilt, 0f, 0f);
        cone.transform.localScale = beamScale;
        cone.AddComponent<MeshFilter>().sharedMesh = BeamMesh();
        var coneRenderer = cone.AddComponent<MeshRenderer>();
        coneRenderer.sharedMaterial = BeamMaterial();
        coneRenderer.shadowCastingMode = ShadowCastingMode.Off;
        coneRenderer.receiveShadows = false;
        coneRenderer.lightProbeUsage = LightProbeUsage.Off;
        coneRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        coneRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

        var sweep = new GameObject("Beam Light");
        sweep.transform.SetParent(cone.transform, false);
        var beamLight = sweep.AddComponent<Light>();
        beamLight.type = LightType.Spot;
        beamLight.color = BeamColour;
        beamLight.spotAngle = 2f * Mathf.Atan(BeamEndRadius / BeamLength) * Mathf.Rad2Deg + 1f;
        beamLight.innerSpotAngle = beamLight.spotAngle * 0.4f;
        // A light's reach ignores its parent's scale, so it is stretched to the beam's length by
        // hand: the same light at the same share of the way along.
        beamLight.intensity = BeamLightIntensity * beamScale.z * beamScale.z;
        beamLight.range = BeamLightRange * beamScale.z;
        beamLight.shadows = LightShadows.None;

        Undo.CollapseUndoOperations(undo);
        EditorSceneManager.MarkSceneDirty(island.scene);
        return "[LighthouseLights] " + lampNotes.Count + " lamps lit (intensity/range " + string.Join(", ", lampNotes) + ", from the main island's " +
            mainIntensity + "/" + mainRange + "m); lantern glass glowing at " + glass.center.ToString("F1") + ", " + BigLightName + " and its beam added.";
    }

    // The main island's lamp settings (the most common, if they differ) and the height of a
    // lamp head's lit panels.
    private static void MainLamp(out float intensity, out float range, out Color colour, out float panel)
    {
        intensity = MainIntensity;
        range = MainRange;
        colour = MainColour;
        panel = MainPanelHeight;
        GameObject map = GameObject.Find(MainMapName);
        if (map == null) return;
        var intensities = new List<float>();
        var ranges = new List<float>();
        var panels = new List<float>();
        foreach (LampGlow lamp in map.GetComponentsInChildren<LampGlow>(true))
        {
            var settings = new SerializedObject(lamp);
            intensities.Add(settings.FindProperty("intensity").floatValue);
            ranges.Add(settings.FindProperty("range").floatValue);
            colour = settings.FindProperty("color").colorValue;
            Transform head = lamp.transform.parent;
            if (head == null || !head.TryGetComponent(out MeshRenderer renderer) || !head.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null) continue;
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length && i < filter.sharedMesh.subMeshCount; i++)
            {
                if (materials[i] != null && materials[i].name.StartsWith("LampHeadGlow", StringComparison.Ordinal))
                {
                    panels.Add(WorldBounds(filter.sharedMesh, i, head.localToWorldMatrix).size.y);
                    break;
                }
            }
        }
        if (intensities.Count == 0) return;
        intensity = Median(intensities);
        range = Median(ranges);
        if (panels.Count > 0) panel = Median(panels);
    }

    private static float Median(List<float> values)
    {
        values.Sort();
        return values[values.Count / 2];
    }

    // Bounds in the world of one submesh's vertices.
    private static Bounds WorldBounds(Mesh mesh, int submesh, Matrix4x4 toWorld)
    {
        Vector3[] vertices = mesh.vertices;
        int[] indices = mesh.GetIndices(submesh);
        var bounds = new Bounds(toWorld.MultiplyPoint3x4(vertices[indices[0]]), Vector3.zero);
        foreach (int index in indices) bounds.Encapsulate(toWorld.MultiplyPoint3x4(vertices[index]));
        return bounds;
    }

    // Each separate clump of the mesh's vertices seen from above (one per lamp head), as
    // bounds in the world. Vertices within about a metre of each other are one clump.
    private static List<Bounds> Clumps(MeshFilter filter)
    {
        Matrix4x4 toWorld = filter.transform.localToWorldMatrix;
        var cells = new Dictionary<Vector2Int, List<Vector3>>();
        foreach (Vector3 vertex in filter.sharedMesh.vertices)
        {
            Vector3 at = toWorld.MultiplyPoint3x4(vertex);
            var cell = new Vector2Int(Mathf.FloorToInt(at.x), Mathf.FloorToInt(at.z));
            if (!cells.TryGetValue(cell, out List<Vector3> list)) cells[cell] = list = new List<Vector3>();
            list.Add(at);
        }
        var clumps = new List<Bounds>();
        var seen = new HashSet<Vector2Int>();
        var queue = new Queue<Vector2Int>();
        foreach (Vector2Int start in cells.Keys)
        {
            if (!seen.Add(start)) continue;
            queue.Enqueue(start);
            bool any = false;
            Bounds bounds = default;
            while (queue.Count > 0)
            {
                Vector2Int cell = queue.Dequeue();
                foreach (Vector3 at in cells[cell])
                {
                    if (any) bounds.Encapsulate(at);
                    else bounds = new Bounds(at, Vector3.zero);
                    any = true;
                }
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        var next = new Vector2Int(cell.x + dx, cell.y + dz);
                        if (cells.ContainsKey(next) && seen.Add(next)) queue.Enqueue(next);
                    }
                }
            }
            clumps.Add(bounds);
        }
        return clumps;
    }

    private static void RemoveChildren(Transform parent, string name)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            Transform child = parent.GetChild(i);
            if (child.name == name) Undo.DestroyObjectImmediate(child.gameObject);
        }
    }

    // A closed cone along +z from the lantern, which the shader fills with light: its two rims
    // and a cap on each end, as the shader works out the rest from the cone's size. Closed, so
    // a ray leaving through either end still has a far side to be drawn on.
    private static Mesh BeamMesh()
    {
        var positions = new List<Vector3>();
        var triangles = new List<int>();
        for (int side = 0; side < BeamSides; side++)
        {
            float around = side * Mathf.PI * 2f / BeamSides;
            var outward = new Vector3(Mathf.Cos(around), Mathf.Sin(around), 0f);
            positions.Add(outward * (BeamStartRadius / BeamCore));
            positions.Add(outward * (BeamEndRadius / BeamCore) + Vector3.forward * BeamLength);
        }
        int start = positions.Count, end = start + 1;
        positions.Add(Vector3.zero);
        positions.Add(Vector3.forward * BeamLength);
        for (int side = 0; side < BeamSides; side++)
        {
            // Everything faces out, so the shader can draw the far side by culling the front.
            int a = side * 2, c = a + 1, b = (a + 2) % (BeamSides * 2), d = b + 1;
            triangles.AddRange(new[] { a, b, c, b, d, c });
            triangles.AddRange(new[] { start, b, a });
            triangles.AddRange(new[] { end, c, d });
        }
        var mesh = new Mesh { name = "Lighthouse Beam" };
        mesh.SetVertices(positions);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();

        if (AssetDatabase.LoadAssetAtPath<Mesh>(BeamMeshPath) != null) AssetDatabase.DeleteAsset(BeamMeshPath);
        Directory.CreateDirectory(Folder);
        AssetDatabase.Refresh();
        AssetDatabase.CreateAsset(mesh, BeamMeshPath);
        return mesh;
    }

    // Made once; tune the beam's look in the asset. Its size always follows the mesh's.
    private static Material BeamMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(BeamMaterialPath);
        if (material == null)
        {
            material = new Material(Shader.Find(BeamShaderName)) { name = "LighthouseBeam" };
            AssetDatabase.CreateAsset(material, BeamMaterialPath);
        }
        material.SetFloat("_Length", BeamLength);
        material.SetFloat("_StartRadius", BeamStartRadius / BeamCore);
        material.SetFloat("_EndRadius", BeamEndRadius / BeamCore);
        material.SetFloat("_Core", BeamCore);
        EditorUtility.SetDirty(material);
        return material;
    }
}
