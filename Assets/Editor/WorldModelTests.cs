using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public class WorldModelTests
{
    [TestCase("WorldModels/StartingBoat", "Assets/Models/gameboat.fbx")]
    [TestCase("WorldModels/DolphinCounter", "Assets/Sprites/DolphinStatue.fbx")]
    public void RuntimeModelsKeepTheSuppliedMeshesAndUseBuildSafeMaterials(string resource, string source)
    {
        GameObject prefab = Resources.Load<GameObject>(resource);
        Assert.IsNotNull(prefab);
        foreach (MeshFilter part in prefab.GetComponentsInChildren<MeshFilter>(true))
            Assert.AreEqual(source, AssetDatabase.GetAssetPath(part.sharedMesh));
        foreach (Renderer part in prefab.GetComponentsInChildren<Renderer>(true))
            foreach (Material material in part.sharedMaterials)
            {
                Assert.AreEqual("Universal Render Pipeline/Lit", material.shader.name);
                StringAssert.StartsWith("Assets/Materials/WorldModels/", AssetDatabase.GetAssetPath(material));
            }
    }

    [Test]
    public void StartingBoatKeepsItsLengthAndDeckHeightWithoutScalingTheSailFlat()
    {
        GameObject prefab = Resources.Load<GameObject>("WorldModels/StartingBoat");
        Assert.IsNotNull(prefab);
        Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>();
        Bounds bounds = BoundsOf(renderers);
        Assert.AreEqual(7f, bounds.size.z, 0.001f);
        Assert.That(bounds.size.y, Is.InRange(3.5f, 4f), "The mast should rise above the player.");
        Assert.AreEqual(0.4f, BoatFit.RidingBench(prefab).bounds.max.y, 0.001f);
        Assert.Less(Vector3.Dot(prefab.transform.GetChild(0).localRotation * Vector3.forward, Vector3.forward), -0.999f,
            "The imported model must face the opposite way without reversing the travel rig.");
        Assert.AreEqual("pCube4", BoatFit.RidingBench(prefab).name);
        Assert.AreEqual(7f, prefab.GetComponent<BoxCollider>().size.z, 0.001f);
        Assert.IsTrue(prefab.GetComponent<BoxCollider>().enabled);
        Assert.AreEqual(1, prefab.GetComponentsInChildren<Collider>(true).Length);
        foreach (Material material in renderers.First(r => r.name == "pPlane1").sharedMaterials)
            Assert.AreEqual((float)UnityEngine.Rendering.CullMode.Off, material.GetFloat("_Cull"));
    }

    [Test]
    public void TheScenePlayerKeepsItsOriginalSharedMaterials()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Additive);
        try
        {
            PlayerController player = scene.GetRootGameObjects().Select(r => r.GetComponent<PlayerController>()).Single(p => p != null);
            Assert.IsNull(player.GetComponent<CharacterController>().sharedMaterial);
            string material = AssetDatabase.GetAssetPath(player.GetComponent<SpriteRenderer>().sharedMaterial);
            Assert.AreEqual("9dfc825aed78fcd4ba02077103263b40", AssetDatabase.AssetPathToGUID(material));
        }
        finally { EditorSceneManager.CloseScene(scene, true); }
    }

    [Test]
    public void LighthouseKeepsAllSourceTrianglesButUsesFewRenderers()
    {
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/lighthouse island.fbx");
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WorldModelSetup.IslandPath);
        Assert.IsNotNull(source);
        Assert.IsNotNull(prefab);
        Assert.AreEqual(IndexCount(source), IndexCount(prefab));
        Assert.LessOrEqual(prefab.GetComponentsInChildren<Renderer>().Length, 20);
        foreach (MeshFilter part in prefab.GetComponentsInChildren<MeshFilter>())
        {
            Assert.LessOrEqual(part.sharedMesh.vertexCount, 200000);
            Assert.Less(new System.IO.FileInfo(AssetDatabase.GetAssetPath(part.sharedMesh)).Length, 50000000);
        }
        // Transform the source vertices, not the looser rotated renderer bounding boxes.
        var points = source.GetComponentsInChildren<MeshFilter>()
            .SelectMany(f => f.sharedMesh.vertices.Select(v => f.transform.TransformPoint(v))).ToArray();
        Bounds original = new Bounds(points[0], Vector3.zero);
        foreach (Vector3 point in points) original.Encapsulate(point);
        Bounds combined = BoundsOf(prefab.GetComponentsInChildren<Renderer>());
        Assert.Less(Vector3.Distance(original.size * 100f, combined.size), 0.001f);
        Assert.That(combined.center.x, Is.EqualTo(0f).Within(0.001f));
        Assert.That(combined.center.z, Is.EqualTo(0f).Within(0.001f));
        Assert.IsNotEmpty(prefab.GetComponentsInChildren<MeshCollider>());
    }

    [Test]
    public void LighthouseIslandIsOffshoreInsideTheOceanAndClearOfTheOpeningBoatRoute()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Additive);
        try
        {
            GameObject island = scene.GetRootGameObjects().Single(r => r.name == WorldModelSetup.IslandName);
            GameObject map = scene.GetRootGameObjects().Single(r => r.name == "IslanddemoV3");
            Renderer ocean = map.GetComponentsInChildren<Renderer>().Single(r => r.name == OpeningVoyage.OceanName);
            Renderer land = map.GetComponentsInChildren<Renderer>().Single(r => r.name == OpeningVoyage.GroundName);
            Renderer dock = map.GetComponentsInChildren<Renderer>().Single(r => r.name == OpeningVoyage.DockName);
            Bounds bounds = BoundsOf(island.GetComponentsInChildren<Renderer>());
            Assert.Greater(bounds.min.x, land.bounds.max.x + 20f);
            Assert.Greater(bounds.min.z, dock.bounds.max.z + 20f);
            Assert.Greater(bounds.min.x, ocean.bounds.min.x);
            Assert.Less(bounds.max.x, ocean.bounds.max.x);
            Assert.Greater(bounds.min.z, ocean.bounds.min.z);
            Assert.Less(bounds.max.z, ocean.bounds.max.z);
            Assert.AreEqual(ocean.bounds.max.y, island.transform.position.y, 0.001f);
        }
        finally { EditorSceneManager.CloseScene(scene, true); }
    }

    private static long IndexCount(GameObject root) => root.GetComponentsInChildren<MeshFilter>()
        .Sum(f => Enumerable.Range(0, f.sharedMesh.subMeshCount).Sum(sub => (long)f.sharedMesh.GetIndexCount(sub)));

    private static Bounds BoundsOf(Renderer[] renderers)
    {
        Bounds bounds = renderers[0].bounds;
        foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
        return bounds;
    }
}
