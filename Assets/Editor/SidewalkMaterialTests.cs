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

    private static MeshRenderer FindRenderer(GameObject map, string name)
    {
        foreach (MeshRenderer renderer in map.GetComponentsInChildren<MeshRenderer>(true))
            if (renderer.name == name) return renderer;
        return null;
    }
}
