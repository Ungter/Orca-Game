using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public class GameplayDebugMenuTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private Scene scene;
    private GameObject root;
    private PlayerController player;
    private GameplayDebugMenu menu;
    private InventorySystem inventory;
    private InventorySystem previousInventory;
    private ClueMinigame previousHunt;
    private ClueMinigame hunt;
    private ShellMinigame hats;
    private TwentyOneGame twentyOne;
    private DigSite dig;
    private DolphinWordPuzzle anagram;
    private LighthouseStacker lighthouse;
    private CursorLockMode originalLock;
    private bool originalVisible;

    [SetUp]
    public void SetUp()
    {
        scene = EditorSceneManager.NewPreviewScene();
        root = new GameObject("Debug tools test");
        SceneManager.MoveGameObjectToScene(root, scene);
        previousInventory = InventorySystem.instance;
        previousHunt = ClueMinigame.Current;
        originalLock = Cursor.lockState;
        originalVisible = Cursor.visible;
        inventory = root.AddComponent<InventorySystem>();
        Call(inventory, "Awake");
        hunt = root.AddComponent<ClueMinigame>();
        Call(hunt, "Awake");
        Call(hunt, "OnEnable");
        hats = root.AddComponent<ShellMinigame>();
        twentyOne = root.AddComponent<TwentyOneGame>();
        dig = root.AddComponent<DigSite>();
        anagram = root.AddComponent<DolphinWordPuzzle>();
        lighthouse = root.AddComponent<LighthouseStacker>();
        var visitor = new GameObject("Debug player");
        visitor.transform.SetParent(root.transform);
        visitor.AddComponent<SpriteRenderer>();
        player = visitor.AddComponent<PlayerController>();
        Call(player, "Awake");
        menu = visitor.AddComponent<GameplayDebugMenu>();
        Call(menu, "Awake");
    }

    [TearDown]
    public void TearDown()
    {
        menu.SetOpen(false);
        twentyOne.Close();
        anagram.Close();
        lighthouse.Close();
        Call(menu, "OnDisable");
        Call(hunt, "OnDestroy");
        EditorSceneManager.ClosePreviewScene(scene);
        InventorySystem.instance = previousInventory;
        typeof(ClueMinigame).GetProperty("Current").SetValue(null, previousHunt);
        Cursor.lockState = originalLock;
        Cursor.visible = originalVisible;
        Assert.IsFalse(GameplayDebugMenu.IsOpen);
    }

    [Test]
    public void WindowAndSpeedHackAreIndependentTogglesAndRestoreControllerState()
    {
        float timeScale = Time.timeScale;
        Assert.IsFalse(player.DebugFlyEnabled);
        menu.SetOpen(true);
        Assert.IsTrue(GameplayDebugMenu.IsOpen);
        Assert.IsTrue(Interactables.BlocksMovement);
        Assert.IsFalse(player.DebugFlyEnabled, "Opening the menu must not enable the speed hack.");
        Assert.AreEqual(timeScale, Time.timeScale);
        Assert.IsTrue(menu.SetSpeedHack(true));
        Assert.IsFalse(player.GetComponent<CharacterController>().enabled);
        menu.SetOpen(false);
        Assert.IsFalse(GameplayDebugMenu.IsOpen);
        Assert.IsTrue(player.DebugFlyEnabled, "The speed toggle stays on when the menu closes.");
        Assert.IsTrue(menu.SetSpeedHack(false));
        Assert.IsTrue(player.GetComponent<CharacterController>().enabled);
        player.GetComponent<CharacterController>().enabled = false;
        Assert.IsTrue(menu.SetSpeedHack(true));
        Assert.IsTrue(menu.SetSpeedHack(false));
        Assert.IsFalse(player.GetComponent<CharacterController>().enabled, "Do not enable someone else's controller.");
    }

    [Test]
    public void EveryMinigameCanBeCompletedBeforeStartingAndRepeatedWithoutDuplicateRewards()
    {
        inventory.TryAdd("Keep this item");
        int wins = 0;
        lighthouse.Won += () => wins++;
        menu.SetOpen(true);
        for (int repeat = 0; repeat < 2; repeat++)
            foreach (GameplayDebugMenu.Minigame choice in Enum.GetValues(typeof(GameplayDebugMenu.Minigame)))
                Assert.IsTrue(menu.CompleteMinigame(choice), choice.ToString());
        Assert.IsTrue(hunt.IsDelivered);
        Assert.IsFalse(ClueMinigame.HuntUnderway);
        Assert.IsTrue(hats.IsSolved);
        Assert.IsTrue(twentyOne.IsSolved);
        Assert.AreEqual(TwentyOneMatch.Side.Player, twentyOne.Match.Winner);
        Assert.IsTrue(dig.IsDug);
        Assert.IsTrue(anagram.IsSolved);
        Assert.AreEqual("JEFFTHEKILLER", anagram.ArrangedLetters);
        Assert.IsTrue(lighthouse.IsUnlocked);
        Assert.IsTrue(lighthouse.HasWon);
        Assert.AreEqual(1, wins);
        Assert.IsTrue(inventory.Contains("Keep this item"));
        Assert.AreEqual(1, inventory.ItemCount());
        CollectionAssert.AreEquivalent(new[] { "FELL", "JERK" }, inventory.RevealedWords);
        Assert.IsTrue(GameplayDebugMenu.IsOpen);
        Assert.IsTrue(Cursor.visible);
        menu.SetOpen(false);
        Assert.IsFalse(Interactables.Busy);
    }

    [Test]
    public void PartialAnagramFinishesAndClosesWithoutErasingOtherClues()
    {
        inventory.RevealClueWords("Fell, Jerk, Thief");
        anagram.Begin();
        Assert.IsTrue(anagram.BeginDrag(false, 4));
        Assert.IsTrue(anagram.DropTile(true, 0));
        Assert.IsFalse(anagram.IsSolved);
        menu.SetOpen(true);
        Assert.IsTrue(menu.CompleteMinigame(GameplayDebugMenu.Minigame.Anagram));
        Assert.IsTrue(anagram.IsSolved);
        Assert.IsFalse(DolphinWordPuzzle.IsOpen);
        Assert.AreEqual(3, inventory.RevealedWords.Count);
    }

    [Test]
    public void ActiveTwentyOneCompletionRestoresCameraAndTheCorrectCursorAfterTheMenuCloses()
    {
        var actorObject = new GameObject("Debug Cachud");
        actorObject.transform.SetParent(root.transform);
        var actor = actorObject.AddComponent<CachudCallout>();
        actor.GetComponent<SpriteRenderer>().sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/al_cachud.png");
        typeof(CachudCallout).GetField("<HasDice>k__BackingField", Private).SetValue(actor, true);
        var sprites = new Sprite[4];
        string[] names = { "Orca", "Panther", "Gazelle", "Iguana" };
        for (int i = 0; i < sprites.Length; i++)
            sprites[i] = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/" + names[i] + "_tile_mono.png");
        typeof(TwentyOneGame).GetField("tileSprites", Private).SetValue(twentyOne, sprites);
        var cameraObject = new GameObject("Debug camera");
        cameraObject.transform.SetParent(root.transform);
        var camera = cameraObject.AddComponent<Camera>();
        var follow = cameraObject.AddComponent<CameraFollow>();
        camera.transform.position = new Vector3(4f, 5f, 6f);
        camera.fieldOfView = 68f;
        Cursor.visible = false;
        bool visibleBeforeGame = Cursor.visible;
        CursorLockMode lockBeforeGame = Cursor.lockState;
        Assert.IsTrue(twentyOne.Begin(actor, player.transform, camera));
        twentyOne.ChooseOrder(true);
        twentyOne.Tick(TwentyOneGame.RulesDuration);
        twentyOne.Tick(0f, true);
        Assert.AreEqual(TwentyOneGame.Phase.Throwing, twentyOne.CurrentPhase);
        menu.SetOpen(true);
        Assert.IsTrue(menu.CompleteMinigame(GameplayDebugMenu.Minigame.TwentyOne));
        Assert.IsFalse(TwentyOneGame.IsOpen);
        Assert.IsTrue(twentyOne.IsSolved);
        Assert.IsTrue(inventory.HasRevealedWord("JERK"));
        Assert.IsTrue(follow.enabled);
        Assert.AreEqual(new Vector3(4f, 5f, 6f), camera.transform.position);
        Assert.AreEqual(68f, camera.fieldOfView);
        Assert.IsTrue(Cursor.visible);
        menu.SetOpen(false);
        Assert.AreEqual(visibleBeforeGame, Cursor.visible);
        Assert.AreEqual(lockBeforeGame, Cursor.lockState);
    }

    [Test]
    public void CompletingAClosedOrLostTwentyOneMatchStillAwardsThePlayerWin()
    {
        twentyOne.Match.Start((twentyOne.Match.Limit - 1) % 2 == 0);
        while (!twentyOne.Match.Finished) twentyOne.Match.Take(twentyOne.Match.Turn, 1);
        Assert.AreEqual(TwentyOneMatch.Side.Player, twentyOne.Match.Loser);
        Assert.IsTrue(menu.CompleteMinigame(GameplayDebugMenu.Minigame.TwentyOne));
        Assert.AreEqual(TwentyOneMatch.Side.Player, twentyOne.Match.Winner);
        int turns = twentyOne.Match.TurnCount;
        Assert.IsTrue(menu.CompleteMinigame(GameplayDebugMenu.Minigame.TwentyOne));
        Assert.AreEqual(turns, twentyOne.Match.TurnCount);
    }

    [Test]
    public void DisablingTheMenuReleasesItsCursorAndSpeedHack()
    {
        menu.SetOpen(true);
        menu.SetSpeedHack(true);
        menu.enabled = false;
        Call(menu, "OnDisable");
        Assert.IsFalse(GameplayDebugMenu.IsOpen);
        Assert.IsFalse(player.DebugFlyEnabled);
        Assert.IsTrue(player.GetComponent<CharacterController>().enabled);
    }

    [Test]
    public void PartlyPlayedHatsFinishAndReleaseTheirView()
    {
        var canvas = new GameObject("Hat viewport", typeof(RectTransform), typeof(Canvas));
        canvas.transform.SetParent(root.transform);
        hats.Show((RectTransform)canvas.transform);
        for (int i = 0; i < 40 && !hats.AcceptsGuess; i++) Call(hats, "Tick", 1f);
        Assert.IsTrue(hats.AcceptsGuess);
        for (int i = 0; i < hats.HatCount; i++)
            if (hats.GetHatPose(i).panther == hats.RequestedPanther) { hats.GuessHat(i); break; }
        Assert.IsFalse(hats.IsSolved, "Only the first round has been cleared.");
        menu.SetOpen(true);
        Assert.IsTrue(menu.CompleteMinigame(GameplayDebugMenu.Minigame.Hats));
        Assert.IsTrue(hats.IsSolved);
        Assert.IsFalse(hats.IsShowing);
        Assert.IsTrue(inventory.HasRevealedWord("JERK"));
    }

    [Test]
    public void AFailedLighthouseRunCanBeCompletedAndRestoresTheCamera()
    {
        anagram.DebugComplete();
        var tower = new GameObject("Test tower").transform;
        tower.SetParent(root.transform);
        lighthouse.Configure(tower, tower, tower, tower);
        var cameraObject = new GameObject("Stacker camera");
        cameraObject.transform.SetParent(root.transform);
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.transform.position = new Vector3(7f, 8f, 9f);
        Assert.IsTrue(lighthouse.Begin(player.transform, camera));
        lighthouse.Tick(0f, true);
        Assert.IsTrue(lighthouse.Match.Failed);
        menu.SetOpen(true);
        Assert.IsTrue(menu.CompleteMinigame(GameplayDebugMenu.Minigame.Lighthouse));
        Assert.IsTrue(lighthouse.HasWon);
        Assert.IsFalse(LighthouseStacker.IsOpen);
        Assert.AreEqual(new Vector3(7f, 8f, 9f), camera.transform.position);
    }

    [Test]
    public void AnActiveDigFinishesTheChecksAndUncoversItsNormalReward()
    {
        var rewardObject = new GameObject("Buried reward", typeof(SpriteRenderer));
        rewardObject.transform.SetParent(root.transform);
        PickupItem reward = rewardObject.AddComponent<PickupItem>();
        Call(reward, "Awake");
        reward.Bury();
        typeof(DigSite).GetField("reward", Private).SetValue(dig, reward);
        dig.Begin();
        dig.Tick(DigSite.AppearDuration, false);
        Assert.AreEqual(DigSite.Phase.Sweep, dig.CurrentPhase);
        Assert.IsFalse(rewardObject.GetComponent<SpriteRenderer>().enabled);
        menu.SetOpen(true);
        Assert.IsTrue(menu.CompleteMinigame(GameplayDebugMenu.Minigame.Digging));
        Assert.IsTrue(dig.IsDug);
        Assert.IsFalse(DigSite.IsDigging);
        Assert.IsTrue(rewardObject.GetComponent<SpriteRenderer>().enabled);
        Assert.Less(reward.transform.position.y, 0f, "The normal reward rise should have begun.");
        Assert.AreEqual(0, inventory.ItemCount(), "The reward still uses normal pickup, never deleting a full bag's contents.");
    }

    [Test]
    public void QuestOwnersCompleteWithoutDiscardingItemsWhenTheInventoryIsFull()
    {
        var alchemist = root.AddComponent<AlchemistEncounter>();
        var magician = root.AddComponent<MagicianEncounter>();
        for (int i = 0; i < inventory.SlotCount(); i++) inventory.TryAdd("Keep " + i);
        menu.SetOpen(true);
        Assert.IsTrue(menu.CompleteMinigame(GameplayDebugMenu.Minigame.PotionHunt));
        Assert.IsTrue(alchemist.HasHeardIntroduction);
        Assert.IsTrue(hunt.IsDelivered);
        Assert.IsTrue(menu.CompleteMinigame(GameplayDebugMenu.Minigame.Hats));
        Assert.IsTrue(magician.HasWand);
        Assert.IsTrue(hats.IsSolved);
        Assert.AreEqual(inventory.SlotCount(), inventory.ItemCount());
        for (int i = 0; i < inventory.ItemCount(); i++) Assert.AreEqual("Keep " + i, inventory.GetItem(i));
    }

    private static void Call(object component, string method, params object[] args) =>
        component.GetType().GetMethod(method, Private).Invoke(component, args);
}
