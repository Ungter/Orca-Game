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
    }
}
