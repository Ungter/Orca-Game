using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public class CachudDoorwayTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private Scene scene;
    private Transform map;
    private Transform actor;
    private PlayerController player;
    private CachudDoorway doorway;

    [SetUp]
    public void SetUp()
    {
        scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Additive);
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == CachudDoorway.MapName) map = root.transform;
            if (root.TryGetComponent(out PlayerController found)) player = found;
            if (root.TryGetComponent(out CachudCallout cachud)) actor = cachud.transform;
        }
        Assert.IsNotNull(map);
        Assert.IsNotNull(player);
        Assert.IsNotNull(actor);
        player.GetType().GetMethod("Awake", PrivateInstance).Invoke(player, null);
        // Knocking: the player stands in front of the door.
        Transform door = CachudDoorway.FindDoor(map);
        Assert.IsNotNull(door);
        Physics.SyncTransforms();
    }

    [TearDown]
    public void TearDown()
    {
        doorway?.Dispose();
        doorway = null;
        EditorSceneManager.CloseScene(scene, true);
    }

    [Test]
    public void HeWalksOutFromInsideTheDoorOntoClearGroundAndTheVisitorWaitsInFront()
    {
        StandAtTheDoor();
        Assert.IsTrue(CachudDoorway.TryCreate(map, actor, player.transform, out doorway));
        Assert.Less(Vector3.Dot(actor.position - doorway.DoorGround, doorway.Outward), -0.8f, "He starts indoors.");
        doorway.Tick(CachudDoorway.Duration);
        Vector3 end = actor.position;
        Assert.AreEqual(2f, Vector3.Dot(end - doorway.DoorGround, doorway.Outward), 0.05f);
        Physics.SyncTransforms();
        Assert.IsTrue(Physics.Raycast(end + Vector3.up * 0.3f, Vector3.down, out RaycastHit ground, 0.6f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore));
        Assert.AreEqual(ground.point.y + 0.02f, end.y, 0.03f);
        foreach (Collider collider in Physics.OverlapCapsule(end + Vector3.up * 0.5f, end + Vector3.up * 1.5f, 0.35f,
                     Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            Assert.IsTrue(collider.transform.IsChildOf(actor) || collider.transform.IsChildOf(player.transform),
                "He stops clear of walls, lamps and hedges, not inside " + collider.name);
        }
        Vector3 visitor = player.transform.position - doorway.DoorGround;
        Assert.Greater(Vector3.Dot(visitor, doorway.Outward), 3f, "The visitor waits further out, facing him.");
    }

    [Test]
    public void DoorSwingsOnAHingeThenClosesBehindHim()
    {
        StandAtTheDoor();
        Transform door = CachudDoorway.FindDoor(map);
        Vector3 position = door.position;
        Quaternion rotation = door.rotation;
        Assert.IsTrue(CachudDoorway.TryCreate(map, actor, player.transform, out doorway));
        doorway.Tick(0.8f);
        Assert.AreEqual(105f, Quaternion.Angle(rotation, door.rotation), 0.01f);
        Assert.Greater(Vector3.Distance(position, door.position), 0.5f, "Swing around an edge, not the centre pivot.");
        doorway.Tick(2.7f);
        Assert.AreEqual(105f, Quaternion.Angle(rotation, door.rotation), 0.01f);
        Assert.Greater(Vector3.Dot(actor.position - doorway.DoorGround, doorway.Outward), 1f, "He is out before it shuts.");
        doorway.Tick(CachudDoorway.Duration);
        Assert.Less(Vector3.Distance(position, door.position), 0.001f);
        Assert.Less(Quaternion.Angle(rotation, door.rotation), 0.01f);
    }

    [Test]
    public void TheDarkInteriorShowsOnlyWhileTheDoorIsOpenAndIsCleanedUp()
    {
        StandAtTheDoor();
        Assert.IsTrue(CachudDoorway.TryCreate(map, actor, player.transform, out doorway));
        var interior = (GameObject)typeof(CachudDoorway).GetField("doorway", PrivateInstance).GetValue(doorway);
        Assert.IsFalse(interior.activeSelf);
        doorway.Tick(1f);
        Assert.IsTrue(interior.activeSelf, "Show a dark interior instead of the solid facade.");
        doorway.Tick(CachudDoorway.Duration);
        Assert.IsFalse(interior.activeSelf, "The closed door needs no interior.");
        doorway.Dispose();
        Assert.IsTrue(interior == null);
    }

    [Test]
    public void AnInterruptedEntranceShutsTheDoorAndLeavesHimOutside()
    {
        StandAtTheDoor();
        Transform door = CachudDoorway.FindDoor(map);
        Vector3 position = door.position;
        Quaternion rotation = door.rotation;
        Assert.IsTrue(CachudDoorway.TryCreate(map, actor, player.transform, out doorway));
        doorway.Tick(1.5f);
        doorway.Dispose();
        doorway.Dispose();
        Assert.Less(Vector3.Distance(position, door.position), 0.001f);
        Assert.Less(Quaternion.Angle(rotation, door.rotation), 0.01f);
        Assert.Less(Vector3.Distance(actor.position, doorway.StopPoint + Vector3.up * 0.02f), 0.001f);
    }

    [Test]
    public void NoDoorTouchesNothing()
    {
        Vector3 position = actor.position;
        Assert.IsFalse(CachudDoorway.TryCreate(null, actor, player.transform, out doorway));
        Assert.AreEqual(position, actor.position);
    }

    // As if the player had walked up and knocked: just outside the door.
    private void StandAtTheDoor()
    {
        Transform door = CachudDoorway.FindDoor(map);
        Bounds bounds = door.GetComponent<Renderer>().bounds;
        MeshFilter filter = door.GetComponent<MeshFilter>();
        Vector3 x = door.TransformVector(Vector3.right * filter.sharedMesh.bounds.size.x);
        Vector3 z = door.TransformVector(Vector3.forward * filter.sharedMesh.bounds.size.z);
        Vector3 normal = Vector3.ProjectOnPlane(x.sqrMagnitude < z.sqrMagnitude ? x : z, Vector3.up).normalized;
        // The player's authored spawn by this house fixes which face is the outside.
        if (Vector3.Dot(normal, player.transform.position - bounds.center) < 0f) normal = -normal;
        player.PlaceOnGround(new Vector3(bounds.center.x, bounds.min.y, bounds.center.z) + normal * 2f);
        Physics.SyncTransforms();
    }
}
