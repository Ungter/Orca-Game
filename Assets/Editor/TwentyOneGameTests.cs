using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public class TwentyOneGameTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private GameObject root;
    private Transform player;
    private CachudCallout actor;
    private TwentyOneGame game;
    private InventorySystem inventory;
    private InventorySystem previousInventory;
    private CameraFollow follow;
    private Camera testCamera;
    private bool followWasEnabled;
    private readonly string[] animals = { "Orca", "Panther", "Gazelle", "Iguana" };

    [SetUp]
    public void SetUp()
    {
        root = new GameObject("Twenty-one test");
        player = new GameObject("Test player").transform;
        player.SetParent(root.transform);
        previousInventory = InventorySystem.instance;
        inventory = root.AddComponent<InventorySystem>();
        Call(inventory, "Awake");
        var npc = new GameObject("Al Cachud test");
        npc.transform.SetParent(root.transform);
        npc.transform.position = Vector3.up * 1000f;
        actor = npc.AddComponent<CachudCallout>();
        actor.GetComponent<SpriteRenderer>().sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/al_cachud.png");
        Set(actor, "<HasDice>k__BackingField", true);
        Set(actor, "player", player);
        player.position = npc.transform.position + Vector3.back * 2f;
        game = npc.AddComponent<TwentyOneGame>();
        var sprites = new Sprite[4];
        for (int i = 0; i < sprites.Length; i++)
            sprites[i] = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/" + animals[i] + "_tile_mono.png");
        Set(game, "tileSprites", sprites);
        var cameraObject = new GameObject("Test camera");
        cameraObject.transform.SetParent(root.transform);
        testCamera = cameraObject.AddComponent<Camera>();
        follow = cameraObject.AddComponent<CameraFollow>();
        Set(follow, "target", player);
        Call(follow, "Awake");
        Set(actor, "view", testCamera);
        followWasEnabled = follow.enabled;
    }

    [TearDown]
    public void TearDown()
    {
        game.Close();
        actor.Cancel();
        Object.DestroyImmediate(root);
        InventorySystem.instance = previousInventory;
        if (follow != null) follow.enabled = followWasEnabled;
    }

    [Test]
    public void GameWaitsForDiceAndAsksPermissionBeforeAnyRoll()
    {
        Set(actor, "<HasDice>k__BackingField", false);
        Assert.IsFalse(game.Begin(actor, player, testCamera));
        Set(actor, "<HasDice>k__BackingField", true);
        Assert.IsTrue(game.Begin(actor, player, testCamera));
        Assert.AreEqual(TwentyOneGame.Phase.ChoosingOrder, game.CurrentPhase);
        Assert.IsFalse(game.Match.Started);
        Assert.IsTrue(Interactables.Busy);
        Assert.IsTrue(Interactables.BlocksMovement);
        Assert.AreEqual(0, game.SelectedOrder);
        game.Tick(10f, true);
        Assert.AreEqual(0, game.Match.Total);
        Assert.IsTrue(game.ChooseOrder(true));
        Assert.AreEqual(TwentyOneMatch.Side.Player, game.Match.Turn);
        Assert.IsFalse(game.ChooseOrder(false));
    }

    [Test]
    public void RefusingLetsThePlayerGoSecond()
    {
        Assert.IsTrue(game.Begin(actor, player, testCamera));
        Assert.IsTrue(game.ChooseOrder(false));
        Assert.AreEqual(TwentyOneMatch.Side.Opponent, game.Match.Turn);
        game.Tick(TwentyOneGame.RulesDuration);
        Assert.AreEqual(TwentyOneGame.Phase.OpponentRolling, game.CurrentPhase);
    }

    [Test]
    public void SpaceStopsTheDisplayedFaceAndCannotStealTheOpponentsTurn()
    {
        Start(true);
        game.Tick(TwentyOneGame.FaceDuration);
        Assert.AreEqual(2, game.Face);
        game.Tick(TwentyOneGame.FaceDuration, true);
        Assert.AreEqual(2, game.Match.LastRoll);
        Assert.AreEqual(2, game.Match.Total);
        game.Tick(0.01f, true);
        Assert.AreEqual(2, game.Match.Total);
        Until(TwentyOneGame.Phase.OpponentRolling);
        game.Tick(0.01f, true);
        Assert.AreEqual(2, game.Match.Total);
        Until(TwentyOneGame.Phase.TakingTiles);
        Assert.AreEqual(4, game.Match.Total);
        Assert.AreEqual(2, game.Match.LastRoll);
    }

    [Test]
    public void EachDieNumberLeavesHalfASecondToReactBeforeCycling()
    {
        Start(true);
        for (int expected = 1; expected <= 3; expected++)
        {
            Assert.AreEqual(expected, game.Face);
            game.Tick(0.5f);
            Assert.AreEqual(expected, game.Face, "The number changed before the reaction window ended.");
            game.Tick(0.11f);
        }
        Assert.AreEqual(1, game.Face, "The die should cycle back to one.");
    }

    [Test]
    public void LosingGivesNoKeywordAndRetryAsksForTurnOrderAgain()
    {
        Play(false);
        Assert.AreEqual(TwentyOneGame.Phase.Lost, game.CurrentPhase);
        Assert.IsFalse(inventory.HasRevealedWord(TwentyOneGame.Keyword));
        game.Tick(0f, false, true);
        Assert.AreEqual(TwentyOneGame.Phase.ChoosingOrder, game.CurrentPhase);
        Assert.IsFalse(game.Match.Started);
        Assert.AreEqual(0, game.Match.Total);
        Assert.IsTrue(game.View.IsVisible);
    }

    [Test]
    public void VictorySaysDratThenHighlightsAndRevealsJerkWithoutRemovingOtherWords()
    {
        inventory.RevealClueWords("FELL");
        Play(true);
        Assert.AreEqual(TwentyOneGame.Phase.Drat, game.CurrentPhase);
        Assert.IsTrue(game.IsSolved);
        Assert.IsFalse(inventory.HasRevealedWord(TwentyOneGame.Keyword));
        game.Tick(0f, false, true);
        Assert.AreEqual(TwentyOneGame.Phase.Drat, game.CurrentPhase, "First press only finishes typing.");
        game.Tick(0f, false, true);
        Assert.AreEqual(TwentyOneGame.Phase.Insult, game.CurrentPhase);
        game.Tick(0.1f);
        Assert.IsFalse(inventory.HasRevealedWord(TwentyOneGame.Keyword));
        game.Tick(10f);
        Assert.AreEqual("You're a JERK!", game.Speech);
        Assert.AreEqual("JERK", game.View.KeywordLabel.text);
        Assert.AreEqual((Color)new Color32(242, 211, 107, 255), game.View.KeywordLabel.color);
        Assert.IsTrue(inventory.HasRevealedWord("JERK"));
        Assert.IsTrue(inventory.HasRevealedWord("FELL"));
        Assert.AreEqual(2, inventory.RevealedWords.Count);
        game.Tick(10f);
        Assert.AreEqual(2, inventory.RevealedWords.Count);
        game.Tick(0f, false, true);
        Assert.IsFalse(TwentyOneGame.IsOpen);
        Assert.IsFalse(Interactables.BlocksMovement);
    }

    [TestCase(0.75f)]
    [TestCase(1f)]
    [TestCase(2f)]
    public void VictoryTextUsesTheSnappedFontWidthAtEachCanvasScale(float scale)
    {
        Play(true);
        game.Tick(0f, false, true);
        game.Tick(0f, false, true);
        game.Tick(10f);
        var keyword = game.View.KeywordLabel;
        var canvas = keyword.canvas;
        canvas.GetComponent<CanvasScaler>().enabled = false;
        canvas.scaleFactor = scale;
        game.Tick(0f);
        var prefix = (PixelText)typeof(TwentyOneView).GetField("prefix", Private).GetValue(game.View);
        var suffix = (PixelText)typeof(TwentyOneView).GetField("suffix", Private).GetValue(game.View);
        Assert.AreEqual("You're a JERK!", prefix.text + keyword.text + suffix.text);
        float pixel = prefix.PixelSize();
        Assert.AreEqual(PixelFont.Measure(prefix.text) * pixel, prefix.rectTransform.rect.width, 0.001f);
        Assert.AreEqual(PixelFont.Measure(keyword.text) * pixel, keyword.rectTransform.rect.width, 0.001f);
        Assert.AreEqual(PixelFont.Measure(suffix.text) * pixel, suffix.rectTransform.rect.width, 0.001f);
        float prefixRight = prefix.rectTransform.anchoredPosition.x + prefix.rectTransform.rect.xMax;
        float wordLeft = keyword.rectTransform.anchoredPosition.x + keyword.rectTransform.rect.xMin;
        float wordRight = keyword.rectTransform.anchoredPosition.x + keyword.rectTransform.rect.xMax;
        float suffixLeft = suffix.rectTransform.anchoredPosition.x + suffix.rectTransform.rect.xMin;
        Assert.AreEqual(PixelFont.Spacing * pixel, wordLeft - prefixRight, 0.001f);
        Assert.AreEqual(PixelFont.Spacing * pixel, suffixLeft - wordRight, 0.001f);
        Assert.AreEqual(TextAnchor.MiddleLeft, prefix.alignment);
        Assert.AreEqual(TextAnchor.MiddleLeft, keyword.alignment);
    }

    [Test]
    public void LeavingDuringVictoryKeepsTheEarnedClue()
    {
        Play(true);
        game.Close();
        Assert.IsTrue(inventory.HasRevealedWord(TwentyOneGame.Keyword));
        Assert.IsFalse(TwentyOneGame.IsOpen);
        Assert.IsFalse(game.View.IsVisible);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void ClosingRestoresTheOriginalCameraEnabledState(bool enabled)
    {
        Assert.IsNotNull(follow);
        follow.enabled = enabled;
        Assert.IsTrue(game.Begin(actor, player, testCamera));
        Assert.IsFalse(follow.enabled);
        game.enabled = false;
        // EditMode fixtures do not receive the Play Mode lifecycle callback.
        Call(game, "OnDisable");
        Assert.IsFalse(TwentyOneGame.IsOpen);
        Assert.AreEqual(enabled, follow.enabled);
        Assert.IsFalse(inventory.HasRevealedWord(TwentyOneGame.Keyword));
    }

    [Test]
    public void BothOrderChoicesHaveMouseHitAreasAndPixelArtDoesNotModifyImportedTextures()
    {
        var source = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/Orca_tile_mono.png");
        FilterMode original = source.texture.filterMode;
        Assert.IsTrue(game.Begin(actor, player, testCamera));
        Canvas.ForceUpdateCanvases();
        Assert.AreEqual(0, game.View.OrderChoiceAt(game.View.OrderChoiceCentre(0)));
        Assert.AreEqual(1, game.View.OrderChoiceAt(game.View.OrderChoiceCentre(1)));
        Assert.AreEqual(-1, game.View.OrderChoiceAt(new Vector2(-10f, -10f)));
        Assert.AreEqual(original, source.texture.filterMode);
        Image[] tiles = (Image[])typeof(TwentyOneView).GetField("tiles", Private).GetValue(game.View);
        Assert.AreEqual(21, tiles.Length);
        Assert.AreEqual(FilterMode.Point, tiles[0].sprite.texture.filterMode);
        Assert.AreNotSame(source.texture, tiles[0].sprite.texture);
    }

    [Test]
    public void ExistingDiceReturnConversationStartsTheConfiguredGameAfterCameraHandoff()
    {
        Set(actor, "diceGame", game);
        Assert.IsTrue(actor.TryTalk());
        actor.Tick(CachudCallout.PanDuration);
        Assert.AreEqual(CachudCallout.Ready, actor.CurrentLine);
        actor.Tick(0f, true);
        actor.Tick(0f, true);
        Assert.IsFalse(TwentyOneGame.IsOpen);
        actor.Tick(CachudCallout.ReturnDuration);
        Assert.IsFalse(CachudCallout.IsOpen);
        Assert.IsTrue(TwentyOneGame.IsOpen);
        Assert.AreEqual(TwentyOneGame.Phase.ChoosingOrder, game.CurrentPhase);
    }

    [Test]
    public void SceneConnectsTheReturnedDiceAndAllFourSuppliedAnimalTiles()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Additive);
        try
        {
            TwentyOneGame found = null;
            CachudCallout npc = null;
            foreach (GameObject sceneRoot in scene.GetRootGameObjects())
                if (sceneRoot.TryGetComponent(out CachudCallout candidate))
                {
                    npc = candidate;
                    found = sceneRoot.GetComponent<TwentyOneGame>();
                }
            Assert.IsNotNull(found);
            Assert.AreSame(found, new SerializedObject(npc).FindProperty("diceGame").objectReferenceValue);
            var art = new SerializedObject(found).FindProperty("tileSprites");
            Assert.AreEqual(4, art.arraySize);
            for (int i = 0; i < animals.Length; i++)
                Assert.AreEqual("Assets/Sprites/" + animals[i] + "_tile_mono.png",
                    AssetDatabase.GetAssetPath(art.GetArrayElementAtIndex(i).objectReferenceValue));
        }
        finally { EditorSceneManager.CloseScene(scene, true); }
    }

    private void Start(bool opponentSecond)
    {
        Assert.IsTrue(game.Begin(actor, player, testCamera));
        Assert.IsTrue(game.ChooseOrder(opponentSecond));
        game.Tick(TwentyOneGame.RulesDuration);
    }

    private void Play(bool playerWins)
    {
        Start(!playerWins);
        for (int i = 0; i < 2000; i++)
        {
            if (game.CurrentPhase == TwentyOneGame.Phase.Lost || game.CurrentPhase == TwentyOneGame.Phase.Drat) return;
            int desired = playerWins ? 4 - game.Match.LastRoll : 1;
            bool stop = game.CurrentPhase == TwentyOneGame.Phase.PlayerRolling && game.Face == desired;
            game.Tick(stop ? 0f : 0.04f, stop);
        }
        Assert.Fail("The game did not end.");
    }

    private void Until(TwentyOneGame.Phase phase)
    {
        for (int i = 0; i < 200 && game.CurrentPhase != phase; i++) game.Tick(0.04f);
        Assert.AreEqual(phase, game.CurrentPhase);
    }

    private static void Set(object target, string name, object value) =>
        target.GetType().GetField(name, Private).SetValue(target, value);

    private static void Call(object target, string name) =>
        target.GetType().GetMethod(name, Private).Invoke(target, null);
}
