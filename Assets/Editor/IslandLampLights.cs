using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Puts a LampGlow light in the head (pCube1) of every lamp, lamp1, lamp2... in the island
// map when it imports, so the lights live in the model and follow it into any scene.
public class IslandLampLights : AssetPostprocessor
{
    private const string MapPath = "Assets/IslanddemoV3.fbx";
    private static readonly Regex LampName = new Regex(@"^lamp\d*$");

    // Fraction of each pole, measured down from its top, that casts no shadow. The
    // part right under the lamp would throw a huge disc of shade on the ground; the
    // rest of the pole still casts normal shadows from every light.
    private const float PoleShadowlessTop = 0.35f;

    public override uint GetVersion()
    {
        // Bump when the lamp setup or LampGlow defaults change, so the map reimports.
        return 9;
    }

    // Matte template for the foliage: spheres (leaf clumps) get its dark green, cylinders
    // (trunks and branches) get BarkColor. Each tree or hedge picks one of a few slightly
    // shifted shades, so neighbours don't look identical.
    private const string FoliageMaterialPath = "Assets/Materials/FoliageMatte.mat";
    private static readonly string[] FoliageGroups = { "bigtrees", "mediumtrees", "shrubs", "hedges" };
    private static readonly string[] TreeGroups = { "bigtrees", "mediumtrees" };
    private static readonly Color BarkColor = new Color(0.16f, 0.10f, 0.06f);
    private const int ShadeCount = 8;
    private const float HueJitter = 0.035f;
    private const float SaturationJitter = 0.12f;
    private const float ValueJitter = 0.18f;

    private void OnPostprocessModel(GameObject root)
    {
        if (!string.Equals(assetPath, MapPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        int added = 0;
        int poles = 0;
        foreach (Transform lamp in root.GetComponentsInChildren<Transform>(true))
        {
            if (!LampName.IsMatch(lamp.name))
            {
                continue;
            }
            foreach (Transform head in lamp)
            {
                // The pole sits right under the light and would throw a huge
                // circular shadow on the ground around every lamp.
                if (head.name.EndsWith("pCylinder1", StringComparison.Ordinal))
                {
                    foreach (MeshRenderer pole in head.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        if (AddTrimmedShadow(root.transform, pole))
                        {
                            poles++;
                        }
                    }
                    continue;
                }

                // Some lamps keep their Maya namespace, e.g. "maplamp:pCube1".
                if (!head.name.EndsWith("pCube1", StringComparison.Ordinal))
                {
                    continue;
                }
                var glow = new GameObject("Lamp Light");
                glow.transform.SetParent(head, false);
                MeshFilter mesh = head.GetComponent<MeshFilter>();
                if (mesh != null && mesh.sharedMesh != null)
                {
                    glow.transform.localPosition = mesh.sharedMesh.bounds.center;
                }
                glow.AddComponent<Light>();
                glow.AddComponent<LampGlow>();
                added++;
            }
        }
        if (added == 0)
        {
            Debug.LogWarning("[IslandLampLights] No lampN/pCube1 found in " + MapPath);
        }
        int leaves = ApplyFoliageMaterial(root);
        if (leaves == 0)
        {
            Debug.LogWarning("[IslandLampLights] No pSphere leaves found under bigtrees/mediumtrees/shrubs/hedges in " + MapPath);
        }
        if (poles == 0)
        {
            Debug.LogWarning("[IslandLampLights] No lampN/pCylinder1 pole found in " + MapPath);
        }
    }

    // Stops the pole's own renderer casting shadows and adds a shadows-only copy of
    // the pole with its top cut off, so only the lower part of the pole casts shade.
    private bool AddTrimmedShadow(Transform root, MeshRenderer pole)
    {
        MeshFilter filter = pole.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null)
        {
            return false;
        }

        Mesh source = filter.sharedMesh;
        Matrix4x4 toRoot = root.worldToLocalMatrix * pole.transform.localToWorldMatrix;
        Vector3[] vertices = source.vertices;
        var heights = new float[vertices.Length];
        float bottom = float.MaxValue;
        float top = float.MinValue;
        for (int i = 0; i < vertices.Length; i++)
        {
            heights[i] = toRoot.MultiplyPoint3x4(vertices[i]).y;
            bottom = Mathf.Min(bottom, heights[i]);
            top = Mathf.Max(top, heights[i]);
        }
        float cut = top - (top - bottom) * PoleShadowlessTop;

        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        Vector3[] sourceNormals = source.normals;
        bool hasNormals = sourceNormals != null && sourceNormals.Length == vertices.Length;
        var polygon = new List<int>(3);
        var clipped = new List<(Vector3 p, Vector3 n)>(4);
        for (int sub = 0; sub < source.subMeshCount; sub++)
        {
            int[] triangles = source.GetTriangles(sub);
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                // Sutherland-Hodgman clip of one triangle against "height <= cut".
                clipped.Clear();
                for (int k = 0; k < 3; k++)
                {
                    int a = triangles[t + k];
                    int b = triangles[t + (k + 1) % 3];
                    bool aIn = heights[a] <= cut;
                    bool bIn = heights[b] <= cut;
                    Vector3 na = hasNormals ? sourceNormals[a] : Vector3.up;
                    Vector3 nb = hasNormals ? sourceNormals[b] : Vector3.up;
                    if (aIn)
                    {
                        clipped.Add((vertices[a], na));
                    }
                    if (aIn != bIn)
                    {
                        float s = (cut - heights[a]) / (heights[b] - heights[a]);
                        clipped.Add((Vector3.Lerp(vertices[a], vertices[b], s), Vector3.Lerp(na, nb, s).normalized));
                    }
                }
                for (int k = 1; k + 1 < clipped.Count; k++)
                {
                    foreach (var corner in new[] { clipped[0], clipped[k], clipped[k + 1] })
                    {
                        positions.Add(corner.p);
                        normals.Add(corner.n);
                    }
                }
            }
        }

        pole.shadowCastingMode = ShadowCastingMode.Off;
        if (positions.Count == 0)
        {
            return true;
        }

        var trimmed = new Mesh { name = source.name + "_shadow" };
        trimmed.indexFormat = positions.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        trimmed.SetVertices(positions);
        trimmed.SetNormals(normals);
        var indices = new int[positions.Count];
        for (int i = 0; i < indices.Length; i++) indices[i] = i;
        trimmed.SetTriangles(indices, 0);
        trimmed.RecalculateBounds();
        context.AddObjectToAsset(pole.transform.parent.name + "/" + pole.name + "/shadow", trimmed);

        var caster = new GameObject("Pole Shadow");
        caster.transform.SetParent(pole.transform, false);
        caster.AddComponent<MeshFilter>().sharedMesh = trimmed;
        MeshRenderer renderer = caster.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = pole.sharedMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
        renderer.receiveShadows = false;
        return true;
    }

private int ApplyFoliageMaterial(GameObject root)
    {
        // Reimport the map whenever the foliage material changes.
        context.DependsOnSourceAsset(FoliageMaterialPath);
        Material template = AssetDatabase.LoadAssetAtPath<Material>(FoliageMaterialPath);
        if (template == null)
        {
            Debug.LogWarning("[IslandLampLights] Missing " + FoliageMaterialPath);
            return 0;
        }

        Color leafColor = template.HasProperty("_BaseColor") ? template.GetColor("_BaseColor") : template.color;
        Material[] leaves = BuildShades(template, leafColor, "Leaves");
        Material[] bark = BuildShades(template, BarkColor, "Bark");

        int count = 0;
        foreach (Transform group in root.GetComponentsInChildren<Transform>(true))
        {
            if (Array.IndexOf(FoliageGroups, group.name) < 0)
            {
                continue;
            }
            bool isTrees = Array.IndexOf(TreeGroups, group.name) >= 0;

            // Each direct child of a group is one tree, shrub or hedge.
            foreach (Transform plant in group)
            {
                int shade = (int)(StableHash(plant.name) % ShadeCount);
                MeshRenderer[] renderers = plant.GetComponentsInChildren<MeshRenderer>(true);
                for (int r = 0; r < renderers.Length; r++)
                {
                    MeshRenderer renderer = renderers[r];
                    // Namespaced in places, e.g. "maptreebig:pSphere19".
                    Material material = null;
                    if (renderer.name.IndexOf("pSphere", StringComparison.Ordinal) >= 0)
                    {
                        // Trees keep one tone per tree; in shrubs and hedges every sphere
                        // gets its own. Names repeat across plants, so the index is hashed too.
                        int leafShade = isTrees ? shade : (int)(StableHash(plant.name + "/" + r + "/" + renderer.name) % ShadeCount);
                        material = leaves[leafShade];
                    }
                    else if (isTrees && renderer.name.IndexOf("pCylinder", StringComparison.Ordinal) >= 0)
                    {
                        material = bark[shade];
                    }
                    if (material == null)
                    {
                        continue;
                    }
                    var materials = new Material[renderer.sharedMaterials.Length];
                    for (int i = 0; i < materials.Length; i++) materials[i] = material;
                    renderer.sharedMaterials = materials;
                    count++;
                }
            }
        }
        return count;
    }

    // ShadeCount matte copies of the template around a base colour, stored inside the model.
    private Material[] BuildShades(Material template, Color baseColor, string label)
    {
        Color.RGBToHSV(baseColor, out float h, out float s, out float v);
        var shades = new Material[ShadeCount];
        for (int i = 0; i < ShadeCount; i++)
        {
            // Deterministic offsets in [-1, 1] so reimports give the same shades.
            float a = Jitter(label, i, 1);
            float b = Jitter(label, i, 2);
            float c = Jitter(label, i, 3);
            Color color = Color.HSVToRGB(Mathf.Repeat(h + a * HueJitter, 1f),
                Mathf.Clamp01(s * (1f + b * SaturationJitter)),
                Mathf.Clamp01(v * (1f + c * ValueJitter)));

            var material = new Material(template) { name = label + " " + (i + 1) };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            context.AddObjectToAsset("foliage_" + label + "_" + i, material);
            shades[i] = material;
        }
        return shades;
    }

    private static float Jitter(string label, int index, int axis)
    {
        return (StableHash(label + "/" + index + "/" + axis) % 10001) / 5000f - 1f;
    }

    // FNV-1a: stable across sessions, unlike string.GetHashCode.
    private static uint StableHash(string text)
    {
        uint hash = 2166136261;
        foreach (char ch in text)
        {
            hash = (hash ^ ch) * 16777619;
        }
        return hash;
    }
}
