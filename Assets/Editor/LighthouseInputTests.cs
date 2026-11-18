using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

// Real E/Esc presses through Update, not just calls to the ferry's logic.
public class LighthouseInputTests
{
    private GameObject root;
    private LighthouseIsland lighthouse;
    private LighthouseFerry ferry;
    private PlayerController player;
    private Camera cameraView;
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
        root = new GameObject("Lighthouse input test");
        lighthouse = root.AddComponent<LighthouseIsland>();
        Transform tower = Child("Supplied tower", new Vector3(50f, 0f, 0f));
        Transform entry = Child("Tower entrance", new Vector3(50f, 0f, 0f));
        Transform pier = Child("Lighthouse pier", new Vector3(40f, 0f, 0f));
        Transform berth = Child("Ferry berth", new Vector3(36f, 0f, 0f));
        lighthouse.Configure(tower, entry, pier, berth);
        GameObject visitor = Child("Test player", new Vector3(-40f, 0f, 0f)).gameObject;
        visitor.tag = "Player";
        visitor.AddComponent<SpriteRenderer>().sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/Player_Front_Idle.png");
        player = visitor.AddComponent<PlayerController>();
        cameraView = Child("Test camera", new Vector3(-50f, 3f, -5f)).gameObject.AddComponent<Camera>();
        cameraView.tag = "MainCamera";
        CameraFollow follow = cameraView.gameObject.AddComponent<CameraFollow>();
        typeof(CameraFollow).GetField("target", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(follow, visitor.transform);
        GameObject boat = Object.Instantiate(Resources.Load<GameObject>("WorldModels/StartingBoat"), root.transform, false);
        boat.transform.position = new Vector3(-46f, -0.05f, 0f);
        ferry = boat.GetComponent<LighthouseFerry>();
        ferry.Configure(player, new Vector3(-40f, 0f, 0f));
        player.PlaceOnGround(ferry.MainLanding);
        keys = new KeyboardState();
        yield return NextFrame();
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        InputSystem.onBeforeUpdate -= FeedInput;
        if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        ferry?.Cancel();
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
    public IEnumerator EBoardsTheFerryAndEscapeCancelsWithoutOpeningPause()
    {
        LighthouseTestProgress.Solve(root);
        keys = new KeyboardState(Key.E);
        yield return NextFrame();
        Assert.IsTrue(LighthouseFerry.IsSailing);
        Assert.AreEqual(LighthouseFerry.Phase.Boarding, ferry.CurrentPhase);
        Assert.IsFalse(player.enabled);
        ferry.Tick(1f);
        ferry.Tick(ferry.CrossingDuration * 0.5f);
        keys = new KeyboardState(Key.Escape);
        yield return NextFrame();
        Assert.IsFalse(LighthouseFerry.IsSailing);
        Assert.IsFalse(PauseMenu.IsPaused);
        Assert.IsTrue(player.enabled);
        Assert.IsTrue(player.GetComponent<CharacterController>().enabled);
        Assert.Less(Vector2.Distance(new Vector2(player.transform.position.x, player.transform.position.z),
            new Vector2(ferry.MainLanding.x, ferry.MainLanding.z)), 0.01f);
    }

    [UnityTest]
    public IEnumerator DisablingTheFerryMidRunReleasesControls()
    {
        LighthouseTestProgress.Solve(root);
        player.enabled = false;
        player.GetComponent<CharacterController>().enabled = false;
        Assert.IsTrue(ferry.Begin(cameraView));
        ferry.enabled = false;
        yield return NextFrame();
        Assert.IsFalse(LighthouseFerry.IsSailing);
        Assert.IsFalse(player.enabled);
        Assert.IsFalse(player.GetComponent<CharacterController>().enabled);
        Assert.IsFalse(Interactables.Busy);
    }

    [UnityTest]
    public IEnumerator LockedEPressesDoNothingAndTheTowerHasNothingToStart()
    {
        keys = new KeyboardState(Key.E);
        yield return NextFrame();
        Assert.IsFalse(LighthouseFerry.IsSailing);
        LighthouseTestProgress.Solve(root);
        ArriveAtLighthouse();
        player.PlaceOnGround(lighthouse.Entrance);
        keys = new KeyboardState();
        yield return NextFrame();
        keys = new KeyboardState(Key.E);
        yield return NextFrame();
        Assert.IsFalse(Interactables.Busy, "No game starts at the tower's foot.");
        Assert.IsTrue(player.enabled);
    }

    private void ArriveAtLighthouse()
    {
        Assert.IsTrue(ferry.Begin(cameraView));
        ferry.Tick(1f);
        ferry.Tick(ferry.CrossingDuration + 0.1f);
        ferry.Tick(1f);
        Assert.IsTrue(ferry.AtLighthouse);
    }

    private Transform Child(string name, Vector3 position)
    {
        var child = new GameObject(name).transform;
        child.SetParent(root.transform, false);
        child.position = position;
        return child;
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
