using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

public class MagicianLiveWorldTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private GameObject player;
    private GameObject entrance;
    private GameObject floor;
    private Camera camera;
    private Keyboard keyboard;
    private KeyboardState heldKeys;
    private InputSettings previousInputSettings;
    private InputSettings.BackgroundBehavior previousBackgroundBehavior;
    private InputSettings.EditorInputBehaviorInPlayMode previousEditorInputBehavior;
    private bool previousRunInBackground;
    private MagicianEncounter encounter;
    private ShellMinigame game;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();
        Assert.IsTrue(Application.isPlaying);
        previousRunInBackground = Application.runInBackground;
        Application.runInBackground = true;
        previousInputSettings = InputSystem.settings;
        previousBackgroundBehavior = previousInputSettings.backgroundBehavior;
        previousEditorInputBehavior = previousInputSettings.editorInputBehaviorInPlayMode;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode =
            InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        keyboard = InputSystem.AddDevice<Keyboard>();
        heldKeys = new KeyboardState();
        InputSystem.onBeforeUpdate += FeedKeyboard;
        floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.transform.position = Vector3.down * 0.5f;
        floor.transform.localScale = new Vector3(200f, 1f, 200f);
        player = new GameObject("Player") { tag = "Player" };
        player.transform.position = Vector3.up;
        InventorySystem inventory = player.AddComponent<InventorySystem>();
        player.AddComponent<PlayerController>();
        camera = new GameObject("Live follow camera", typeof(Camera)).GetComponent<Camera>();
        camera.tag = "MainCamera";
        camera.pixelRect = new Rect(0f, 0f, 1280f, 720f);
        camera.gameObject.AddComponent<CameraFollow>();
        entrance = new GameObject("Live magician entrance");
        encounter = entrance.AddComponent<MagicianEncounter>();
        inventory.TryAdd(MagicianEncounter.WandItemName);
        encounter.Begin();
        Until(MagicianEncounter.Phase.Waiting);
        encounter.Tick(0.01f, true);
        Until(MagicianEncounter.Phase.Waiting);
        encounter.Tick(0.01f, true);
        Until(MagicianEncounter.Phase.Waiting);
        encounter.Tick(0.01f, true);
        // The lantern is handed over before the trick.
        Until(MagicianEncounter.Phase.Waiting);
        encounter.Tick(0.01f, true);
        Until(MagicianEncounter.Phase.Playing);
        game = entrance.GetComponent<ShellMinigame>();
        yield return null;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        InputSystem.onBeforeUpdate -= FeedKeyboard;
        if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        if (entrance != null) Object.DestroyImmediate(entrance);
        if (player != null) Object.DestroyImmediate(player);
        if (camera != null) Object.DestroyImmediate(camera.gameObject);
        if (floor != null) Object.DestroyImmediate(floor);
        if (previousInputSettings != null)
        {
            previousInputSettings.backgroundBehavior = previousBackgroundBehavior;
            previousInputSettings.editorInputBehaviorInPlayMode = previousEditorInputBehavior;
        }
        Application.runInBackground = previousRunInBackground;
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator PlayerMovesAndCameraFollowsAndOrbitsWhileTheGameRuns_EscapeEndsWithoutPausing()
    {
        Vector3 start = player.transform.position;
        Vector3 cameraStart = camera.transform.position;
        WorldHatStage stage = entrance.GetComponent<WorldHatStage>();
        Vector3 performance = stage.Centre;
        Vector3 placement = player.transform.position - stage.ViewingPoint;
        Assert.Less(new Vector2(placement.x, placement.z).magnitude, 0.05f);
        Assert.IsTrue(player.GetComponent<CharacterController>().enabled);
        Assert.That(player.GetComponent<CharacterController>().bounds.min.y, Is.InRange(-0.1f, 0.15f));
        float gameTime = Read<float>(game, "phaseElapsed");
        heldKeys = new KeyboardState(Key.W);
        yield return WaitForGameTime(0.2f);
        Assert.Greater(Vector3.Distance(start, player.transform.position), 0.1f);
        Assert.Greater(Vector3.Distance(cameraStart, camera.transform.position), 0.01f);
        Assert.Greater(Read<float>(game, "phaseElapsed"), gameTime + 0.1f);
        Assert.AreEqual(performance, stage.Centre, "Walking must not move the entrance game.");
        Assert.IsFalse(Interactables.BlocksMovement);
        CameraFollow follow = camera.GetComponent<CameraFollow>();
        float angle = Read<float>(follow, "cameraAngle");
        heldKeys = new KeyboardState(Key.W, Key.L);
        yield return WaitForGameTime(0.15f);
        Assert.Greater(Mathf.Abs(Read<float>(follow, "cameraAngle") - angle), 1f);
        Assert.AreEqual(MagicianEncounter.Phase.Playing, encounter.CurrentPhase);
        Assert.IsTrue(game.IsShowing);
        Vector3 playerPoint = camera.WorldToViewportPoint(player.transform.position);
        Assert.Greater(playerPoint.z, 0f);
        Assert.That(playerPoint.x, Is.InRange(0f, 1f));
        Assert.That(playerPoint.y, Is.InRange(0f, 1f));
        heldKeys = new KeyboardState(Key.Escape);
        yield return null;
        yield return null;
        Assert.AreEqual(MagicianEncounter.Phase.Idle, encounter.CurrentPhase);
        Assert.IsFalse(game.IsShowing);
        Assert.IsFalse(PauseMenu.IsPaused, "Esc belongs to the hat game, not the pause menu.");
        Assert.AreEqual(1f, Time.timeScale);
    }

    [UnityTest]
    public IEnumerator WalkingAcrossTheDistanceLimitEndsTheGameAndMovementContinues()
    {
        CharacterController controller = player.GetComponent<CharacterController>();
        controller.enabled = false;
        player.transform.position = new Vector3(encounter.MaxGameDistance - 0.25f, 1f, 0f);
        controller.enabled = true;
        heldKeys = new KeyboardState(Key.D);
        float deadline = Time.realtimeSinceStartup + 3f;
        while (encounter.CurrentPhase == MagicianEncounter.Phase.Playing && Time.realtimeSinceStartup < deadline)
            yield return null;
        Assert.AreEqual(MagicianEncounter.Phase.Idle, encounter.CurrentPhase);
        Assert.IsFalse(game.IsShowing);
        Assert.IsTrue(encounter.HasWand);
        Assert.IsFalse(Interactables.BlocksMovement);
        Vector3 afterExit = player.transform.position;
        deadline = Time.realtimeSinceStartup + 3f;
        while (player.transform.position.x < afterExit.x + 0.15f && Time.realtimeSinceStartup < deadline)
            yield return null;
        Assert.Greater(player.transform.position.x, afterExit.x + 0.1f);
        Assert.Less(Vector3.Distance(camera.transform.position, player.transform.position), 10f,
            "The normal camera follows the player's current position, not a saved doorway shot.");
        Assert.IsFalse(PauseMenu.IsPaused);
    }

    private void Until(MagicianEncounter.Phase phase)
    {
        for (int i = 0; i < 1000 && encounter.CurrentPhase != phase; i++) encounter.Tick(1f / 60f, false);
        Assert.AreEqual(phase, encounter.CurrentPhase);
    }

    private void FeedKeyboard()
    {
        if (InputState.currentUpdateType == InputUpdateType.Dynamic)
            InputSystem.QueueStateEvent(keyboard, heldKeys);
    }

    // WaitForSeconds must run on the player loop, not EditMode's editor-update enumerator.
    private IEnumerator WaitForGameTime(float seconds)
    {
        bool completed = false;
        player.GetComponent<PlayerController>().StartCoroutine(Wait());
        float deadline = Time.realtimeSinceStartup + 5f;
        while (!completed && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.IsTrue(completed, "Play-mode frames did not advance.");

        IEnumerator Wait()
        {
            yield return new WaitForSeconds(seconds);
            completed = true;
        }
    }

    private static T Read<T>(object owner, string name) =>
        (T)owner.GetType().GetField(name, Private).GetValue(owner);
}
