using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class ScenePavingTests
{
    private const string Folder = "Assets/ScenePavingTests";

    private readonly List<Object> created = new List<Object>();
    private Transform group;
    private MeshFilter ground;
    private Material pavement;

    [SetUp]
    public void SetUp()
    {
        pavement = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        created.Add(pavement);
        // A 40 m ground block with its top at y = 0.
        GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.transform.position = new Vector3(0f, -0.5f, 0f);
        block.transform.localScale = new Vector3(40f, 1f, 40f);
        created.Add(block);
        ground = block.GetComponent<MeshFilter>();
        var root = new GameObject("Paving Group");
        root.transform.position = new Vector3(3f, 0f, -2f);
        created.Add(root);
        group = root.transform;
    }

    [TearDown]
    public void TearDown()
    {
        foreach (Object item in created)
        {
            if (item != null) Object.DestroyImmediate(item);
        }
        created.Clear();
        AssetDatabase.DeleteAsset(Folder);
    }

    [Test]
    public void SlabsGetTheSidewalkMaterialMetreUVsAndASpillDownToTheGround()
    {
        // Top at y = 0.7, 0.7 m above the ground.
        Transform slab = AddSlab("Slab", new Vector3(0f, 0.2f, 0f));
        Assert.IsTrue(ScenePaving.Pave(group, pavement, new[] { ground }, Folder));

        Mesh paved = slab.GetComponent<MeshFilter>().sharedMesh;
        Assert.AreEqual(Folder + "/Slab Paving.asset", AssetDatabase.GetAssetPath(paved));
        Assert.AreEqual(pavement, slab.GetComponent<MeshRenderer>().sharedMaterial);
        Vector3[] vertices = paved.vertices;
        Vector3[] normals = paved.normals;
        Vector2[] uvs = paved.uv;
        int tops = 0;
        for (int i = 0; i < vertices.Length; i++)
        {
            if (normals[i].y < 0.9f) continue;
            // The top's UVs are its X/Z in metres, like the imported sidewalk's.
            Vector3 p = group.InverseTransformPoint(slab.TransformPoint(vertices[i]));
            Assert.AreEqual(p.x, uvs[i].x, 1e-4f);
            Assert.AreEqual(p.z, uvs[i].y, 1e-4f);
            tops++;
        }
        Assert.Greater(tops, 0);

        Transform spill = group.Find("Sidewalk Spill (Slab)");
        Assert.IsNotNull(spill, "The slab spills gravel like the sidewalk.");
        Assert.AreEqual(pavement, spill.GetComponent<MeshRenderer>().sharedMaterial);
        Mesh gravel = spill.GetComponent<MeshFilter>().sharedMesh;
        Assert.AreEqual(Folder + "/Sidewalk Spill (Slab).asset", AssetDatabase.GetAssetPath(gravel));
        Assert.AreEqual(gravel, spill.GetComponent<MeshCollider>().sharedMesh);
        var bounds = new Bounds(spill.TransformPoint(gravel.vertices[0]), Vector3.zero);
        foreach (Vector3 v in gravel.vertices) bounds.Encapsulate(spill.TransformPoint(v));
        Assert.AreEqual(0.7f, bounds.max.y, 0.01f, "The slope starts at the slab's top.");
        Assert.Less(bounds.min.y, 0.1f, "The slope runs down to the ground.");
        Assert.Less(bounds.min.x, 1f, "The gravel spreads past the slab's edges.");
        Assert.Greater(bounds.max.x, 5f, "The gravel spreads past the slab's edges.");
        foreach (Vector3 normal in gravel.normals) Assert.Greater(normal.y, 0f);

        // Paving again rebuilds the same assets in place and leaves the scene alone.
        Assert.IsFalse(ScenePaving.Pave(group, pavement, new[] { ground }, Folder));
        Assert.AreEqual(paved, slab.GetComponent<MeshFilter>().sharedMesh);
        Assert.AreEqual(gravel, spill.GetComponent<MeshFilter>().sharedMesh);
    }

    [Test]
    public void RemovedSlabsLoseTheirSpillAndGeneratedMeshes()
    {
        AddSlab("First", new Vector3(0f, 0.2f, 0f));
        Transform second = AddSlab("Second", new Vector3(12f, 0.2f, 0f));
        ScenePaving.Pave(group, pavement, new[] { ground }, Folder);
        Assert.IsNotNull(group.Find("Sidewalk Spill (Second)"));

        Object.DestroyImmediate(second.gameObject);
        Assert.IsTrue(ScenePaving.Pave(group, pavement, new[] { ground }, Folder));
        Assert.IsNull(group.Find("Sidewalk Spill (Second)"));
        Assert.IsNotNull(group.Find("Sidewalk Spill (First)"));
        Assert.IsNull(AssetDatabase.LoadAssetAtPath<Mesh>(Folder + "/Second Paving.asset"));
        Assert.IsNull(AssetDatabase.LoadAssetAtPath<Mesh>(Folder + "/Sidewalk Spill (Second).asset"));
        Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Mesh>(Folder + "/First Paving.asset"));
    }

    // A 4 x 1 x 10 m box in the group.
    private Transform AddSlab(string name, Vector3 position)
    {
        GameObject slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
        slab.name = name;
        slab.transform.SetParent(group, false);
        slab.transform.localPosition = position;
        slab.transform.localScale = new Vector3(4f, 1f, 10f);
        return slab.transform;
    }
}
