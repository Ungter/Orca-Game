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
        Assert.AreEqual(15, root.GetComponentsInChildren<SpriteRenderer>(true).Length);
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
    public void EveryFinalRoundHatCanBeChosenByWalkingUpToIt()
    {
        Transform player = CreatePlayer();
        game.PlayInWorld(null, Vector3.zero);
        game.StartWorldPlayback();
        Call("BeginRound", 2);
        for (int i = 0; i < 2000 && !game.AcceptsGuess; i++) Call("Tick", 0.05f);
        Assert.IsTrue(game.AcceptsGuess);
        WorldHatStage stage = root.GetComponent<WorldHatStage>();
        stage.Frame();
        Vector3 outward = stage.ViewingPoint - stage.Centre;
        outward.y = 0f;
        outward.Normalize();
        int seen = 0;
        foreach (SpriteRenderer hat in root.GetComponentsInChildren<SpriteRenderer>())
        {
            if (!hat.name.StartsWith("Hat ")) continue;
            int index = int.Parse(hat.name.Substring(4)) - 1;
            Vector3 ground = hat.transform.position;
            ground.y = 0f;
            player.position = ground + outward * 1.5f;
            Assert.AreEqual(index, stage.NearestHat());
            seen++;
        }
        Assert.AreEqual(5, seen);
        player.position = stage.ViewingPoint;
        Assert.AreEqual(-1, stage.NearestHat(), "Watching from the viewing point reaches no hat.");
    }

    [Test]
    public void OnlyUnrevealedHatsAreOfferedAndOnlyWhileGuessing()
    {
        Transform player = CreatePlayer();
        game.PlayInWorld(null, Vector3.zero);
        game.StartWorldPlayback();
        Call("BeginRound", 2);
        WorldHatStage stage = root.GetComponent<WorldHatStage>();
        stage.Frame();
        int first = -1;
        for (int i = 0; i < game.HatCount; i++)
            if (game.GetHatPose(i).panther == 0) first = i;
        player.position = Ground(first);
        Assert.AreEqual(-1, stage.NearestHat(), "Hats cannot be chosen during the reveal.");
        for (int i = 0; i < 2000 && !game.AcceptsGuess; i++) Call("Tick", 0.05f);
        stage.Frame();
        first = -1;
        for (int i = 0; i < game.HatCount; i++)
            if (game.GetHatPose(i).panther == 0) first = i;
        player.position = Ground(first);
        Assert.AreEqual(first, stage.NearestHat());
        game.GuessHat(stage.NearestHat());
        Call("Tick", 0.81f);
        Assert.IsTrue(game.AcceptsGuess);
        Assert.AreNotEqual(first, stage.NearestHat(), "A found hat cannot be chosen again.");
    }

    [Test]
    public void OutdoorControlsUseEAndDropMovementHints()
    {
        game.PlayInWorld(null, Vector3.zero);
        game.StartWorldPlayback();
        Assert.AreEqual("[ESC] END", game.Controls);
        for (int i = 0; i < 2000 && !game.AcceptsGuess; i++) Call("Tick", 0.05f);
        Call("UpdateVisuals");
        Assert.AreEqual("[E] CHOOSE A HAT    [ESC] END", game.Controls);
        string shown = root.GetComponent<WorldHatStage>().Hud.Find("Controls").GetComponent<PixelText>().text;
        Assert.AreEqual(game.Controls, shown);
        StringAssert.DoesNotContain("WASD", shown);
        StringAssert.DoesNotContain("J/L", shown);
        StringAssert.DoesNotContain("WALK AWAY", shown);
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
        Call("BeginRound", 2);
        for (int i = 0; i < 2000 && !game.AcceptsGuess; i++) Call("Tick", 0.05f);
        int count = game.PantherCount;
        for (int type = 0; type < count; type++)
        {
            for (int i = 0; i < game.HatCount; i++)
            {
                if (game.GetHatPose(i).panther != type) continue;
                game.GuessHat(i);
                break;
            }
            if (type < count - 1) Call("Tick", 0.81f);
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

    private Transform CreatePlayer()
    {
        var player = new GameObject("Player");
        player.tag = "Player";
        player.transform.SetParent(root.transform, false);
        return player.transform;
    }

    private Vector3 Ground(int hat)
    {
        Vector3 position = root.transform.Find("Entrance hat stage/Hat " + (hat + 1)).position;
        position.y = 0f;
        return position;
    }

    private void Call(string name, params object[] arguments) =>
        typeof(ShellMinigame).GetMethod(name, Private).Invoke(game, arguments);
}
