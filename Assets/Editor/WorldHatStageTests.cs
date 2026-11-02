using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

public class WorldHatStageTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private GameObject root;
    private Camera camera;
    private ShellMinigame game;
    private Random.State randomState;

    [SetUp]
    public void SetUp()
    {
        randomState = Random.state;
        Random.InitState(91);
        camera = new GameObject("Outdoor test camera", typeof(Camera)).GetComponent<Camera>();
        camera.tag = "MainCamera";
        camera.pixelRect = new Rect(0f, 0f, 1280f, 720f);
        root = new GameObject("Outdoor hat test");
        game = root.AddComponent<ShellMinigame>();
    }

    [TearDown]
    public void TearDown()
    {
        game.Hide();
        Object.DestroyImmediate(root);
        Object.DestroyImmediate(camera.gameObject);
        Random.state = randomState;
    }

    [Test]
    public void OutdoorGameCreatesWorldSpritesInsteadOfAnOpaqueBoard()
    {
        game.PlayInWorld(null, Vector3.zero);
        Assert.IsTrue(game.IsWorldGame);
        Assert.IsTrue(game.IsShowing);
        Assert.AreEqual(18, root.GetComponentsInChildren<SpriteRenderer>(true).Length);
        foreach (RawImage image in root.GetComponentsInChildren<RawImage>(true))
            Assert.AreNotEqual("Background", image.name);
        Assert.AreEqual(RenderMode.ScreenSpaceOverlay, root.GetComponent<WorldHatStage>().Hud.GetComponent<Canvas>().renderMode);
        WorldHatStage stage = root.GetComponent<WorldHatStage>();
        Assert.That(stage.Centre.y, Is.InRange(2f, 4f));
        Assert.AreEqual(3f, new Vector2(stage.Centre.x, stage.Centre.z).magnitude, 0.01f);
        Assert.AreEqual(12f, new Vector2(stage.ViewingPoint.x, stage.ViewingPoint.z).magnitude, 0.01f);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void OpeningRefreshingAndClosingTheStageNeverOwnsOrRestoresTheCamera(bool disable)
    {
        Vector3 position = new Vector3(1f, 4f, -6f);
        Quaternion rotation = Quaternion.Euler(24f, 20f, 0f);
        camera.transform.SetPositionAndRotation(position, rotation);
        camera.fieldOfView = 63f;
        camera.orthographic = true;
        camera.orthographicSize = 8f;
        game.PlayInWorld(null, Vector3.zero);
        Assert.AreEqual(position, camera.transform.position);
        Assert.Less(Quaternion.Angle(rotation, camera.transform.rotation), 0.001f);
        Assert.IsTrue(camera.orthographic);
        position += Vector3.right * 8f;
        rotation = Quaternion.Euler(30f, 70f, 0f);
        camera.transform.SetPositionAndRotation(position, rotation);
        root.GetComponent<WorldHatStage>().Frame();
        if (disable)
        {
            // Plain EditMode tests do not dispatch play-mode lifecycle messages.
            typeof(WorldHatStage).GetMethod("OnDisable", Private).Invoke(root.GetComponent<WorldHatStage>(), null);
        }
        else game.Hide();
        Assert.AreEqual(position, camera.transform.position);
        Assert.Less(Quaternion.Angle(rotation, camera.transform.rotation), 0.001f);
        Assert.AreEqual(63f, camera.fieldOfView);
        Assert.IsTrue(camera.orthographic);
        Assert.AreEqual(8f, camera.orthographicSize);
        Assert.IsFalse(game.IsShowing);
    }

    [Test]
    public void EveryFinalRoundHatCanBeClickedAtItsActualWorldPosition()
    {
        game.PlayInWorld(null, Vector3.zero);
        game.StartWorldPlayback();
        Call("BeginRound", 3);
        for (int i = 0; i < 2000 && !game.AcceptsGuess; i++) Call("Tick", 0.05f);
        Assert.IsTrue(game.AcceptsGuess);
        WorldHatStage stage = root.GetComponent<WorldHatStage>();
        camera.transform.position = stage.Centre + new Vector3(-22f, 8f, -22f);
        camera.transform.LookAt(stage.Centre);
        stage.Frame();
        int seen = 0;
        foreach (SpriteRenderer hat in root.GetComponentsInChildren<SpriteRenderer>())
        {
            if (!hat.name.StartsWith("Hat ")) continue;
            int index = int.Parse(hat.name.Substring(4)) - 1;
            Vector3 point = camera.WorldToScreenPoint(hat.transform.position);
            Assert.Greater(point.z, 0f);
            Assert.That(point.x, Is.InRange(0f, 1280f));
            Assert.That(point.y, Is.InRange(0f, 720f));
            Assert.AreEqual(index, stage.HatAt(point));
            seen++;
        }
        Assert.AreEqual(6, seen);
    }

    [Test]
    public void WorldPickingUsesTheFrontmostHatAndCannotClickThroughWorldGeometry()
    {
        game.PlayInWorld(null, Vector3.zero);
        game.StartWorldPlayback();
        for (int i = 0; i < 2000 && !game.AcceptsGuess; i++) Call("Tick", 0.05f);
        Assert.IsTrue(game.AcceptsGuess);
        WorldHatStage stage = root.GetComponent<WorldHatStage>();
        stage.Frame();
        Transform first = root.transform.Find("Entrance hat stage/Hat 1");
        Transform second = root.transform.Find("Entrance hat stage/Hat 2");
        first.position = Vector3.forward * 30f;
        second.position = Vector3.forward * 20f;
        Vector3 point = camera.WorldToScreenPoint(second.position);
        Assert.AreEqual(1, stage.HatAt(point));
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.transform.SetParent(root.transform, false);
        wall.transform.position = Vector3.forward * 10f;
        wall.transform.localScale = Vector3.one * 5f;
        Physics.SyncTransforms();
        Assert.AreEqual(-1, stage.HatAt(point));
    }

    [Test]
    public void ReopeningUnfinishedOutdoorGameKeepsItsRoundAndProgress()
    {
        game.PlayInWorld(null, Vector3.zero);
        game.StartWorldPlayback();
        Call("BeginRound", 2);
        for (int i = 0; i < 2000 && !game.AcceptsGuess; i++) Call("Tick", 0.05f);
        int first = -1;
        for (int i = 0; i < game.HatCount; i++)
            if (game.GetHatPose(i).panther == 0) first = i;
        game.GuessHat(first);
        Call("Tick", 0.81f);
        game.Hide();
        game.PlayInWorld(null, Vector3.zero);
        Assert.AreEqual(5, game.HatCount);
        Assert.AreEqual(1, game.RequestedPanther);
        Assert.IsTrue(game.GetHatPose(first).revealed);
        Assert.IsFalse(game.AcceptsGuess, "Wait for the outdoor fade to finish.");
        game.StartWorldPlayback();
        Assert.IsTrue(game.AcceptsGuess);
    }

    [Test]
    public void OutdoorGameDoesNotHideWorldGeometryWhenThePlayerCanWalkAround()
    {
        game.PlayInWorld(null, Vector3.zero);
        Vector3 centre = root.GetComponent<WorldHatStage>().Centre;
        camera.transform.position = centre + new Vector3(-22f, 8f, -22f);
        camera.transform.LookAt(centre);
        Vector3 obstruction = Vector3.Lerp(camera.transform.position, centre, 0.5f);
        game.Hide();
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.transform.SetParent(root.transform, false);
        cube.transform.position = obstruction;
        cube.transform.localScale = Vector3.one * 5f;
        Renderer renderer = cube.GetComponent<Renderer>();
        game.PlayInWorld(null, Vector3.zero);
        Assert.IsTrue(renderer.enabled);
        root.GetComponent<WorldHatStage>().Frame();
        Assert.IsTrue(renderer.enabled);
        game.Hide();
        Assert.IsTrue(renderer.enabled);
    }

    [Test]
    public void EntranceAndViewingPointFollowTheWalkableGroundAndIgnoreThePlayerAndTriggers()
    {
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.transform.SetParent(root.transform);
        ground.transform.position = Vector3.up * 1.5f;
        ground.transform.localScale = new Vector3(80f, 1f, 80f);
        GameObject trigger = GameObject.CreatePrimitive(PrimitiveType.Cube);
        trigger.transform.SetParent(root.transform);
        trigger.transform.position = Vector3.up * 2.9f;
        trigger.transform.localScale = new Vector3(80f, 0.1f, 80f);
        trigger.GetComponent<Collider>().isTrigger = true;
        GameObject player = GameObject.CreatePrimitive(PrimitiveType.Cube);
        player.transform.SetParent(root.transform);
        player.tag = "Player";
        player.transform.position = new Vector3(-1f, 0f, -1f).normalized * 12f + Vector3.up * 2.5f;
        player.transform.localScale = Vector3.one * 0.6f;
        Physics.SyncTransforms();
        game.PlayInWorld(null, Vector3.zero);
        WorldHatStage stage = root.GetComponent<WorldHatStage>();
        Assert.AreEqual(2f, stage.ViewingPoint.y, 0.01f);
        Assert.AreEqual(4.6f, stage.Centre.y, 0.01f);
    }

    [Test]
    public void FinalIdentityUnlocksTheOutdoorRewardAndTheTrickCanBePlayedAgain()
    {
        game.PlayInWorld(null, Vector3.zero);
        game.StartWorldPlayback();
        Call("BeginRound", 3);
        for (int i = 0; i < 2000 && !game.AcceptsGuess; i++) Call("Tick", 0.05f);
        for (int type = 0; type < 6; type++)
        {
            for (int i = 0; i < game.HatCount; i++)
            {
                if (game.GetHatPose(i).panther != type) continue;
                game.GuessHat(i);
                break;
            }
            if (type < 5) Call("Tick", 0.81f);
        }
        Assert.IsTrue(game.IsSolved);
        Call("Tick", 1f);
        Call("UpdateVisuals");
        Assert.IsFalse(root.transform.Find("Entrance hat stage").gameObject.activeSelf);
        Assert.IsTrue(root.GetComponent<WorldHatStage>().Hud.Find("Clue letter").gameObject.activeSelf);
        game.Hide();
        game.PlayInWorld(null, Vector3.zero);
        Assert.IsFalse(game.IsSolved);
        Assert.AreEqual(2, game.HatCount);
        Assert.AreEqual(0, game.RequestedPanther);
        Assert.IsTrue(root.transform.Find("Entrance hat stage").gameObject.activeSelf);
        Assert.IsFalse(root.GetComponent<WorldHatStage>().Hud.Find("Clue letter").gameObject.activeSelf);
    }

    private void Call(string name, params object[] arguments) =>
        typeof(ShellMinigame).GetMethod(name, Private).Invoke(game, arguments);
}
