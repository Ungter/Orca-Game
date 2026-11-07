using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class SidewalkMaterialTests
{
    private const string TexturePath = "Assets/Materials/Cobblestone.png";

    [Test]
    public void SidewalkTemplateUsesRepeatingPixelFilteredCobblestone()
    {
        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        Material template = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/SidewalkGravel.mat");
        Assert.IsNotNull(texture);
        Assert.IsNotNull(template);
        Assert.AreEqual(FilterMode.Point, texture.filterMode);
        Assert.AreEqual(TextureWrapMode.Repeat, texture.wrapModeU);
        Assert.AreEqual(TextureWrapMode.Repeat, texture.wrapModeV);
        Assert.Greater(texture.mipmapCount, 1);
        Assert.AreEqual(texture, template.GetTexture("_BaseMap"));
        Assert.AreEqual(texture, template.GetTexture("_MainTex"));
        Assert.AreEqual(new Vector2(0.5f, 0.5f), template.GetTextureScale("_BaseMap"));
    }

    [Test]
    public void ImportedPathAndSpillKeepCobblestoneWithoutOldGravelBumps()
    {
        GameObject map = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/IslanddemoV3.fbx");
        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        Assert.IsNotNull(map);
        Assert.IsNotNull(texture);
        Material pavement = null;
        foreach (MeshRenderer renderer in map.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (renderer.name != "IslandDemo:pCube2") continue;
            foreach (Material material in renderer.sharedMaterials)
            {
                Assert.IsNotNull(material);
                Assert.AreEqual(texture, material.GetTexture("_BaseMap"));
                Assert.AreEqual(texture, material.GetTexture("_MainTex"));
                Assert.IsNull(material.GetTexture("_BumpMap"));
                Assert.IsNull(material.GetTexture("_ParallaxMap"));
                Assert.IsFalse(material.IsKeywordEnabled("_NORMALMAP"));
                Assert.IsFalse(material.IsKeywordEnabled("_PARALLAXMAP"));
                pavement = material;
            }
        }
        Assert.IsNotNull(pavement, "The imported sidewalk must use the cobblestone material.");
        Transform spill = map.transform.Find("Sidewalk Spill");
        Assert.IsNotNull(spill);
        Assert.AreEqual(pavement, spill.GetComponent<MeshRenderer>().sharedMaterial);

        MeshRenderer slab = FindRenderer(map, "pCube86");
        Assert.IsNotNull(slab);
        foreach (Material material in slab.sharedMaterials)
            Assert.AreEqual(pavement, material, "pCube86 is paved like the sidewalk.");
    }

    // Mirrored by a negative Y scale with its faces stored inward, it used to trap the player.
    [Test]
    public void MirroredSlabFacesOutwardSoItsColliderCannotTrapThePlayer()
    {
        GameObject map = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/IslanddemoV3.fbx");
        MeshRenderer slab = FindRenderer(map, "pCube86");
        Assert.IsNotNull(slab);
        Mesh mesh = slab.GetComponent<MeshFilter>().sharedMesh;
        // Positive volume means its faces point out, whatever sign its mirrored scale has.
        Assert.Greater(IslandLampLights.SignedVolume(mesh, mesh.vertices), 0f);
        Assert.AreEqual(mesh, slab.GetComponent<MeshCollider>().sharedMesh);
    }

    // Mirrored like the slab itself, so the spill must find its top through the flipped winding.
    [Test]
    public void LooseSlabSpillsGravelFromItsTopEdgesLikeTheSidewalk()
    {
        GameObject map = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/IslanddemoV3.fbx");
        MeshRenderer slab = FindRenderer(map, "pCube86");
        Assert.IsNotNull(slab);
        Transform spill = map.transform.Find("Sidewalk Spill (pCube86)");
        Assert.IsNotNull(spill, "pCube86 spills gravel like the sidewalk.");
        Assert.AreEqual(slab.sharedMaterial, spill.GetComponent<MeshRenderer>().sharedMaterial);
        Mesh gravel = spill.GetComponent<MeshFilter>().sharedMesh;
        Assert.AreEqual(gravel, spill.GetComponent<MeshCollider>().sharedMesh);

        Matrix4x4 toRoot = map.transform.worldToLocalMatrix * slab.transform.localToWorldMatrix;
        var slabBounds = new Bounds(toRoot.MultiplyPoint3x4(slab.GetComponent<MeshFilter>().sharedMesh.vertices[0]), Vector3.zero);
        foreach (Vector3 v in slab.GetComponent<MeshFilter>().sharedMesh.vertices)
            slabBounds.Encapsulate(toRoot.MultiplyPoint3x4(v));
        Matrix4x4 spillToRoot = map.transform.worldToLocalMatrix * spill.localToWorldMatrix;
        var spillBounds = new Bounds(spillToRoot.MultiplyPoint3x4(gravel.vertices[0]), Vector3.zero);
        foreach (Vector3 v in gravel.vertices)
            spillBounds.Encapsulate(spillToRoot.MultiplyPoint3x4(v));

        float tolerance = slabBounds.size.y * 0.05f + 1e-4f;
        Assert.AreEqual(slabBounds.max.y, spillBounds.max.y, tolerance, "The slope starts at the slab's top, not its underside.");
        Assert.Less(spillBounds.min.y, slabBounds.max.y - tolerance, "The slope runs down to the ground.");
        Assert.IsTrue(spillBounds.min.x < slabBounds.min.x || spillBounds.max.x > slabBounds.max.x ||
            spillBounds.min.z < slabBounds.min.z || spillBounds.max.z > slabBounds.max.z,
            "The gravel spreads out past the slab's edges.");
        foreach (Vector3 normal in gravel.normals)
            Assert.Greater(normal.y, 0f, "Gravel faces up so it can be seen and walked on.");
    }

    // Maya stored some of the sidewalk's faces with normals pointing down; lit from behind,
    // those parts rendered pure black at night.
    [Test]
    public void SidewalkNormalsFaceTheSameWayAsTheirFaces()
    {
        GameObject map = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/IslanddemoV3.fbx");
        MeshRenderer sidewalk = FindRenderer(map, "IslandDemo:pCube2");
        Assert.IsNotNull(sidewalk);
        Mesh mesh = sidewalk.GetComponent<MeshFilter>().sharedMesh;
        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;
        int[] triangles = mesh.triangles;
        for (int t = 0; t + 2 < triangles.Length; t += 3)
        {
            int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
            Vector3 face = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
            if (face.sqrMagnitude < 1e-12f) continue;
            Assert.Greater(Vector3.Dot(normals[a] + normals[b] + normals[c], face), 0f, "Triangle " + t / 3 + "'s normals face away from it.");
        }
    }

    // The map's Maya material imported as Transparent (no depth write), so the dock's far sides
    // drew over its top from some angles.
    [Test]
    public void DockMaterialIsOpaqueAndWritesDepth()
    {
        GameObject map = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/IslanddemoV3.fbx");
        MeshRenderer dock = FindRenderer(map, "IslandDemo:Dock");
        Assert.IsNotNull(dock);
        foreach (Material material in dock.sharedMaterials)
        {
            Assert.IsNotNull(material);
            Assert.AreEqual(0f, material.GetFloat("_Surface"), material.name + " is Opaque.");
            Assert.AreEqual(1f, material.GetFloat("_ZWrite"), material.name + " writes depth.");
            Assert.AreEqual((int)UnityEngine.Rendering.RenderQueue.Geometry, material.renderQueue);
            Assert.IsFalse(material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"));
            Assert.IsFalse(material.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON"));
            Assert.AreEqual("Opaque", material.GetTag("RenderType", false));
        }
    }

    private static MeshRenderer FindRenderer(GameObject map, string name)
    {
        foreach (MeshRenderer renderer in map.GetComponentsInChildren<MeshRenderer>(true))
            if (renderer.name == name) return renderer;
        return null;
    }
}
