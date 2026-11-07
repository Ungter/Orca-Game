using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public class OpeningVoyageTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private Scene scene;
    private Transform map;
    private PlayerController player;
    private OpeningVoyage voyage;

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
        voyage?.Dispose();
        if (voyage != null && voyage.Boat != null)
        {
            Object.DestroyImmediate(voyage.Boat.GetComponent<Renderer>().sharedMaterial);
            Object.DestroyImmediate(voyage.Boat);
        }
        voyage = null;
        EditorSceneManager.CloseScene(scene, true);
    }

    [Test]
    public void TheBoatSetsOffOutAtSeaAndComesInToTheDocksEndWithThePlayerAboard()
    {
        Assert.IsTrue(OpeningVoyage.TryCreate(map, player, out voyage));
        CharacterController controller = player.GetComponent<CharacterController>();
        Assert.IsFalse(controller.enabled, "The ride is scripted.");
        Assert.AreEqual(OpeningVoyage.Phase.Drifting, voyage.CurrentPhase);
        Assert.Greater(Flat(voyage.StartPoint - voyage.MooredPoint), 59f, "It sets off well out at sea.");
        Assert.AreEqual(voyage.MooredPoint.z, voyage.StartPoint.z, 0.01f, "It comes up to the dock along x.");
        Collider ground = Find(map, OpeningVoyage.GroundName).GetComponent<Collider>();
        for (float t = 0f; t <= 1f; t += 0.05f)
        {
            Vector3 over = Vector3.Lerp(voyage.StartPoint, voyage.MooredPoint, t) + Vector3.up * 200f;
            Assert.IsFalse(ground.Raycast(new Ray(over, Vector3.down), out _, 600f), "The whole crossing is over the sea.");
        }
        Assert.AreEqual(voyage.Boat.transform.position.y + 0.4f, Feet(controller), 0.1f, "The player stands on the deck.");

        voyage.Tick(voyage.CrossingDuration * 0.5f, true);
        Assert.AreEqual(OpeningVoyage.Phase.Drifting, voyage.CurrentPhase, "SPACE does nothing until the boat is in.");
        voyage.Tick(voyage.CrossingDuration, false);
        Assert.IsTrue(voyage.IsWaitingForJump);
        Assert.Less(Flat(voyage.Boat.transform.position - voyage.MooredPoint), 0.01f);
        Assert.Less(Flat(voyage.MooredPoint - voyage.Landing), 6f, "It moors right by the dock's end.");
        Assert.AreEqual(voyage.Boat.transform.position.y + 0.4f, Feet(controller), 0.1f);
    }

    [Test]
    public void TheCameraRidesRightBehindThePlayerWhoFacesAwayFromIt()
    {
        Assert.IsTrue(OpeningVoyage.TryCreate(map, player, out voyage));
        voyage.Tick(1f, false);
        voyage.GetCameraPose(out Vector3 position, out Quaternion rotation);
        Vector3 forward = voyage.MooredPoint - voyage.StartPoint;
        forward.y = 0f;
        forward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Vector3 fromPlayer = position - player.transform.position;
        Assert.Less(Vector3.Dot(fromPlayer, forward), -3f, "Behind the player.");
        Assert.That(Vector3.Dot(fromPlayer, right), Is.InRange(0.5f, 3f), "A little to their right.");
        Assert.Greater(Vector3.Dot(rotation * Vector3.forward, forward), 0.8f, "Looking the way the boat goes.");

        Camera.main.transform.SetPositionAndRotation(position, rotation);
        voyage.AnimatePlayer(0.1f);
        var serialized = new SerializedObject(player);
        Assert.AreEqual(serialized.FindProperty("backIdle").objectReferenceValue, player.GetComponent<SpriteRenderer>().sprite,
            "The player's back is to the camera.");
    }

    [Test]
    public void SpaceJumpsThePlayerOntoTheDockAndHandsControlBack()
    {
        Assert.IsTrue(OpeningVoyage.TryCreate(map, player, out voyage));
        CharacterController controller = player.GetComponent<CharacterController>();
        voyage.Tick(voyage.CrossingDuration + 0.1f, false);
        float deck = Feet(controller);
        voyage.Tick(0.02f, true);
        Assert.AreEqual(OpeningVoyage.Phase.Jumping, voyage.CurrentPhase);
        float highest = deck;
        for (int i = 0; i < 120 && !voyage.HasLanded; i++)
        {
            voyage.Tick(1f / 60f, false);
            highest = Mathf.Max(highest, Feet(controller));
        }
        Assert.IsTrue(voyage.HasLanded);
        Assert.Greater(highest, (deck + voyage.Landing.y) * 0.5f + 1f, "It is a jump, not a slide.");

        Vector3 at = player.transform.position;
        float feet = Feet(controller);
        Transform dock = Find(map, OpeningVoyage.DockName);
        Assert.IsTrue(Physics.Raycast(new Vector3(at.x, feet + 0.3f, at.z), Vector3.down, out RaycastHit hit, 1f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore));
        Assert.IsTrue(hit.collider.transform.IsChildOf(dock), "The jump lands on the dock.");
        Assert.AreEqual(hit.point.y + 0.02f, feet, 0.05f);
        Assert.IsFalse(Physics.CheckCapsule(new Vector3(at.x, feet + 0.58f, at.z), new Vector3(at.x, feet + 1.35f, at.z), 0.42f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), "Control returns clear of posts and lamps.");

        voyage.Dispose();
        Assert.IsTrue(controller.enabled);
        Assert.IsTrue(voyage.Boat.GetComponent<Collider>().enabled, "The moored boat is solid.");
        Assert.Less(Flat(voyage.Boat.transform.position - voyage.MooredPoint), 0.01f);
        CameraFollow follow = Camera.main.GetComponent<CameraFollow>();
        Assert.IsNotNull(follow);
        Physics.SyncTransforms();
        Assert.IsTrue(follow.HasClearView(player.transform.position, voyage.Inland),
            "Play starts with the follow camera at its full distance, not pulled in by a post on the dock.");
    }

    [Test]
    public void StoppingMidCrossingMoorsTheBoatAndPutsThePlayerOnTheDock()
    {
        CharacterController controller = player.GetComponent<CharacterController>();
        controller.enabled = false;
        Assert.IsTrue(OpeningVoyage.TryCreate(map, player, out voyage));
        voyage.Tick(voyage.CrossingDuration * 0.3f, false);
        voyage.Dispose();
        voyage.Dispose();
        Assert.IsFalse(controller.enabled, "Do not enable a controller disabled by somebody else.");
        Assert.Less(Flat(voyage.Boat.transform.position - voyage.MooredPoint), 0.01f);
        Assert.Less(Flat(player.transform.position - voyage.Landing), 0.01f);
    }

    [Test]
    public void ADocklessMapLeavesThePlayerAlone()
    {
        Vector3 position = player.transform.position;
        Assert.IsFalse(OpeningVoyage.TryCreate(null, player, out voyage));
        Assert.AreEqual(position, player.transform.position);
        Assert.IsTrue(player.GetComponent<CharacterController>().enabled);
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

    // TitleScreen disables PlayerController for the whole opening, so its LateUpdate never
    // runs; the scripted jump must still keep the shadow quad narrow and facing the light.
    [Test]
    public void CinematicWalkKeepsTheShadowNarrowAndFacingTheLightWhileThePlayerIsFrozen()
    {
        var serialized = new SerializedObject(player);
        var caster = (Renderer)serialized.FindProperty("shadowCaster").objectReferenceValue;
        Assert.IsNotNull(caster);
        float widthScale = serialized.FindProperty("shadowWidthScale").floatValue;
        var baseScale = (Vector3)typeof(PlayerController).GetField("shadowBaseScale", PrivateInstance).GetValue(player);
        var light = (Light)typeof(PlayerController).GetField("shadowLight", PrivateInstance).GetValue(player);
        Call(player, "Awake");
        Assert.AreEqual(baseScale, typeof(PlayerController).GetField("shadowBaseScale", PrivateInstance).GetValue(player),
            "A second Awake must not narrow the shadow twice.");

        player.enabled = false;
        caster.transform.localScale = baseScale;
        caster.transform.localRotation = Quaternion.identity;
        player.AnimateCinematicWalk(Vector3.forward, 0.1f);
        Assert.AreEqual(baseScale.x * widthScale, caster.transform.localScale.x, 1e-4f);
        Assert.AreEqual(baseScale.y, caster.transform.localScale.y, 1e-4f);
        if (light != null)
        {
            Vector3 towardsLight = Vector3.ProjectOnPlane(light.transform.forward, Vector3.up).normalized;
            Assert.Less(Vector3.Angle(towardsLight, caster.transform.forward), 0.1f);
        }
    }

    private float Feet(CharacterController controller)
    {
        return player.transform.position.y + controller.center.y - controller.height * 0.5f;
    }

    private static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

    private static Transform Find(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform child in root)
        {
            Transform found = Find(child, name);
            if (found != null) return found;
        }
        return null;
    }

    private static void Call(object target, string method)
    {
        target.GetType().GetMethod(method, PrivateInstance).Invoke(target, null);
    }
}
