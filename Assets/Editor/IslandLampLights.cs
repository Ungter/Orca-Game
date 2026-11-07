using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Puts a LampGlow light in the head (pCube1) of every lamp, lamp1, lamp2... in the island
// map when it imports, so the lights live in the model and follow it into any scene.
public class IslandLampLights : AssetPostprocessor
{
    private const string MapPath = "Assets/IslanddemoV3.fbx";
    private static readonly Regex LampName = new Regex(@"^lamp\d*$");

    // Emissive material for the lamp heads. The light sits inside the head, so its outer
    // faces never catch that light and would stay dark; the emission (HDR, picked up by
    // bloom) makes the head itself read as the bright source. Tune the glow in the asset.
    private const string LampHeadMaterialPath = "Assets/Materials/LampHeadGlow.mat";

    public override uint GetVersion()
    {
        // Bump when the lamp setup or LampGlow defaults change, so the map reimports.
        // Editing the sidewalk pixel art changes the hash, which reimports it too.
        return 23u ^ (SidewalkGravelArt.ContentHash() << 8);
    }

    // The slab gets a matte copy of this template, using its supplied texture when set.
    // UVs are laid out in scene metres, so the template's tiling reads as tiles per metre
    // (0.5 = one tile every 2 m). Hand-drawn gravel is a fallback for untextured templates.
    // SampleScene renames it IslandDemo:sideWalk.
    private const string SidewalkName = "IslandDemo:pCube2";
    // Loose slabs paved like the sidewalk. They share its material and metre UVs and spill
    // gravel over their edges the same way.
    private static readonly string[] ExtraSidewalkNames = { "pCube86" };
    // The island's ground block, which the spilled gravel slopes down onto.
    internal const string GroundName = "IslandDemo:pCube1";
    // Bumps on the gravel: normal map strength from the art's height field. The parallax
    // depth comes from the template's Height Map scale (_Parallax).
    private const float GravelNormalStrength = 5f;
    private const string SidewalkMaterialPath = "Assets/Materials/SidewalkGravel.mat";
    // The map is placed at this scale in SampleScene; converts model units to metres.
    private const float MapSceneScale = 209.3f;

    // The plaza fountain: one mesh holding the basin, the column and the spout pipe on top.
    private const string FountainName = "IslandDemo:pCylinder3";
    // Lit templates for the fountain's droplets (alpha-clipped) and pool; FountainWater
    // lays its pixel art over them.
    private const string FountainSprayMaterialPath = "Assets/Materials/FountainSpray.mat";
    private const string FountainPoolMaterialPath = "Assets/Materials/FountainPool.mat";

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

        // First, while every material on the map is still one this import made.
        MakeImportedMaterialsOpaque(root);

        context.DependsOnSourceAsset(LampHeadMaterialPath);
        Material headGlow = AssetDatabase.LoadAssetAtPath<Material>(LampHeadMaterialPath);
        if (headGlow == null)
        {
            Debug.LogWarning("[IslandLampLights] Missing " + LampHeadMaterialPath);
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
                // The pole sits right under the light and would throw a disc of
                // shadow on the ground around every lamp.
                if (head.name.EndsWith("pCylinder1", StringComparison.Ordinal))
                {
                    foreach (MeshRenderer pole in head.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        if (AddPoleShadow(pole))
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
                // LampGlow sets the light's shadow layers on this.
                glow.AddComponent<UniversalAdditionalLightData>();
                glow.AddComponent<LampGlow>();
                MeshRenderer shell = head.GetComponent<MeshRenderer>();
                if (headGlow != null && shell != null)
                {
                    GlowLampHead(lamp, shell, headGlow);
                }
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
        FixInvertedNormals(root);
        FixInsideOutMeshes(root);
        if (!ApplyGroundUVs(root))
        {
            Debug.LogWarning("[IslandLampLights] No " + GroundName + " ground found in " + MapPath);
        }
        if (!AddFountainWater(root))
        {
            Debug.LogWarning("[IslandLampLights] No " + FountainName + " fountain found in " + MapPath);
        }
        if (!ApplySidewalkGravel(root))
        {
            Debug.LogWarning("[IslandLampLights] No " + SidewalkName + " sidewalk found in " + MapPath);
        }
    }

    // The head's own material slots (the lit panels) become the glow; slots sharing a
    // material with the lamp's other parts (the frame, same as the pole) stay as they are.
    // A head with no slot of its own glows whole.
    private static void GlowLampHead(Transform lamp, MeshRenderer head, Material glow)
    {
        var frame = new HashSet<Material>();
        foreach (MeshRenderer part in lamp.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (part != head)
            {
                frame.UnionWith(part.sharedMaterials);
            }
        }
        Material[] materials = head.sharedMaterials;
        bool own = false;
        for (int i = 0; i < materials.Length; i++) own |= !frame.Contains(materials[i]);
        for (int i = 0; i < materials.Length; i++)
        {
            if (!own || !frame.Contains(materials[i]))
            {
                materials[i] = glow;
            }
        }
        head.sharedMaterials = materials;
    }

    // Stops the pole's own renderer casting shadows and adds a shadows-only copy of it on
    // LampGlow.PoleShadowLayer. Lamp lights leave that layer out of their shadows, so no
    // pole shades the ground under its own lamp; the moon includes it, so poles still cast
    // full shadows there. The visible pole stays on the default layer so every light lights it.
    private static bool AddPoleShadow(MeshRenderer pole)
    {
        MeshFilter filter = pole.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null)
        {
            return false;
        }

        pole.shadowCastingMode = ShadowCastingMode.Off;
        var caster = new GameObject("Pole Shadow");
        caster.transform.SetParent(pole.transform, false);
        caster.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
        MeshRenderer renderer = caster.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = pole.sharedMaterials;
        renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
        renderer.receiveShadows = false;
        renderer.renderingLayerMask = 1u << LampGlow.PoleShadowLayer;
        return true;
    }

    // The fountain is a single mesh, so it's measured from its surface: walking in from the
    // outer edge, the top stays at rim height across the rim, drops to the floor at the
    // inner wall and climbs again at the column. A FountainWater goes on the spout tip (the
    // highest point), sized to the basin.
    private bool AddFountainWater(GameObject root)
    {
        MeshFilter fountain = null;
        foreach (MeshFilter candidate in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (candidate.name == FountainName && candidate.sharedMesh != null)
            {
                fountain = candidate;
                break;
            }
        }
        if (fountain == null)
        {
            return false;
        }

        Material sprayMaterial = LoadDependency(FountainSprayMaterialPath);
        Material poolMaterial = LoadDependency(FountainPoolMaterialPath);

        Mesh mesh = fountain.sharedMesh;
        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;
        Vector3 centre = mesh.bounds.center;
        float outer = 0f;
        foreach (Vector3 v in vertices)
        {
            outer = Mathf.Max(outer, new Vector2(v.x - centre.x, v.z - centre.z).magnitude);
        }

        // Surface height at Steps + 1 radii from the axis out to the edge.
        const int Steps = 400;
        var heights = new float[Steps + 1];
        for (int i = 0; i <= Steps; i++)
        {
            heights[i] = SurfaceHeight(vertices, triangles, centre, outer * i / Steps);
        }
        int edge = Steps * 99 / 100;
        float rim = heights[edge];
        float rimDip = (rim - mesh.bounds.min.y) * 0.1f;
        int wall = edge;
        while (wall > 0 && heights[wall] > rim - rimDip)
        {
            wall--;
        }
        float floor = float.MaxValue;
        for (int i = 0; i < wall; i++)
        {
            if (heights[i] > float.MinValue)
            {
                floor = Mathf.Min(floor, heights[i]);
            }
        }
        // Most of the way up to the rim, like a full basin.
        float water = Mathf.Lerp(floor, rim, 0.75f);
        int column = wall;
        while (column > 0 && heights[column] <= water)
        {
            column--;
        }
        if (column == 0 || column == wall)
        {
            Debug.LogWarning("[IslandLampLights] Couldn't find the basin and column of " + FountainName);
            return true;
        }
        float wallRadius = outer * wall / Steps;
        float columnRadius = outer * column / Steps;

        // The spout tip: middle of the pipe's top. The column sits a little off the basin's centre.
        float top = mesh.bounds.max.y;
        Vector3 tip = Vector3.zero;
        int tipCount = 0;
        foreach (Vector3 v in vertices)
        {
            if (v.y > top - mesh.bounds.size.y * 0.02f)
            {
                tip += v;
                tipCount++;
            }
        }
        tip /= tipCount;
        tip.y = top;

        var holder = new GameObject("Fountain Water");
        holder.transform.SetParent(fountain.transform, false);
        holder.transform.localPosition = tip;
        holder.AddComponent<FountainWater>().Configure(top - water, columnRadius, wallRadius, sprayMaterial, poolMaterial);
        return true;
    }

    // A project asset the map uses; editing or adding it reimports the map.
    private Material LoadDependency(string path)
    {
        context.DependsOnSourceAsset(path);
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            Debug.LogWarning("[IslandLampLights] Missing " + path);
        }
        return material;
    }

    // Height of the mesh's top surface straight above a point r from the centre: the highest
    // of eight directions round it, or float.MinValue where there's no surface.
    private static float SurfaceHeight(Vector3[] vertices, int[] triangles, Vector3 centre, float r)
    {
        float best = float.MinValue;
        for (int a = 0; a < 8; a++)
        {
            float angle = a * Mathf.PI / 4f;
            float x = centre.x + Mathf.Cos(angle) * r;
            float z = centre.z + Mathf.Sin(angle) * r;
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                Vector3 p0 = vertices[triangles[t]];
                Vector3 p1 = vertices[triangles[t + 1]];
                Vector3 p2 = vertices[triangles[t + 2]];
                // Barycentric weights of (x, z) in the triangle seen from above.
                float area = (p1.z - p2.z) * (p0.x - p2.x) + (p2.x - p1.x) * (p0.z - p2.z);
                if (Mathf.Abs(area) < 1e-12f)
                {
                    continue;
                }
                float w0 = ((p1.z - p2.z) * (x - p2.x) + (p2.x - p1.x) * (z - p2.z)) / area;
                float w1 = ((p2.z - p0.z) * (x - p2.x) + (p0.x - p2.x) * (z - p2.z)) / area;
                float w2 = 1f - w0 - w1;
                if (w0 >= 0f && w1 >= 0f && w2 >= 0f)
                {
                    best = Mathf.Max(best, w0 * p0.y + w1 * p1.y + w2 * p2.y);
                }
            }
        }
        return best;
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

    // Unity imports the map's one Maya material (openPBR_shader1: opacity 1, no transmission) as
    // URP Transparent, which doesn't write depth. Triangles then draw in mesh order, so on the
    // L-shaped dock the far sides showed through its top from some angles. Anything this import
    // made see-through while fully opaque (base colour alpha 1) is set back to Opaque.
    private static void MakeImportedMaterialsOpaque(GameObject root)
    {
        var seen = new HashSet<Material>();
        var fixedNames = new List<string>();
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            foreach (Material material in renderer.sharedMaterials)
            {
                if (material == null || !seen.Add(material)) continue;
                if (!material.HasProperty("_Surface") || material.GetFloat("_Surface") < 0.5f) continue;
                if (material.HasProperty("_BaseColor") && material.GetColor("_BaseColor").a < 0.999f) continue;
                MakeOpaque(material);
                fixedNames.Add(material.name);
            }
        }
        if (fixedNames.Count > 0)
        {
            Debug.Log("[IslandLampLights] Made imported materials opaque: " + string.Join(", ", fixedNames));
        }
    }

    // What URP's material inspector sets for Surface Type: Opaque.
    internal static void MakeOpaque(Material material)
    {
        material.SetFloat("_Surface", 0f);
        if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
        if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 1f);
        if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
        if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
        if (material.HasProperty("_SrcBlendAlpha")) material.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
        if (material.HasProperty("_DstBlendAlpha")) material.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.Zero);
        material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.DisableKeyword("_ALPHAMODULATE_ON");
        material.DisableKeyword("_ALPHABLEND_ON");
        material.SetOverrideTag("RenderType", "Opaque");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
        material.SetShaderPassEnabled("DepthOnly", true);
        material.SetShaderPassEnabled("DepthNormals", true);
        material.SetShaderPassEnabled("ShadowCaster", true);
    }

    // Maya leaves some faces with their normals pointing the opposite way to the face (the
    // sidewalk, IslandDemo:pCube2, has a mix of both). Those faces still draw, but lights see
    // their back, so they only get ambient light and render black at night. Each vertex
    // normal is turned to agree with the faces that use it; vertices that already agree stay.
    private static void FixInvertedNormals(GameObject root)
    {
        var seen = new HashSet<Mesh>();
        var fixedNames = new List<string>();
        foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            Mesh mesh = filter.sharedMesh;
            if (mesh == null || !seen.Add(mesh))
            {
                continue;
            }
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            if (normals == null || normals.Length != vertices.Length)
            {
                continue;
            }

            // Area-weighted face direction (from the winding) round each vertex.
            var faces = new Vector3[vertices.Length];
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                int[] triangles = mesh.GetTriangles(sub);
                for (int t = 0; t + 2 < triangles.Length; t += 3)
                {
                    int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                    Vector3 face = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                    faces[a] += face;
                    faces[b] += face;
                    faces[c] += face;
                }
            }

            Vector4[] tangents = mesh.tangents;
            bool hasTangents = tangents != null && tangents.Length == normals.Length;
            int flipped = 0;
            for (int i = 0; i < normals.Length; i++)
            {
                if (Vector3.Dot(normals[i], faces[i]) >= 0f)
                {
                    continue;
                }
                normals[i] = -normals[i];
                if (hasTangents)
                {
                    tangents[i] = new Vector4(-tangents[i].x, -tangents[i].y, -tangents[i].z, tangents[i].w);
                }
                flipped++;
            }
            if (flipped == 0)
            {
                continue;
            }
            mesh.normals = normals;
            if (hasTangents)
            {
                mesh.tangents = tangents;
            }
            fixedNames.Add(filter.name + " (" + flipped + "/" + normals.Length + ")");
        }
        if (fixedNames.Count > 0)
        {
            Debug.Log("[IslandLampLights] Flipped inverted normals on: " + string.Join(", ", fixedNames));
        }
    }

    // Closed meshes whose faces all point inward (negative enclosed volume), e.g. pCube86,
    // which Maya mirrors back with a negative scale. Unity flips culling for mirrored objects
    // and PhysX flips their collider's triangles, so in Unity the mesh is inside out: the
    // player walks in through faces that point away from them, then the faces pointing at
    // them box them in. Turn every face, normal and tangent frame outward.
    private static void FixInsideOutMeshes(GameObject root)
    {
        var seen = new HashSet<Mesh>();
        var fixedNames = new List<string>();
        foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            Mesh mesh = filter.sharedMesh;
            if (mesh == null || !seen.Add(mesh))
            {
                continue;
            }
            Vector3[] vertices = mesh.vertices;
            if (!IsClosedTriangleMesh(mesh, vertices) || SignedVolume(mesh, vertices) >= 0f)
            {
                continue;
            }

            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                int[] triangles = mesh.GetTriangles(sub);
                for (int t = 0; t + 2 < triangles.Length; t += 3)
                {
                    (triangles[t + 1], triangles[t + 2]) = (triangles[t + 2], triangles[t + 1]);
                }
                mesh.SetTriangles(triangles, sub, false);
            }
            Vector3[] normals = mesh.normals;
            if (normals != null && normals.Length == vertices.Length)
            {
                for (int i = 0; i < normals.Length; i++) normals[i] = -normals[i];
                mesh.normals = normals;
            }
            // The surface directions stay; only the side flips, so the bitangent sign does too.
            Vector4[] tangents = mesh.tangents;
            if (tangents != null && tangents.Length == vertices.Length)
            {
                for (int i = 0; i < tangents.Length; i++) tangents[i].w = -tangents[i].w;
                mesh.tangents = tangents;
            }
            fixedNames.Add(filter.name);
        }
        if (fixedNames.Count > 0)
        {
            Debug.Log("[IslandLampLights] Turned inside-out meshes outward: " + string.Join(", ", fixedNames));
        }
    }

    // Volume enclosed by the triangles; positive when they face outward (Unity winding).
    internal static float SignedVolume(Mesh mesh, Vector3[] vertices)
    {
        double volume = 0.0;
        for (int sub = 0; sub < mesh.subMeshCount; sub++)
        {
            int[] triangles = mesh.GetTriangles(sub);
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                Vector3 a = vertices[triangles[t]], b = vertices[triangles[t + 1]], c = vertices[triangles[t + 2]];
                volume += Vector3.Dot(a, Vector3.Cross(b, c));
            }
        }
        return (float)(volume / 6.0);
    }

    // Every edge, with split vertices welded by position, is shared with a face running it
    // the other way. Only then does the enclosed volume say which side is out.
    private static bool IsClosedTriangleMesh(Mesh mesh, Vector3[] vertices)
    {
        if (vertices.Length == 0)
        {
            return false;
        }
        var welded = new Dictionary<Vector3, int>();
        var ids = new int[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            if (!welded.TryGetValue(vertices[i], out ids[i]))
            {
                ids[i] = welded.Count;
                welded.Add(vertices[i], ids[i]);
            }
        }
        var edges = new HashSet<long>();
        for (int sub = 0; sub < mesh.subMeshCount; sub++)
        {
            if (mesh.GetTopology(sub) != MeshTopology.Triangles)
            {
                return false;
            }
            int[] triangles = mesh.GetTriangles(sub);
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                for (int k = 0; k < 3; k++)
                {
                    long from = ids[triangles[t + k]], to = ids[triangles[t + (k + 1) % 3]];
                    if (from != to) edges.Add((from << 32) | to);
                }
            }
        }
        if (edges.Count == 0)
        {
            return false;
        }
        foreach (long edge in edges)
        {
            if (!edges.Contains(((edge & 0xffffffffL) << 32) | (edge >> 32)))
            {
                return false;
            }
        }
        return true;
    }

    // The scene overrides this mesh's material with GroundMat. Give it metre-based UVs
    // so the grass pattern tiles consistently across the island instead of stretching.
    private static bool ApplyGroundUVs(GameObject root)
    {
        bool found = false;
        foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.name != GroundName || filter.sharedMesh == null) continue;
            BoxProjectUVs(root.transform, filter);
            found = true;
        }
        return found;
    }

    private bool ApplySidewalkGravel(GameObject root)
    {
        context.DependsOnSourceAsset(SidewalkMaterialPath);
        Material template = AssetDatabase.LoadAssetAtPath<Material>(SidewalkMaterialPath);
        if (template == null)
        {
            Debug.LogWarning("[IslandLampLights] Missing " + SidewalkMaterialPath);
            return true;
        }

        Material material = null;
        bool found = false;
        var slabs = new List<MeshFilter>();
        foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            bool sidewalk = renderer.name == SidewalkName;
            if (!sidewalk && Array.IndexOf(ExtraSidewalkNames, renderer.name) < 0)
            {
                continue;
            }
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
            {
                continue;
            }
            found |= sidewalk;
            BoxProjectUVs(root.transform, filter);

            if (material == null)
            {
                material = BuildGravelMaterial(template);
            }
            var materials = new Material[renderer.sharedMaterials.Length];
            for (int i = 0; i < materials.Length; i++) materials[i] = material;
            renderer.sharedMaterials = materials;
            slabs.Add(filter);
        }
        // Every paved slab spills gravel; another slab's top counts as ground below it, so a
        // spill runs onto a neighbouring slab instead of burying itself in its side.
        foreach (MeshFilter slab in slabs)
        {
            AddSidewalkSpill(root.transform, slab, slabs, material);
        }
        return found;
    }

    // Preserve a supplied pavement texture rather than replacing it with generated gravel.
    // Only the fallback gravel uses its matching generated normal and height maps.
    private Material BuildGravelMaterial(Material template)
    {
        var material = new Material(template) { name = "Sidewalk Gravel" };
        Texture pavement = template.HasProperty("_BaseMap") ? template.GetTexture("_BaseMap") : null;
        if (pavement == null && template.HasProperty("_MainTex")) pavement = template.GetTexture("_MainTex");
        if (pavement != null)
        {
            string texturePath = AssetDatabase.GetAssetPath(pavement);
            if (!string.IsNullOrEmpty(texturePath)) context.DependsOnSourceAsset(texturePath);
            material.name = "Sidewalk Cobblestone";
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", pavement);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", pavement);
            // Pebble bumps do not match the cobblestone artwork.
            if (material.HasProperty("_BumpMap")) material.SetTexture("_BumpMap", null);
            if (material.HasProperty("_ParallaxMap")) material.SetTexture("_ParallaxMap", null);
            material.DisableKeyword("_NORMALMAP");
            material.DisableKeyword("_PARALLAXMAP");
        }
        else
        {
            Texture2D gravel = SidewalkGravelArt.Build("Sidewalk Gravel");
            Texture2D bumps = SidewalkGravelArt.BuildNormalMap("Sidewalk Gravel Normal", GravelNormalStrength);
            Texture2D heights = SidewalkGravelArt.BuildHeightMap("Sidewalk Gravel Height");
            context.AddObjectToAsset("sidewalk_gravel_texture", gravel);
            context.AddObjectToAsset("sidewalk_gravel_normal", bumps);
            context.AddObjectToAsset("sidewalk_gravel_height", heights);
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", gravel);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", gravel);
            if (material.HasProperty("_BumpMap"))
            {
                material.SetTexture("_BumpMap", bumps);
                material.EnableKeyword("_NORMALMAP");
            }
            if (material.HasProperty("_ParallaxMap"))
            {
                material.SetTexture("_ParallaxMap", heights);
                material.EnableKeyword("_PARALLAXMAP");
            }
        }
        // Keep this identifier stable so existing model material references still resolve.
        context.AddObjectToAsset("sidewalk_gravel_material", material);
        return material;
    }

    // Gravel slopes from the slab's edges down to the ground (or another paved slab), as a
    // sibling object with its own collider so the player can walk up them instead of meeting
    // a wall. The main sidewalk's spill keeps the name "Sidewalk Spill" and its asset
    // identifier; the others are named after their slab.
    private void AddSidewalkSpill(Transform root, MeshFilter sidewalk, List<MeshFilter> slabs, Material material)
    {
        MeshFilter groundFilter = null;
        foreach (MeshFilter candidate in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (candidate.name == GroundName && candidate.sharedMesh != null)
            {
                groundFilter = candidate;
                break;
            }
        }
        if (groundFilter == null)
        {
            Debug.LogWarning("[IslandLampLights] No " + GroundName + " ground found for the sidewalk spill in " + MapPath);
            return;
        }

        Matrix4x4 toMetres = Matrix4x4.Scale(Vector3.one * MapSceneScale) * root.worldToLocalMatrix;
        var grounds = new List<(Mesh, Matrix4x4)>
        {
            (groundFilter.sharedMesh, toMetres * groundFilter.transform.localToWorldMatrix),
        };
        foreach (MeshFilter slab in slabs)
        {
            if (slab != sidewalk) grounds.Add((slab.sharedMesh, toMetres * slab.transform.localToWorldMatrix));
        }
        Mesh spill = SidewalkSpill.Build(sidewalk.sharedMesh, toMetres * sidewalk.transform.localToWorldMatrix,
            grounds, MapSceneScale);
        bool main = sidewalk.name == SidewalkName;
        if (spill == null)
        {
            Debug.LogWarning("[IslandLampLights] Sidewalk spill for " + sidewalk.name + " came out empty in " + MapPath);
            return;
        }
        string label = main ? "Sidewalk Spill" : "Sidewalk Spill (" + sidewalk.name + ")";
        spill.name = label;
        context.AddObjectToAsset(main ? "sidewalk_spill_mesh" : "sidewalk_spill_mesh_" + sidewalk.name, spill);

        var holder = new GameObject(label);
        holder.transform.SetParent(root, false);
        holder.AddComponent<MeshFilter>().sharedMesh = spill;
        holder.AddComponent<MeshRenderer>().sharedMaterial = material;
        holder.AddComponent<MeshCollider>().sharedMesh = spill;
    }

    // World-aligned UVs in scene metres (SidewalkSpill.MetreUVs), so the pebbles keep one size
    // everywhere and line up across separate faces.
    private static void BoxProjectUVs(Transform root, MeshFilter filter)
    {
        Mesh mesh = filter.sharedMesh;
        Matrix4x4 toMetres = Matrix4x4.Scale(Vector3.one * MapSceneScale) * root.worldToLocalMatrix
            * filter.transform.localToWorldMatrix;
        mesh.uv = SidewalkSpill.MetreUVs(mesh, toMetres);
        // The imported tangents followed the old UVs; the normal map needs them to follow these.
        mesh.RecalculateTangents();
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
