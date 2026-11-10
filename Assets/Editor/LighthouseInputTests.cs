using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

// Real E/Space/R/Esc presses through Update, not just calls to the game logic.
public class LighthouseInputTests
{
    private GameObject root;
    private LighthouseStacker stacker;
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
        stacker = root.AddComponent<LighthouseStacker>();
        Transform tower = Child("Supplied tower", new Vector3(50f, 0f, 0f));
        Transform entry = Child("Stacking entrance", new Vector3(50f, 0f, 0f));
        Transform pier = Child("Lighthouse pier", new Vector3(40f, 0f, 0f));
        Transform berth = Child("Ferry berth", new Vector3(36f, 0f, 0f));
        stacker.Configure(tower, entry, pier, berth);
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
        stacker?.Close();
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
    public IEnumerator SpaceStacksOncePerPressAwardsTenAndKeepsPlaying()
    {
        LighthouseTestProgress.Solve(root);
        ArriveAtLighthouse();
        player.PlaceOnGround(stacker.Entrance);
        int wins = 0;
        stacker.Won += () => wins++;
        keys = new KeyboardState(Key.E);
        yield return NextFrame();
        Assert.IsTrue(LighthouseStacker.IsOpen);
        Assert.IsTrue(Interactables.BlocksMovement);
        for (int section = 1; section <= 12; section++)
        {
            keys = new KeyboardState();
            yield return NextFrame();
            stacker.Match.Tick(-stacker.Match.Offset / stacker.Match.Speed);
            keys = new KeyboardState(Key.Space);
            yield return NextFrame();
            Assert.AreEqual(section, stacker.Match.Score, "Space did not place section " + section);
            Assert.AreEqual(section >= 10, stacker.HasWon);
            yield return NextFrame();
            Assert.AreEqual(section, stacker.Match.Score, "Holding Space placed a second section.");
        }
        Assert.AreEqual(1, wins);
        keys = new KeyboardState(Key.Escape);
        yield return NextFrame();
        Assert.IsFalse(LighthouseStacker.IsOpen);
        Assert.IsFalse(PauseMenu.IsPaused);
        Assert.IsFalse(Interactables.Busy);
        Assert.IsTrue(cameraView.GetComponent<CameraFollow>().enabled);
    }

    [UnityTest]
    public IEnumerator DisablingAnActivityMidRunReleasesControls()
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
        player.enabled = true;
        player.GetComponent<CharacterController>().enabled = true;
        Assert.IsTrue(stacker.Begin(player.transform, cameraView));
        stacker.enabled = false;
        yield return NextFrame();
        Assert.IsFalse(LighthouseStacker.IsOpen);
        Assert.IsFalse(Interactables.Busy);
        Assert.IsTrue(cameraView.GetComponent<CameraFollow>().enabled);
    }

    [UnityTest]
    public IEnumerator MissingShowsFailureAndRStartsAFreshRun()
    {
        LighthouseTestProgress.Solve(root);
        ArriveAtLighthouse();
        player.PlaceOnGround(stacker.Entrance);
        keys = new KeyboardState(Key.E);
        yield return NextFrame();
        keys = new KeyboardState(Key.Space);
        yield return NextFrame();
        Assert.IsTrue(stacker.Match.Failed);
        keys = new KeyboardState(Key.R);
        yield return NextFrame();
        Assert.IsFalse(stacker.Match.Failed);
        Assert.AreEqual(0, stacker.Match.Score);
        Assert.IsTrue(LighthouseStacker.IsOpen);
    }

    [UnityTest]
    public IEnumerator LockedEPressesDoNothingAndSolvingRevealsTheStartMarker()
    {
        keys = new KeyboardState(Key.E);
        yield return NextFrame();
        Assert.IsFalse(LighthouseFerry.IsSailing);
        player.PlaceOnGround(stacker.Entrance);
        keys = new KeyboardState();
        yield return NextFrame();
        keys = new KeyboardState(Key.E);
        yield return NextFrame();
        Assert.IsFalse(LighthouseStacker.IsOpen);
        LighthouseStartMarker marker = root.GetComponentInChildren<LighthouseStartMarker>(true);
        Assert.IsNotNull(marker);
        Assert.IsFalse(marker.gameObject.activeSelf);
        keys = new KeyboardState();
        LighthouseTestProgress.Solve(root);
        yield return NextFrame();
        Assert.IsTrue(marker.gameObject.activeSelf);
        Assert.Less(Vector3.Distance(marker.transform.position, stacker.Entrance), 0.001f);
        Assert.AreEqual(2, marker.GetComponentsInChildren<MeshRenderer>().Length);
        Assert.IsEmpty(marker.GetComponentsInChildren<Collider>(), "The marker must not obstruct walking.");
        keys = new KeyboardState(Key.E);
        yield return NextFrame();
        Assert.IsTrue(LighthouseStacker.IsOpen);
        Assert.IsFalse(marker.gameObject.activeSelf);
        keys = new KeyboardState(Key.Escape);
        yield return NextFrame();
        Assert.IsTrue(marker.gameObject.activeSelf);
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
