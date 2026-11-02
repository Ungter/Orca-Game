using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

public class DolphinDragInputTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private GameObject root;
    private DolphinWordPuzzle puzzle;
    private Mouse mouse;
    private Keyboard keyboard;
    private MouseState mouseState;
    private KeyboardState keyboardState;
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
        mouse = InputSystem.AddDevice<Mouse>();
        keyboard = InputSystem.AddDevice<Keyboard>();
        mouseState = new MouseState();
        keyboardState = new KeyboardState();
        InputSystem.onBeforeUpdate += FeedInput;
        root = new GameObject("Dolphin drag input test");
        InventorySystem inventory = root.AddComponent<InventorySystem>();
        inventory.RevealClueWords("Fell, Jerk, Thief");
        puzzle = root.AddComponent<DolphinWordPuzzle>();
        puzzle.Begin();
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        InputSystem.onBeforeUpdate -= FeedInput;
        if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
        if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        if (root != null) Object.DestroyImmediate(root);
        if (settings != null)
        {
            settings.backgroundBehavior = background;
            settings.editorInputBehaviorInPlayMode = editorInput;
        }
        Application.runInBackground = runInBackground;
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator MouseDragsTilesBothWaysAndEscapeCancelsWithoutLosingLettersOrPausing()
    {
        SetMouse(false, 4, true);
        yield return Frames();
        Assert.IsTrue(puzzle.IsDragging);
        SetMouse(true, 0, true);
        yield return Frames();
        Assert.AreEqual("_____________", puzzle.ArrangedLetters);
        SetMouse(true, 0, false);
        yield return Frames();
        Assert.AreEqual('J', puzzle.ArrangedLetters[0]);
        Assert.AreEqual('_', puzzle.BankLetters[4]);
        SetMouse(true, 0, true);
        yield return Frames();
        SetMouse(false, 4, false);
        yield return Frames();
        Assert.AreEqual("FELLJERKTHIEF", puzzle.BankLetters);
        Assert.AreEqual("_____________", puzzle.ArrangedLetters);
        SetMouse(false, 4, true);
        yield return Frames();
        Assert.IsTrue(puzzle.IsDragging);
        keyboardState = new KeyboardState(Key.Escape);
        yield return Frames();
        Assert.IsFalse(DolphinWordPuzzle.IsOpen);
        Assert.IsFalse(puzzle.IsDragging);
        Assert.AreEqual("FELLJERKTHIEF", puzzle.BankLetters);
        Assert.IsFalse(PauseMenu.IsPaused);
    }

    private void FeedInput()
    {
        if (InputState.currentUpdateType != InputUpdateType.Dynamic) return;
        InputSystem.QueueStateEvent(mouse, mouseState);
        InputSystem.QueueStateEvent(keyboard, keyboardState);
    }

    private void SetMouse(bool isSlot, int index, bool pressed)
    {
        Vector2 point = ((Rect)typeof(DolphinWordPuzzle).GetMethod("TileRect", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { index, isSlot })).center;
        int pixel = (int)typeof(DolphinWordPuzzle).GetMethod("PixelSize", Private).Invoke(puzzle, null);
        Vector2 origin = (Vector2)typeof(DolphinWordPuzzle).GetMethod("PanelOrigin", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { pixel });
        Vector2 screen = origin + point * pixel;
        mouseState = new MouseState { position = new Vector2(screen.x, Screen.height - screen.y) }
            .WithButton(MouseButton.Left, pressed);
    }

    private IEnumerator Frames()
    {
        bool done = false;
        puzzle.StartCoroutine(Wait());
        float deadline = Time.realtimeSinceStartup + 5f;
        while (!done && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.IsTrue(done);
        IEnumerator Wait()
        {
            yield return null;
            yield return null;
            done = true;
        }
    }
}
