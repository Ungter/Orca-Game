using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public class OceanHorizonTests
{
    private const string MaterialPath = "Assets/Materials/OceanWaterMat.mat";
    private GameObject ocean;
    private GameObject far;

    [TearDown]
    public void TearDown()
    {
        if (far != null) Object.DestroyImmediate(far);
        if (ocean != null) Object.DestroyImmediate(ocean);
    }

    [Test]
    public void TheWaterFadesIntoTheSkyWellBeforeTheFarPlaneButNeverUpClose()
    {
        Material water = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        Assert.IsNotNull(water);
        float start = water.GetFloat("_HorizonFadeStart");
        float end = water.GetFloat("_HorizonFadeEnd");
        Assert.Less(start, end);
        Assert.Less(end, 1f, "Fully sky before the far plane cuts the water off.");
        Assert.GreaterOrEqual(water.GetFloat("_HorizonFadeMinDistance"), 100f, "The water round the island stays water.");
    }

    [Test]
    public void AFarOceanCarriesTheWaterOnUnderTheOceansEdges()
    {
        Material water = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        ocean = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ocean.name = OceanHorizon.OceanName;
        ocean.transform.position = new Vector3(10f, 2f, -5f);
        ocean.transform.localScale = new Vector3(5f, 1f, 5f);
        Renderer sea = ocean.GetComponent<Renderer>();
        sea.sharedMaterial = water;
        Bounds bounds = sea.bounds;

        far = OceanHorizon.Lay(sea);
        Assert.IsNotNull(far);
        Assert.IsNotNull(far.GetComponent<FarOcean>());
        Assert.AreEqual(ocean.scene, far.scene);
        MeshRenderer renderer = far.GetComponent<MeshRenderer>();
        Assert.AreEqual(water, renderer.sharedMaterial, "The same water, so its tiling runs straight on.");
        Assert.AreEqual(ShadowCastingMode.Off, renderer.shadowCastingMode);
        Assert.IsNull(far.GetComponent<Collider>(), "Nothing to walk on or bump into out there.");
        Assert.AreEqual(bounds.center.x, far.transform.position.x, 1e-3f);
        Assert.AreEqual(bounds.center.z, far.transform.position.z, 1e-3f);
        Assert.AreEqual(bounds.max.y - OceanHorizon.FarOceanDrop, far.transform.position.y, 1e-4f, "Just under the ocean.");
        Assert.AreEqual(OceanHorizon.FarOceanSize, renderer.bounds.size.x, 1f);
        Assert.AreEqual(OceanHorizon.FarOceanSize, renderer.bounds.size.z, 1f);
        Assert.AreEqual(far, OceanHorizon.Lay(sea), "Laying it again keeps the one.");
    }

    [Test]
    public void AMovedFarOceanPutsItselfBackUnderTheOcean()
    {
        ocean = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ocean.name = OceanHorizon.OceanName;
        ocean.transform.position = new Vector3(-3f, 1f, 4f);
        far = OceanHorizon.Lay(ocean.GetComponent<Renderer>());
        far.transform.position += new Vector3(50f, 3f, 0f);
        Assert.IsTrue(far.GetComponent<FarOcean>().Refresh());
        Assert.AreEqual(-3f, far.transform.position.x, 1e-3f);
        Assert.AreEqual(1f - OceanHorizon.FarOceanDrop, far.transform.position.y, 1e-3f);
    }

    [Test]
    public void WithoutAnOceanNothingIsLaid()
    {
        Assert.IsNull(OceanHorizon.Lay(null));
    }

    [Test]
    public void TheWaterIsGivenTheSkysOwnGradient()
    {
        Shader shader = Shader.Find("Orca/Night Sky Skybox");
        Assert.IsNotNull(shader);
        var sky = new Material(shader);
        try
        {
            sky.SetColor("_HorizonColor", Color.red);
            sky.SetColor("_ZenithColor", Color.green);
            sky.SetColor("_GroundColor", Color.blue);
            sky.SetFloat("_HorizonSharpness", 3f);
            OceanHorizon.PassSkyColours(sky);
            Assert.AreEqual(Color.red, Shader.GetGlobalColor("_SkyHorizonColor"));
            Assert.AreEqual(Color.green, Shader.GetGlobalColor("_SkyZenithColor"));
            Assert.AreEqual(Color.blue, Shader.GetGlobalColor("_SkyGroundColor"));
            Assert.AreEqual(3f, Shader.GetGlobalFloat("_SkyHorizonSharpness"), 1e-4f);
        }
        finally
        {
            Object.DestroyImmediate(sky);
            OceanHorizon.PassSkyColours(RenderSettings.skybox);
        }
    }

    // Saved in the scene, so the endless sea also shows while editing.
    [Test]
    public void TheGameSceneHasItsFarOceanUnderTheOcean()
    {
        Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Additive);
        try
        {
            Renderer sea = OceanHorizon.FindOcean(scene);
            Assert.IsNotNull(sea);
            FarOcean farOcean = null;
            foreach (GameObject root in scene.GetRootGameObjects())
                if (farOcean == null) farOcean = root.GetComponentInChildren<FarOcean>(true);
            Assert.IsNotNull(farOcean);
            Assert.AreEqual(OceanHorizon.FarOceanName, farOcean.name);
            Assert.IsTrue(farOcean.Refresh());
            Renderer water = farOcean.GetComponent<Renderer>();
            Assert.AreEqual(sea.sharedMaterial, water.sharedMaterial);
            Assert.Greater(water.bounds.size.x, sea.bounds.size.x * 4f, "Reaching far past the ocean's edges.");
            Assert.AreEqual(sea.bounds.max.y - OceanHorizon.FarOceanDrop, farOcean.transform.position.y, 1e-3f);
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }
}
