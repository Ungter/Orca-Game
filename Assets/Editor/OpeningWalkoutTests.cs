using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public class OpeningWalkoutTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private Scene scene;
    private Transform map;
    private PlayerController player;
    private OpeningWalkout opening;

    [SetUp]
    public void SetUp()
    {
        scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Additive);
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == "IslanddemoV3") map = root.transform;
            if (root.TryGetComponent(out PlayerController found)) player = found;
        }
        Assert.IsNotNull(map);
        Assert.IsNotNull(player);
        Call(player, "Awake");
        Physics.SyncTransforms();
    }

    [TearDown]
    public void TearDown()
    {
        opening?.Dispose();
        EditorSceneManager.CloseScene(scene, true);
    }

    [Test]
    public void WalkStartsInsideTheRequestedDoorAndEndsOnAClearGroundedPath()
    {
        Assert.IsTrue(OpeningWalkout.TryCreate(map, player, out opening));
        CharacterController controller = player.GetComponent<CharacterController>();
        Assert.IsFalse(controller.enabled, "The scripted walk must not collide with the imported doorway.");
        Vector3 start = player.transform.position;
        opening.Tick(OpeningWalkout.Duration);
        Vector3 end = player.transform.position;
        Vector3 outward = end - opening.DoorGround;
        outward.y = 0f;
        Assert.AreEqual(4.5f, outward.magnitude, 0.05f);
        Assert.Less(Vector3.Dot(start - opening.DoorGround, outward.normalized), -0.8f);
        float feet = end.y + controller.center.y - controller.height * 0.5f;
        Assert.IsTrue(Physics.Raycast(new Vector3(end.x, feet + 0.1f, end.z), Vector3.down,
            out RaycastHit hit, 0.2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore));
        Assert.AreEqual(hit.point.y + 0.02f, feet, 0.03f);
        Assert.IsFalse(Physics.CheckCapsule(new Vector3(end.x, feet + 0.58f, end.z),
            new Vector3(end.x, feet + 1.35f, end.z), 0.42f, Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore), "Control must return outside walls, lamps and hedges.");
        opening.Dispose();
        Assert.IsTrue(controller.enabled);
    }

    [Test]
    public void DoorSwingsOnAHingeThenClosesBehindThePlayer()
    {
        Transform door = map.Find(OpeningWalkout.DoorPath);
        Assert.IsNotNull(door);
        Vector3 position = door.position;
        Quaternion rotation = door.rotation;
        Assert.IsTrue(OpeningWalkout.TryCreate(map, player, out opening));
        opening.Tick(0.8f);
        Assert.AreEqual(105f, Quaternion.Angle(rotation, door.rotation), 0.01f);
        Assert.Greater(Vector3.Distance(position, door.position), 0.5f, "Swing around an edge, not the centre pivot.");
        opening.Tick(2.7f);
        Assert.AreEqual(105f, Quaternion.Angle(rotation, door.rotation), 0.01f);
        Assert.Greater(Vector3.Distance(player.transform.position, opening.DoorGround), 3f);
        opening.Tick(OpeningWalkout.Duration);
        Assert.Less(Vector3.Distance(position, door.position), 0.001f);
        Assert.Less(Quaternion.Angle(rotation, door.rotation), 0.01f);
    }

    [Test]
    public void InterruptedOpeningRestoresTheDoorAndTheOriginalControllerState()
    {
        CharacterController controller = player.GetComponent<CharacterController>();
        controller.enabled = false;
        Transform door = map.Find(OpeningWalkout.DoorPath);
        Vector3 position = door.position;
        Quaternion rotation = door.rotation;
        Assert.IsTrue(OpeningWalkout.TryCreate(map, player, out opening));
        opening.Tick(1.5f);
        opening.Dispose();
        opening.Dispose();
        Assert.IsFalse(controller.enabled, "Do not enable a controller disabled by somebody else.");
        Assert.Less(Vector3.Distance(position, door.position), 0.001f);
        Assert.Less(Quaternion.Angle(rotation, door.rotation), 0.01f);
    }

    [Test]
    public void CinematicWalkUsesThePlayersWalkFramesAndReturnsToIdle()
    {
        var serialized = new SerializedObject(player);
        Camera camera = Camera.main;
        Assert.IsNotNull(camera);
        Vector3 towardsCamera = -Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized;
        player.AnimateCinematicWalk(towardsCamera, 0.25f);
        Assert.AreEqual(serialized.FindProperty("frontWalk").GetArrayElementAtIndex(1).objectReferenceValue,
            player.GetComponent<SpriteRenderer>().sprite);
        player.AnimateCinematicWalk(Vector3.zero, 0f);
        Assert.AreEqual(serialized.FindProperty("frontIdle").objectReferenceValue,
            player.GetComponent<SpriteRenderer>().sprite);
    }

    [Test]
    public void WalkNeverDipsBelowTheRaisedPathAndTheTemporaryInteriorIsCleanedUp()
    {
        Assert.IsTrue(OpeningWalkout.TryCreate(map, player, out opening));
        var interior = (GameObject)typeof(OpeningWalkout).GetField("doorway", PrivateInstance).GetValue(opening);
        Assert.IsFalse(interior.activeSelf);
        float startHeight = player.transform.position.y;
        for (int i = 0; i <= 38; i++)
        {
            opening.Tick(i * 0.1f);
            Assert.GreaterOrEqual(player.transform.position.y, startHeight - 0.01f);
            if (i == 10) Assert.IsTrue(interior.activeSelf, "Show a dark interior instead of the solid facade.");
        }
        Assert.IsFalse(interior.activeSelf, "The closed door needs no interior overlay.");
        opening.Dispose();
        Assert.IsTrue(interior == null);
    }

    [Test]
    public void MissingDoorDoesNotMoveOrDisableThePlayer()
    {
        Vector3 position = player.transform.position;
        Assert.IsFalse(OpeningWalkout.TryCreate(null, player, out opening));
        Assert.AreEqual(position, player.transform.position);
        Assert.IsTrue(player.GetComponent<CharacterController>().enabled);
    }

    private static void Call(object target, string method)
    {
        target.GetType().GetMethod(method, PrivateInstance).Invoke(target, null);
    }
}
