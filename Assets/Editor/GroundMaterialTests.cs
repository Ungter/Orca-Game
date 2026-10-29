using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class GroundMaterialTests
{
    [Test]
    public void GroundMaterialUsesRepeatingGrassTexture()
    {
        const string texturePath = "Assets/Materials/Grass Texture.png";
        Texture2D grass = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        Material ground = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/GroundMat.mat");
        Assert.IsNotNull(grass);
        Assert.IsNotNull(ground);
        Assert.AreEqual(FilterMode.Point, grass.filterMode);
        Assert.AreEqual(TextureWrapMode.Repeat, grass.wrapModeU);
        Assert.AreEqual(TextureWrapMode.Repeat, grass.wrapModeV);
        Assert.Greater(grass.mipmapCount, 1);
        Assert.AreEqual(grass, ground.GetTexture("_BaseMap"));
        Assert.AreEqual(grass, ground.GetTexture("_MainTex"));
        Assert.AreEqual(new Vector2(0.125f, 0.125f), ground.GetTextureScale("_BaseMap"));
        Assert.AreEqual(Color.white, ground.GetColor("_BaseColor"));
    }

    [Test]
    public void ImportedIslandGroundHasTiledUvs()
    {
        GameObject map = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/IslanddemoV3.fbx");
        Assert.IsNotNull(map);
        MeshFilter ground = null;
        foreach (MeshFilter filter in map.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.name == "IslandDemo:pCube1") { ground = filter; break; }
        }
        Assert.IsNotNull(ground);
        Vector2[] uvs = ground.sharedMesh.uv;
        Assert.AreEqual(ground.sharedMesh.vertexCount, uvs.Length);
        float minX = float.MaxValue, maxX = float.MinValue;
        foreach (Vector2 uv in uvs)
        {
            minX = Mathf.Min(minX, uv.x);
            maxX = Mathf.Max(maxX, uv.x);
        }
        Assert.Greater(maxX - minX, 8f, "Ground UVs should use scene metres, not a single stretched tile.");
    }
}
