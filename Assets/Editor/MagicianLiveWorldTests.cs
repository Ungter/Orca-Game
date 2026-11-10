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
    private Mouse mouse;
    private MouseState mouseState;
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
        mouse = InputSystem.AddDevice<Mouse>();
        heldKeys = new KeyboardState();
        mouseState = new MouseState();
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
        Until(MagicianEncounter.Phase.Playing);
        game = entrance.GetComponent<ShellMinigame>();
        yield return null;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        InputSystem.onBeforeUpdate -= FeedKeyboard;
        if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
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
    public IEnumerator WindowBlocksMovementAndCameraOrbit_EscapeClosesWithoutPausing()
    {
        Vector3 start = player.transform.position;
        Vector3 cameraStart = camera.transform.position;
        Assert.IsFalse(game.IsWorldGame);
        Assert.IsNull(entrance.GetComponent<WorldHatStage>());
        Assert.IsTrue(player.GetComponent<CharacterController>().enabled);
        float gameTime = Read<float>(game, "phaseElapsed");
        heldKeys = new KeyboardState(Key.W, Key.L);
        yield return WaitForGameTime(0.2f);
        Assert.Less(Vector3.Distance(start, player.transform.position), 0.001f);
        Assert.Less(Vector3.Distance(cameraStart, camera.transform.position), 0.001f);
        Assert.Greater(Read<float>(game, "phaseElapsed"), gameTime + 0.1f);
        Assert.IsTrue(Interactables.BlocksMovement);
        CameraFollow follow = camera.GetComponent<CameraFollow>();
        float angle = Read<float>(follow, "cameraAngle");
        heldKeys = new KeyboardState(Key.W, Key.L);
        yield return WaitForGameTime(0.15f);
        Assert.AreEqual(angle, Read<float>(follow, "cameraAngle"));
        Assert.AreEqual(MagicianEncounter.Phase.Playing, encounter.CurrentPhase);
        Assert.IsTrue(game.IsShowing);
        heldKeys = new KeyboardState(Key.Escape);
        float deadline = Time.realtimeSinceStartup + 3f;
        while (encounter.CurrentPhase != MagicianEncounter.Phase.Idle && Time.realtimeSinceStartup < deadline)
            yield return null;
        Assert.AreEqual(MagicianEncounter.Phase.Idle, encounter.CurrentPhase);
        Assert.IsFalse(game.IsShowing);
        Assert.IsFalse(PauseMenu.IsPaused, "Esc belongs to the hat game, not the pause menu.");
        Assert.AreEqual(1f, Time.timeScale);
        Assert.IsFalse(Interactables.BlocksMovement);
        heldKeys = new KeyboardState(Key.D);
        yield return WaitForGameTime(0.2f);
        Assert.Greater(Vector3.Distance(start, player.transform.position), 0.1f);
    }

    [UnityTest]
    public IEnumerator ClickingAWindowHatSelectsItAndTheNextRoundButtonWorks()
    {
        MethodInfo tick = typeof(ShellMinigame).GetMethod("Tick", Private);
        for (int i = 0; i < 2000 && !game.AcceptsGuess; i++) tick.Invoke(game, new object[] { 0.05f });
        Assert.IsTrue(game.AcceptsGuess);
        yield return NextFrame();
        RectTransform surface = Read<RectTransform>(game, "surface");
        Assert.IsTrue(surface.Find("Hat board/Background").gameObject.activeInHierarchy);
        Assert.IsFalse(Read<RectTransform>(encounter, "room").gameObject.activeInHierarchy);
        int winner = game.GetHatPose(0).panther == 0 ? 0 : 1;
        Vector3 point = surface.TransformPoint(new Vector3(game.GetHatPose(winner).x, -15f, 0f));
        mouseState.position = RectTransformUtility.WorldToScreenPoint(null, point);
        mouseState.buttons = 1;
        yield return NextFrame();
        mouseState.buttons = 0;
        yield return NextFrame();
        Assert.AreEqual("RoundWon", Read<object>(game, "phase").ToString());
        Assert.AreEqual(MagicianEncounter.Phase.Playing, encounter.CurrentPhase);
        Assert.IsTrue(game.IsShowing);
        tick.Invoke(game, new object[] { 0.81f });
        yield return NextFrame();
        Assert.IsTrue(game.CanContinue);
        RectTransform next = Read<UnityEngine.UI.Image>(game, "roundAction").rectTransform;
        mouseState.position = RectTransformUtility.WorldToScreenPoint(null, next.TransformPoint(Vector3.zero));
        mouseState.buttons = 1;
        yield return NextFrame();
        mouseState.buttons = 0;
        yield return NextFrame();
        Assert.AreEqual(3, game.HatCount);
        Assert.AreEqual("Reveal", Read<object>(game, "phase").ToString());
        Assert.IsFalse(Read<RectTransform>(encounter, "room").gameObject.activeInHierarchy);
        Assert.IsFalse(PauseMenu.IsPaused);
    }

    [UnityTest]
    public IEnumerator MouseSelectsBothPanthersAfterWideAndTallLayoutChanges()
    {
        MethodInfo tick = typeof(ShellMinigame).GetMethod("Tick", Private);
        Canvas canvas = Read<Canvas>(encounter, "canvas");
        var viewport = new GameObject("Resizable hat viewport", typeof(RectTransform)).GetComponent<RectTransform>();
        viewport.SetParent(canvas.transform, false);
        Vector2 bounds = ((RectTransform)canvas.transform).rect.size;
        Vector2[] sizes =
        {
            new Vector2(1280f, 720f), new Vector2(1024f, 768f),
            new Vector2(2560f, 1080f), new Vector2(720f, 1280f)
        };
        foreach (Vector2 size in sizes)
        {
            viewport.sizeDelta = size * Mathf.Min(bounds.x / size.x, bounds.y / size.y);
            game.Play(viewport);
            typeof(ShellMinigame).GetMethod("BeginRound", Private).Invoke(game, new object[] { 2 });
            for (int i = 0; i < 2000 && !game.AcceptsGuess; i++) tick.Invoke(game, new object[] { 0.05f });
            Assert.IsTrue(game.AcceptsGuess);
            yield return NextFrame();
            RectTransform surface = Read<RectTransform>(game, "surface");
            Assert.AreEqual(game.Controls, Read<PixelText>(game, "controls").text);
            for (int type = 0; type < 2; type++)
            {
                int winner = -1;
                for (int i = 0; i < game.HatCount; i++)
                    if (game.GetHatPose(i).panther == type) winner = i;
                Assert.GreaterOrEqual(winner, 0);
                Vector3 point = surface.TransformPoint(new Vector3(game.GetHatPose(winner).x, -15f, 0f));
                mouseState.position = RectTransformUtility.WorldToScreenPoint(null, point);
                Assert.That(mouseState.position.x, Is.InRange(0f, (float)Screen.width));
                Assert.That(mouseState.position.y, Is.InRange(0f, (float)Screen.height));
                mouseState.buttons = 1;
                yield return NextFrame();
                mouseState.buttons = 0;
                yield return NextFrame();
                Assert.AreEqual(type == 0 ? "Found" : "Won", Read<object>(game, "phase").ToString(),
                    $"Click must hit the correct hat at {size}.");
                if (type == 0)
                {
                    tick.Invoke(game, new object[] { 0.81f });
                    yield return NextFrame();
                }
            }
        }
    }

    private void Until(MagicianEncounter.Phase phase)
    {
        for (int i = 0; i < 1000 && encounter.CurrentPhase != phase; i++) encounter.Tick(1f / 60f, false);
        Assert.AreEqual(phase, encounter.CurrentPhase);
    }

    private void FeedKeyboard()
    {
        if (InputState.currentUpdateType == InputUpdateType.Dynamic)
        {
            InputSystem.QueueStateEvent(keyboard, heldKeys);
            InputSystem.QueueStateEvent(mouse, mouseState);
        }
    }

    // EditMode's yield return null only waits for an editor update. Under a frame-rate cap (the
    // pause menu's saved setting) several editor updates can pass between two play-mode frames.
    private static IEnumerator NextFrame()
    {
        int frame = Time.frameCount;
        float deadline = Time.realtimeSinceStartup + 2f;
        while (Time.frameCount == frame && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.AreNotEqual(frame, Time.frameCount, "Play-mode frames did not advance.");
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
