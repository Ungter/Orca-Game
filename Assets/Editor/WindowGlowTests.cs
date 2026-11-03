using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public class WindowGlowTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private GameObject house;
    private GameObject window;
    private WindowGlow glow;
    private Material bars;
    private GameObject door;
    private MagicianEncounter encounter;

    [TearDown]
    public void TearDown()
    {
        if (glow != null) typeof(WindowGlow).GetMethod("OnDestroy", PrivateInstance).Invoke(glow, null);
        if (house != null) Object.DestroyImmediate(house);
        if (bars != null) Object.DestroyImmediate(bars);
        if (encounter != null) encounter.Cancel();
        if (door != null) Object.DestroyImmediate(door);
    }

    [Test]
    public void BothGlowWindowsOnTheMagiciansHouseGlow()
    {
        Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Additive);
        try
        {
            Transform building = null;
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == "IslanddemoV3") building = root.transform.Find("world/building1");
            Assert.IsNotNull(building);
            foreach (string name in new[] { "glowWindow1", "glowWindow2" })
            {
                Transform found = building.Find(name);
                Assert.IsNotNull(found, name);
                WindowGlow component = found.GetComponent<WindowGlow>();
                Assert.IsNotNull(component, name);
                var serialized = new SerializedObject(component);
                Assert.IsNotNull(serialized.FindProperty("glassMaterial").objectReferenceValue, name);
                Assert.IsNotNull(serialized.FindProperty("litBy").objectReferenceValue as MagicianEncounter,
                    name + " waits for the magician's door.");
                Material rays = (Material)serialized.FindProperty("rayMaterial").objectReferenceValue;
                Assert.IsNotNull(rays, name);
                Assert.AreEqual("Orca/Window Light Rays", rays.shader.name);
                Assert.IsTrue(rays.shader.isSupported);
            }
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    [TestCase(1f)]
    [TestCase(-1f)]
    public void RaysAndLightShineOutOfTheSideAwayFromTheHouse(float side)
    {
        BuildWindow(side);
        glow.Tick(10f);
        Vector3 outward = Vector3.forward * side;
        Assert.Greater(Vector3.Dot(glow.Outward, outward), 0.99f);
        Assert.AreEqual(5.35f * side, glow.FaceCentre.z, 0.01f);

        Assert.IsNotNull(glow.Glow);
        Assert.AreEqual(LightType.Spot, glow.Glow.type);
        Assert.AreEqual(LightShadows.None, glow.Glow.shadows);
        Assert.Greater(Vector3.Dot(glow.Glow.transform.forward, outward), 0.8f);
        Assert.Less(glow.Glow.transform.forward.y, 0f, "The light leans down onto the ground in front.");

        Assert.IsNotNull(glow.Rays);
        Mesh mesh = glow.Rays.GetComponent<MeshFilter>().sharedMesh;
        Assert.Greater(mesh.vertexCount, 0);
        Bounds bounds = glow.Rays.bounds;
        Assert.Greater(Vector3.Dot(bounds.center - glow.FaceCentre, outward), 0.5f, "The rays reach out of the glass.");
        Assert.IsNull(glow.Rays.transform.parent, "Not under the window's uneven scale or the house's bounds.");
    }

    [Test]
    public void OnlyTheGlassSlotGlowsAndTheOriginalMaterialsComeBack()
    {
        BuildWindow(1f);
        glow.Tick(10f);
        Material[] before = (Material[])typeof(WindowGlow).GetField("originalMaterials", PrivateInstance).GetValue(glow);
        Material[] lit = window.GetComponent<MeshRenderer>().sharedMaterials;
        Assert.AreNotEqual(before[0], lit[0]);
        Assert.IsTrue(lit[0].IsKeywordEnabled("_EMISSION"));
        Assert.Greater(lit[0].GetColor("_EmissionColor").maxColorComponent, 1f);
        Assert.AreEqual(before[1], lit[1]);

        typeof(WindowGlow).GetMethod("OnDisable", PrivateInstance).Invoke(glow, null);
        Assert.AreEqual(before[0], window.GetComponent<MeshRenderer>().sharedMaterials[0]);
        Assert.IsFalse(glow.Rays.gameObject.activeSelf);

        GameObject rays = glow.Rays.gameObject;
        typeof(WindowGlow).GetMethod("OnDestroy", PrivateInstance).Invoke(glow, null);
        Assert.IsTrue(rays == null, "The rays object goes with the window.");
        glow = null;
    }

    [Test]
    public void WindowStaysDarkUntilThePlayerHasBeenToTheDoorAndIsBackOutside()
    {
        door = new GameObject("Magician door test");
        encounter = door.AddComponent<MagicianEncounter>();
        BuildWindow(1f, encounter);
        Material[] unlit = window.GetComponent<MeshRenderer>().sharedMaterials;
        glow.Tick(10f);
        Assert.IsFalse(glow.SwitchedOn);
        Assert.AreEqual(0f, glow.Level);
        Assert.IsFalse(glow.Rays.gameObject.activeSelf);
        Assert.AreEqual(unlit[0], window.GetComponent<MeshRenderer>().sharedMaterials[0]);

        encounter.Begin();
        glow.Tick(10f);
        Assert.IsFalse(glow.SwitchedOn, "Not while the room covers the world.");

        encounter.Cancel();
        glow.Tick(0.01f);
        Assert.IsTrue(glow.SwitchedOn);
        glow.Tick(10f);
        Assert.AreEqual(1f, glow.Level);
        Assert.IsTrue(glow.Rays.gameObject.activeSelf);
        Assert.Greater(glow.Glow.intensity, 0f);
        Assert.IsTrue(window.GetComponent<MeshRenderer>().sharedMaterials[0].IsKeywordEnabled("_EMISSION"));
    }

    [Test]
    public void LightingUpFadesInGradually()
    {
        BuildWindow(1f);
        float full = new SerializedObject(glow).FindProperty("lightIntensity").floatValue;
        glow.Tick(0.75f);
        Assert.AreEqual(0.5f, glow.Level, 0.01f);
        Assert.That(glow.Glow.intensity, Is.InRange(0.01f, full * 0.6f));
        float half = window.GetComponent<MeshRenderer>().sharedMaterials[0].GetColor("_EmissionColor").maxColorComponent;
        glow.Tick(10f);
        Assert.AreEqual(1f, glow.Level);
        Assert.Greater(glow.Glow.intensity, full * 0.85f);
        Assert.Greater(window.GetComponent<MeshRenderer>().sharedMaterials[0].GetColor("_EmissionColor").maxColorComponent, half);
    }

    // A 10 m house with a 2.5 x 5 x 0.7 window box set into the wall on the given side.
    private void BuildWindow(float side, MagicianEncounter litBy = null)
    {
        house = new GameObject("House");
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.transform.SetParent(house.transform, false);
        body.transform.localScale = Vector3.one * 10f;
        window = GameObject.CreatePrimitive(PrimitiveType.Cube);
        window.name = "glowWindow test";
        window.transform.SetParent(house.transform, false);
        window.transform.localPosition = new Vector3(1f, 2f, 5f * side);
        window.transform.localScale = new Vector3(2.5f, 5f, 0.7f);
        MeshRenderer renderer = window.GetComponent<MeshRenderer>();
        Material plain = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
        bars = new Material(plain) { name = "Bars" };
        renderer.sharedMaterials = new[] { plain, bars };

        glow = window.AddComponent<WindowGlow>();
        var serialized = new SerializedObject(glow);
        serialized.FindProperty("glassMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/WindowGlow.mat");
        serialized.FindProperty("rayMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/WindowLightRays.mat");
        serialized.FindProperty("litBy").objectReferenceValue = litBy;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        typeof(WindowGlow).GetMethod("Start", PrivateInstance).Invoke(glow, null);
    }
}
