using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public class DoorBeaconTests
{
    private GameObject door;
    private GameObject player;
    private GameObject cachud;
    private DoorBeacon beacon;

    [SetUp]
    public void SetUp()
    {
        door = GameObject.CreatePrimitive(PrimitiveType.Cube);
        door.name = AlchemistEncounter.DoorName;
        door.transform.position = new Vector3(5f, 2.1f, -3f);
        door.transform.localScale = new Vector3(1f, 2.2f, 0.15f);
        // Far enough away that the beam is at full strength.
        player = new GameObject("Test player");
        player.transform.position = new Vector3(500f, 0f, 500f);
    }

    [TearDown]
    public void TearDown()
    {
        if (beacon != null) Object.DestroyImmediate(beacon.gameObject);
        if (cachud != null) Object.DestroyImmediate(cachud);
        if (player != null) Object.DestroyImmediate(player);
        if (door != null) Object.DestroyImmediate(door);
    }

    private DoorBeacon Install()
    {
        beacon = DoorBeacon.Install(door);
        typeof(DoorBeacon).GetField("player", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(beacon, player.transform);
        return beacon;
    }

    [Test]
    public void BeaconRisesFromTheFootOfTheDoorAndIsSizedFromIt()
    {
        Install();
        Assert.AreEqual(door.transform, beacon.Door);
        Assert.That(Vector3.Distance(new Vector3(5f, 1f, -3f), beacon.transform.position), Is.LessThan(1e-4f));
        Assert.AreEqual(1f * DoorBeacon.WidthPerDoorWidth, beacon.Width, 1e-4f);
        Assert.AreEqual(2.2f * DoorBeacon.HeightPerDoorHeight, beacon.Height, 1e-4f);

        MeshRenderer beam = beacon.GetComponent<MeshRenderer>();
        Assert.AreEqual(ShadowCastingMode.Off, beam.shadowCastingMode);
        Assert.IsFalse(beam.receiveShadows);
        Assert.AreEqual("Orca/Door Beacon", beam.sharedMaterial.shader.name);
        Assert.IsTrue(beam.sharedMaterial.shader.isSupported);
        Bounds bounds = beacon.GetComponent<MeshFilter>().sharedMesh.bounds;
        Assert.AreEqual(beacon.Height, bounds.size.y, 1e-4f);
        Assert.Greater(bounds.size.z, 0f, "It turns to the camera, so its bounds must cover more than its own plane.");
    }

    [Test]
    public void BeaconFadesInAndOutWithItsDoor()
    {
        Install();
        MeshRenderer beam = beacon.GetComponent<MeshRenderer>();
        Assert.IsFalse(beam.enabled, "It starts dark and fades in.");

        beacon.Tick(DoorBeacon.FadeTime * 0.5f);
        Assert.AreEqual(0.5f, beacon.Opacity, 1e-4f);
        Assert.IsTrue(beam.enabled);
        Assert.AreEqual(0.5f, beam.sharedMaterial.GetFloat("_Opacity"), 1e-4f);
        beacon.Tick(DoorBeacon.FadeTime);
        Assert.AreEqual(1f, beacon.Opacity, 1e-4f);

        door.SetActive(false);
        beacon.Tick(DoorBeacon.FadeTime * 0.5f);
        Assert.AreEqual(0.5f, beacon.Opacity, 1e-4f);
        beacon.Tick(DoorBeacon.FadeTime);
        Assert.AreEqual(0f, beacon.Opacity);
        Assert.IsFalse(beam.enabled, "A hidden beam isn't drawn at all.");
    }

    [Test]
    public void BeaconFadesAsThePlayerWalksUpToTheDoor()
    {
        Install();
        MeshRenderer beam = beacon.GetComponent<MeshRenderer>();
        Vector3 foot = beacon.transform.position;

        // Height doesn't count, only distance over the ground.
        player.transform.position = foot + new Vector3(DoorBeacon.FadeStartDistance, 5f, 0f);
        beacon.Tick(DoorBeacon.FadeTime);
        Assert.AreEqual(1f, beacon.Brightness, 1e-4f);

        float middle = (DoorBeacon.FadeStartDistance + DoorBeacon.FadeEndDistance) * 0.5f;
        player.transform.position = foot + new Vector3(0f, 0f, middle);
        beacon.Tick(0f);
        Assert.AreEqual(0.5f, beacon.Brightness, 1e-3f);
        Assert.AreEqual(0.5f, beam.sharedMaterial.GetFloat("_Opacity"), 1e-3f);

        player.transform.position = foot + new Vector3(DoorBeacon.FadeEndDistance - 0.5f, 0f, 0f);
        beacon.Tick(0f);
        Assert.AreEqual(0f, beacon.Brightness);
        Assert.IsFalse(beam.enabled, "Gone by the time the door's own prompt shows.");

        player.transform.position = foot + new Vector3(DoorBeacon.FadeStartDistance + 1f, 0f, 0f);
        beacon.Tick(0f);
        Assert.AreEqual(1f, beacon.Brightness, 1e-4f, "Back at full strength once the player walks away.");
    }

    [Test]
    public void CachudsBeaconGoesOutOnceHeHasComeOut()
    {
        door.name = CachudDoorway.DoorName;
        cachud = new GameObject("Cachud");
        CachudCallout callout = cachud.AddComponent<CachudCallout>();
        Install();
        beacon.Tick(DoorBeacon.FadeTime);
        Assert.AreEqual(1f, beacon.Opacity, 1e-4f, "Lit while his door can still be knocked on.");

        typeof(CachudCallout).GetProperty("HasComeOut").SetValue(callout, true);
        beacon.Tick(DoorBeacon.FadeTime);
        Assert.AreEqual(0f, beacon.Opacity);
    }

    // The scene renames the model's doors; the encounters must still find theirs.
    [Test]
    public void EveryBeaconDoorIsInTheSceneAndItsEncounterFindsIt()
    {
        Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Additive);
        try
        {
            Transform map = null;
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == CachudDoorway.MapName) map = root.transform;
            Assert.IsNotNull(map);
            foreach (string name in DoorBeacon.DoorNames)
            {
                Transform found = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (child.name == name) found = child;
                    }
                }
                Assert.IsNotNull(found, name);
                Assert.IsTrue(found.gameObject.activeInHierarchy, name + " must be active for GameObject.Find.");
                Renderer[] renderers = found.GetComponentsInChildren<Renderer>();
                Assert.IsNotEmpty(renderers, name);
                Assert.Greater(renderers[0].bounds.size.y, 0f, name);
            }
            Assert.AreEqual(CachudDoorway.DoorName, CachudDoorway.FindDoor(map).name);
            Assert.AreEqual(MagicianEncounter.DoorName, MagicianEncounter.FindDoor(map.Find("world/building1")).name);
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }
}
