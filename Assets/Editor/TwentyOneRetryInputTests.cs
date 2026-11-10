using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

// Retries driven by real key presses through the game's own Update, in Play Mode.
public class TwentyOneRetryInputTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private GameObject root;
    private TwentyOneGame game;
    private CachudCallout actor;
    private Transform player;
    private Camera view;
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
        keys = new KeyboardState();
        InputSystem.onBeforeUpdate += FeedInput;

        root = new GameObject("Twenty-one retry test");
        player = new GameObject("Test player").transform;
        player.SetParent(root.transform);
        var npc = new GameObject("Al Cachud test");
        npc.transform.SetParent(root.transform);
        npc.transform.position = Vector3.up * 1000f;
        actor = npc.AddComponent<CachudCallout>();
        actor.GetComponent<SpriteRenderer>().sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/al_cachud.png");
        Set(actor, "<HasDice>k__BackingField", true);
        Set(actor, "player", player);
        player.position = npc.transform.position + Vector3.back * 2f;
        game = npc.AddComponent<TwentyOneGame>();
        string[] animals = { "Orca", "Panther", "Gazelle", "Iguana" };
        var sprites = new Sprite[4];
        for (int i = 0; i < sprites.Length; i++)
            sprites[i] = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/" + animals[i] + "_tile_mono.png");
        Set(game, "tileSprites", sprites);
        view = new GameObject("Test camera").AddComponent<Camera>();
        view.transform.SetParent(root.transform);
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        InputSystem.onBeforeUpdate -= FeedInput;
        if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        if (game != null) game.Close();
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
    public IEnumerator EveryTryAgainWithEnterDealsADifferentLastTile()
    {
        Assert.IsTrue(game.Begin(actor, player, view));
        GameObject rules = ViewField<GameObject>("rulesPanel");
        PixelText title = ViewField<PixelText>("title");
        var limits = new List<int> { game.Match.Limit };
        for (int retry = 0; retry < 12; retry++)
        {
            Lose();
            Assert.AreEqual(TwentyOneGame.Phase.Lost, game.CurrentPhase);
            yield return NextFrame();
            Assert.IsFalse(rules.activeSelf, "The rules panel should be gone during play.");
            keys = new KeyboardState(Key.Enter);
            yield return NextFrame();
            keys = new KeyboardState();
            yield return NextFrame();
            Assert.AreEqual(TwentyOneGame.Phase.ChoosingOrder, game.CurrentPhase, "Enter did not start a new round.");
            limits.Add(game.Match.Limit);
            Assert.AreNotEqual(limits[limits.Count - 2], game.Match.Limit, "Try again kept the last tile: " + string.Join(", ", limits));
            Assert.AreEqual(game.Match.Limit, game.View.Table.ShownTiles);
            Assert.IsTrue(rules.activeSelf, "The new round's rules should show while he asks again.");
            Assert.AreEqual("TAKE 1-3 TILES.\nDON'T TAKE TILE " + game.Match.Limit + ".", ViewField<PixelText>("rules").text);
            Assert.IsTrue(title.text.StartsWith(game.Match.Limit == 30 ? "THIRTY" : "TWENTY"));
        }
    }
    // Answers the order question wrongly and plays the best rolls, which still loses.
    private void Lose()
    {
        bool[] wins = TwentyOneMatchTests.Solve(game.Match.Limit);
        Assert.IsTrue(game.ChooseOrder(!wins[0]));
        for (int i = 0; i < 20000 && game.CurrentPhase != TwentyOneGame.Phase.Lost; i++)
        {
            int best = TwentyOneMatchTests.WinningRoll(wins, game.Match.Total);
            bool stop = game.CurrentPhase == TwentyOneGame.Phase.PlayerRolling && game.Face == (best > 0 ? best : 1);
            game.Tick(stop ? 0f : 0.04f, stop);
        }
    }

    // EditMode's yield return null only waits for an editor update; under a frame-rate cap several
    // can pass between two play-mode frames, so wait for the frame count to move.
    private static IEnumerator NextFrame()
    {
        int frame = Time.frameCount;
        float deadline = Time.realtimeSinceStartup + 2f;
        while (Time.frameCount == frame && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.AreNotEqual(frame, Time.frameCount, "Play-mode frames did not advance.");
    }

    private void FeedInput()
    {
        if (InputState.currentUpdateType != InputUpdateType.Dynamic) return;
        InputSystem.QueueStateEvent(keyboard, keys);
    }

    private T ViewField<T>(string name) => (T)typeof(TwentyOneView).GetField(name, Private).GetValue(game.View);

    private static void Set(object target, string name, object value) =>
        target.GetType().GetField(name, Private).SetValue(target, value);
}
