using System.Collections;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

public class GameplayDebugMenuInputTests
{
    private GameObject root;
    private PlayerController player;
    private GameplayDebugMenu menu;
    private DolphinWordPuzzle puzzle;
    private Keyboard keyboard;
    private KeyboardState keys;
    private InputSettings settings;
    private InputSettings.BackgroundBehavior background;
    private InputSettings.EditorInputBehaviorInPlayMode editorInput;
    private bool runInBackground;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();
        runInBackground = Application.runInBackground;
        Application.runInBackground = true;
        settings = InputSystem.settings;
        background = settings.backgroundBehavior;
        editorInput = settings.editorInputBehaviorInPlayMode;
        settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        keyboard = InputSystem.AddDevice<Keyboard>();
        InputSystem.onBeforeUpdate += FeedInput;
        root = new GameObject("Debug input test");
        root.AddComponent<SpriteRenderer>();
        root.tag = "Player";
        player = root.AddComponent<PlayerController>();
        menu = root.GetComponent<GameplayDebugMenu>();
        puzzle = root.AddComponent<DolphinWordPuzzle>();
        keys = new KeyboardState();
        yield return NextFrame();
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        InputSystem.onBeforeUpdate -= FeedInput;
        if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        puzzle?.Close();
        if (root != null) Object.DestroyImmediate(root);
        settings.backgroundBehavior = background;
        settings.editorInputBehaviorInPlayMode = editorInput;
        Application.runInBackground = runInBackground;
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator BacktickTogglesOnlyTheWindowAndSpeedHackIsOptional()
    {
        Assert.IsNotNull(menu, "Player Awake should install the runtime debug tools.");
        keys = new KeyboardState(Key.Backquote);
        yield return NextFrame();
        Assert.IsTrue(GameplayDebugMenu.IsOpen);
        Assert.IsFalse(player.DebugFlyEnabled);
        yield return NextFrame();
        Assert.IsTrue(GameplayDebugMenu.IsOpen, "Holding backtick must not repeatedly toggle.");
        Assert.IsTrue(menu.SetSpeedHack(true));
        keys = new KeyboardState();
        yield return NextFrame();
        keys = new KeyboardState(Key.Backquote);
        yield return NextFrame();
        Assert.IsFalse(GameplayDebugMenu.IsOpen);
        Assert.IsTrue(player.DebugFlyEnabled);
        keys = new KeyboardState();
        yield return NextFrame();
        keys = new KeyboardState(Key.Backquote);
        yield return NextFrame();
        Assert.IsTrue(GameplayDebugMenu.IsOpen);
        menu.SetSpeedHack(false);
        keys = new KeyboardState(Key.Escape);
        yield return NextFrame();
        Assert.IsFalse(GameplayDebugMenu.IsOpen);
        Assert.IsFalse(player.DebugFlyEnabled);
        Assert.IsFalse(PauseMenu.IsPaused);
        Assert.IsTrue(player.GetComponent<CharacterController>().enabled);
    }

    [UnityTest]
    public IEnumerator DebugWindowBlocksGameplayAndEscapeDoesNotCloseTheUnderlyingPuzzle()
    {
        puzzle.Begin();
        Assert.IsTrue(DolphinWordPuzzle.IsOpen);
        keys = new KeyboardState(Key.Backquote);
        yield return NextFrame();
        Assert.IsTrue(GameplayDebugMenu.IsOpen, "Debug tools must open during a minigame.");
        Vector3 position = player.transform.position;
        keys = new KeyboardState(Key.W, Key.Space, Key.E);
        yield return NextFrame();
        yield return NextFrame();
        Assert.AreEqual(position, player.transform.position);
        Assert.IsTrue(DolphinWordPuzzle.IsOpen);
        keys = new KeyboardState(Key.Escape);
        yield return NextFrame();
        Assert.IsFalse(GameplayDebugMenu.IsOpen);
        Assert.IsTrue(DolphinWordPuzzle.IsOpen, "Escape must close only the topmost window.");
        Assert.IsFalse(PauseMenu.IsPaused);
        keys = new KeyboardState();
        yield return NextFrame();
        keys = new KeyboardState(Key.Escape);
        yield return NextFrame();
        Assert.IsFalse(DolphinWordPuzzle.IsOpen);
    }

    private void FeedInput()
    {
        if (InputState.currentUpdateType == InputUpdateType.Dynamic) InputSystem.QueueStateEvent(keyboard, keys);
    }

    private static IEnumerator NextFrame()
    {
        int frame = Time.frameCount;
        float deadline = Time.realtimeSinceStartup + 2f;
        while (Time.frameCount == frame && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.AreNotEqual(frame, Time.frameCount);
    }
}
