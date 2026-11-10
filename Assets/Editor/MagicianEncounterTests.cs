using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

public class MagicianEncounterTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private GameObject root;
    private GameObject player;
    private MagicianEncounter encounter;
    private InventorySystem inventory;
    private Random.State randomState;

    [SetUp]
    public void SetUp()
    {
        randomState = Random.state;
        player = new GameObject("Player") { tag = "Player" };
        inventory = player.AddComponent<InventorySystem>();
        typeof(InventorySystem).GetMethod("Awake", PrivateInstance).Invoke(inventory, null);
        root = new GameObject("Magician entrance test");
        encounter = root.AddComponent<MagicianEncounter>();
        typeof(MagicianEncounter).GetMethod("Awake", PrivateInstance).Invoke(encounter, null);
    }

    [TearDown]
    public void TearDown()
    {
        encounter.Cancel();
        Object.DestroyImmediate(root);
        Object.DestroyImmediate(player);
        Random.state = randomState;
    }

    [Test]
    public void DoorwayOnlyOffersTalkWithinRangeAndOnTheSameFloor()
    {
        player.transform.position = new Vector3(2f, 0f, 0f);
        Assert.AreEqual(4f, encounter.InteractDistanceSqr());
        player.transform.position = new Vector3(3.1f, 0f, 0f);
        Assert.AreEqual(float.MaxValue, encounter.InteractDistanceSqr());
        player.transform.position = new Vector3(0f, 4f, 0f);
        Assert.AreEqual(float.MaxValue, encounter.InteractDistanceSqr());
    }

    [Test]
    public void MissingWandConversationReturnsOutsideWithoutStartingTheTrick()
    {
        Vector3 initialPosition = player.transform.position;
        encounter.Begin();
        Assert.IsTrue(MagicianEncounter.IsOpen);
        Assert.IsTrue(Interactables.Busy);
        Assert.IsTrue(Interactables.BlocksMovement);
        RunUntil(MagicianEncounter.Phase.Waiting);
        Assert.AreEqual(MagicianEncounter.Greeting, encounter.CurrentLine);
        Advance();
        Assert.AreEqual(MagicianEncounter.MissingWand, encounter.CurrentLine);
        RunUntil(MagicianEncounter.Phase.Waiting);
        Advance();
        Assert.AreEqual(MagicianEncounter.Phase.LeavingRoom, encounter.CurrentPhase);
        Assert.IsFalse(inventory.Contains(PlayerLantern.ItemName), "The player starts with the lantern; the magician has none to give.");
        Assert.IsFalse(encounter.HasWand);
        Assert.IsFalse(root.GetComponent<ShellMinigame>().IsShowing);
        RunUntil(MagicianEncounter.Phase.Idle);
        Assert.IsFalse(MagicianEncounter.IsOpen);
        Assert.IsFalse(Interactables.Busy);
        Assert.AreEqual(initialPosition, player.transform.position);
    }

    [Test]
    public void EnterDuringTheCinematicDoesNotSkipDialogueAndFirstPressOnlyFinishesTyping()
    {
        encounter.Begin();
        encounter.Tick(0.01f, true);
        Assert.AreEqual(MagicianEncounter.Phase.FadingToRoom, encounter.CurrentPhase);
        encounter.Tick(MagicianEncounter.FadeDuration, true);
        Assert.AreEqual(MagicianEncounter.Phase.EnteringRoom, encounter.CurrentPhase);
        encounter.Tick(MagicianEncounter.RoomEnterDuration, true);
        Assert.AreEqual(MagicianEncounter.Phase.Typing, encounter.CurrentPhase);
        Assert.AreEqual(0, encounter.VisibleCharacters);
        Advance();
        Assert.AreEqual(MagicianEncounter.Phase.Waiting, encounter.CurrentPhase);
        Assert.AreEqual(MagicianEncounter.Greeting, encounter.CurrentLine);
        Advance();
        Assert.AreEqual(MagicianEncounter.MissingWand, encounter.CurrentLine);
    }

    [Test]
    public void ReturningWithWandSkipsTheSeenRequestAndConsumesOnlyTheWand()
    {
        VisitWithoutWand();
        inventory.TryAdd("Other clue");
        inventory.TryAdd(MagicianEncounter.WandItemName, Resources.Load<Sprite>("Magician/MagicWand"));
        inventory.TryAdd("Another clue");
        encounter.Begin();
        Assert.AreEqual(MagicianEncounter.Thanks, encounter.CurrentLine);
        Assert.IsTrue(inventory.Contains(MagicianEncounter.WandItemName));
        RunUntil(MagicianEncounter.Phase.Waiting);
        Assert.AreEqual(MagicianEncounter.Thanks, encounter.CurrentLine);
        Assert.IsTrue(encounter.HasWand);
        Assert.IsFalse(inventory.Contains(MagicianEncounter.WandItemName));
        Assert.AreEqual(2, inventory.ItemCount());
        Assert.AreEqual("Other clue", inventory.GetItem(0));
        Assert.AreEqual("Another clue", inventory.GetItem(1));
        Assert.IsFalse(root.GetComponent<ShellMinigame>().IsShowing);
        RunUntil(MagicianEncounter.Phase.Waiting);
        Advance();
        Assert.AreEqual(MagicianEncounter.Phase.LeavingForTrick, encounter.CurrentPhase);
        RunUntil(MagicianEncounter.Phase.Playing);
        Assert.AreEqual(MagicianEncounter.Phase.Playing, encounter.CurrentPhase);
        Assert.IsTrue(root.GetComponent<ShellMinigame>().IsShowing);
    }

    [Test]
    public void LaterVisitsOnlyAskForATrickAndEscapePreservesTheHandIn()
    {
        GiveWand();
        encounter.Tick(0.01f, false, true);
        RunUntil(MagicianEncounter.Phase.Idle);
        Assert.AreEqual(MagicianEncounter.Phase.Idle, encounter.CurrentPhase);
        Assert.IsFalse(MagicianEncounter.IsOpen);
        Assert.IsTrue(encounter.HasWand);
        encounter.Begin();
        RunUntil(MagicianEncounter.Phase.Waiting);
        Assert.AreEqual(MagicianEncounter.Greeting, encounter.CurrentLine);
        Advance();
        RunUntil(MagicianEncounter.Phase.Playing);
        Assert.AreEqual(MagicianEncounter.Phase.Playing, encounter.CurrentPhase);
        Assert.AreEqual(0, inventory.ItemCount(), "The wand was handed in and nothing else is given.");
    }

    [Test]
    public void EscapingBeforeHandInKeepsTheWandInInventory()
    {
        inventory.TryAdd(MagicianEncounter.WandItemName);
        encounter.Begin();
        RunUntil(MagicianEncounter.Phase.Waiting);
        Advance();
        RunUntil(MagicianEncounter.Phase.Waiting);
        encounter.Tick(0.01f, true, true);
        RunUntil(MagicianEncounter.Phase.Idle);
        Assert.IsTrue(inventory.Contains(MagicianEncounter.WandItemName));
        Assert.IsFalse(encounter.HasWand);
    }

    [TestCase(MagicianEncounter.Phase.FadingToRoom)]
    [TestCase(MagicianEncounter.Phase.EnteringRoom)]
    [TestCase(MagicianEncounter.Phase.Typing)]
    [TestCase(MagicianEncounter.Phase.Waiting)]
    public void EscapeWorksThroughoutTheConversation(MagicianEncounter.Phase phase)
    {
        encounter.Begin();
        RunUntil(phase);
        encounter.Tick(0.01f, false, true);
        RunUntil(MagicianEncounter.Phase.Idle);
        Assert.IsFalse(MagicianEncounter.IsOpen);
        Assert.IsFalse(root.GetComponentInChildren<Canvas>(true).gameObject.activeSelf);
    }

    [Test]
    public void CancellingAnEncounterHidesTheGameAndReleasesInput()
    {
        GiveWand();
        encounter.Cancel();
        Assert.IsFalse(MagicianEncounter.IsOpen);
        Assert.IsFalse(Interactables.Busy);
        Assert.IsFalse(root.GetComponent<ShellMinigame>().IsShowing);
    }

    [Test]
    public void CompletedTrickAutomaticallyReturnsOutside()
    {
        GiveWand();
        typeof(ShellMinigame).GetProperty("ShouldClose").SetValue(root.GetComponent<ShellMinigame>(), true);
        encounter.Tick(0.01f, false);
        RunUntil(MagicianEncounter.Phase.Idle);
        Assert.AreEqual(MagicianEncounter.Phase.Idle, encounter.CurrentPhase);
        Assert.IsTrue(encounter.HasWand);
    }

    [Test]
    public void ConversationUsesSuppliedArtworkAndTheMagicianBobsWhileTalking()
    {
        encounter.Begin();
        RunUntil(MagicianEncounter.Phase.Typing);
        Image magician = FindImage("Magician");
        Assert.AreEqual(Resources.Load<Sprite>("Magician/Magician"), magician.sprite);
        Assert.AreEqual(Resources.Load<Sprite>("Magician/Room"), FindImage("Room").sprite);
        Image bubble = FindImage("Speech bubble");
        Assert.AreEqual(Resources.Load<Sprite>("Magician/SpeechBubble"), bubble.sprite);
        Assert.AreEqual(Image.Type.Sliced, bubble.type);
        float firstY = magician.rectTransform.anchoredPosition.y;
        encounter.Tick(0.1f, false);
        Assert.AreNotEqual(firstY, magician.rectTransform.anchoredPosition.y);
        RunUntil(MagicianEncounter.Phase.Waiting);
        Assert.AreEqual(-4f, magician.rectTransform.anchoredPosition.y);
        Assert.IsFalse(root.GetComponent<ShellMinigame>().Handles(MagicianEncounter.WandItemName));
    }

    [Test]
    public void LongRequestWrapsInsideTheSpeechBubble()
    {
        encounter.Begin();
        RunUntil(MagicianEncounter.Phase.Waiting);
        Advance();
        RunUntil(MagicianEncounter.Phase.Waiting);
        foreach (PixelText text in root.GetComponentsInChildren<PixelText>(true))
        {
            if (text.name != "Dialogue") continue;
            Assert.AreEqual(-6f, text.rectTransform.anchoredPosition.y, "Dialogue sits lower inside the supplied frame.");
            StringAssert.Contains("\n", text.text);
            Assert.LessOrEqual(PixelFont.Measure(text.text) * text.PixelSize(), text.rectTransform.rect.width);
            Assert.LessOrEqual(PixelFont.MeasureHeight(text.text) * text.PixelSize(), text.rectTransform.rect.height);
            return;
        }
        Assert.Fail("Missing dialogue label.");
    }

    [Test]
    public void TrickUsesTheWindowWithoutMovingThePlayerOrCreatingWorldHats()
    {
        Vector3 position = new Vector3(7f, 0f, 128f);
        player.transform.position = position;
        root.transform.position = position;
        GiveWand();
        ShellMinigame game = root.GetComponent<ShellMinigame>();
        Assert.IsFalse(game.IsWorldGame);
        Assert.IsNull(root.GetComponent<WorldHatStage>());
        Assert.IsFalse(FindImage("Room").gameObject.activeInHierarchy, "The room must not draw behind the hat game's own background.");
        Assert.IsTrue(FindGameBackground().gameObject.activeInHierarchy);
        Assert.IsTrue(FindImage("Room backdrop").enabled);
        Assert.IsTrue(encounter.InRoom);
        Assert.AreEqual(position, player.transform.position);
        RectTransform surface = (RectTransform)typeof(ShellMinigame).GetField("surface", PrivateInstance).GetValue(game);
        Assert.AreEqual(root.GetComponentInChildren<Canvas>().transform, surface.parent);
        Assert.IsTrue(surface.gameObject.activeInHierarchy);
        Assert.AreEqual("[ESC] CLOSE", game.Controls);
        Assert.IsFalse(root.GetComponentInChildren<Canvas>().transform.Find("Cinematic stage/Conversation").gameObject.activeSelf);
        encounter.Tick(0.01f, false);
        Assert.AreEqual(position, player.transform.position);
        encounter.Tick(0.01f, false, true);
        RunUntil(MagicianEncounter.Phase.Idle);
        Assert.AreEqual(position, player.transform.position);
        Assert.IsFalse(game.IsShowing);
    }

    [Test]
    public void HatBackgroundFillsTheCanvasAcrossAspectRatiosAndWindowResizes()
    {
        GiveWand();
        ShellMinigame game = root.GetComponent<ShellMinigame>();
        Canvas canvas = root.GetComponentInChildren<Canvas>();
        canvas.GetComponent<CanvasScaler>().enabled = false;
        canvas.renderMode = RenderMode.WorldSpace;
        RectTransform viewport = (RectTransform)canvas.transform;
        RectTransform surface = (RectTransform)typeof(ShellMinigame).GetField("surface", PrivateInstance).GetValue(game);
        Vector2[] sizes =
        {
            new Vector2(1280f, 720f), new Vector2(1024f, 768f),
            new Vector2(2560f, 1080f), new Vector2(720f, 1280f)
        };
        var corners = new Vector3[4];
        foreach (Vector2 size in sizes)
        {
            viewport.sizeDelta = size;
            typeof(MagicianEncounter).GetMethod("LateUpdate", PrivateInstance).Invoke(encounter, null);
            typeof(ShellMinigame).GetMethod("LateUpdate", PrivateInstance).Invoke(game, null);
            RawImage background = FindGameBackground();
            background.rectTransform.GetWorldCorners(corners);
            Vector3 min = viewport.InverseTransformPoint(corners[0]);
            Vector3 max = viewport.InverseTransformPoint(corners[2]);
            Assert.AreEqual(viewport.rect.xMin, min.x, 0.1f, $"Left edge at {size}");
            Assert.AreEqual(viewport.rect.yMin, min.y, 0.1f, $"Bottom edge at {size}");
            Assert.AreEqual(viewport.rect.xMax, max.x, 0.1f, $"Right edge at {size}");
            Assert.AreEqual(viewport.rect.yMax, max.y, 0.1f, $"Top edge at {size}");
            float artAspect = background.texture.width * background.uvRect.width /
                (background.texture.height * background.uvRect.height);
            Assert.AreEqual(background.rectTransform.rect.width / background.rectTransform.rect.height,
                artAspect, 0.001f, "The background fills without distorting the artwork.");
            surface.GetWorldCorners(corners);
            foreach (Vector3 corner in corners)
            {
                Vector3 point = viewport.InverseTransformPoint(corner);
                Assert.That(point.x, Is.InRange(viewport.rect.xMin - 0.1f, viewport.rect.xMax + 0.1f));
                Assert.That(point.y, Is.InRange(viewport.rect.yMin - 0.1f, viewport.rect.yMax + 0.1f));
            }
            Assert.AreEqual(surface.localScale.x, surface.localScale.y, "Do not stretch hats or controls.");
        }
        Assert.Greater(FindImage("Transition").transform.GetSiblingIndex(), surface.GetSiblingIndex(),
            "The full-screen game must remain behind the transition fade.");
    }

    [Test]
    public void HatBackgroundDoesNotOverlapTheRoomDuringTransitionsOrRepeatVisits()
    {
        GiveWand();
        Assert.IsFalse(FindImage("Room").gameObject.activeInHierarchy);
        Assert.IsTrue(FindGameBackground().gameObject.activeInHierarchy);
        encounter.Exit();
        Assert.AreEqual(MagicianEncounter.Phase.LeavingRoom, encounter.CurrentPhase);
        encounter.Tick(MagicianEncounter.FadeDuration * 0.5f, false);
        Assert.IsFalse(FindImage("Room").gameObject.activeInHierarchy);
        Assert.IsTrue(FindGameBackground().gameObject.activeInHierarchy);
        RunUntil(MagicianEncounter.Phase.Idle);
        Assert.IsFalse(FindGameBackground().gameObject.activeInHierarchy);

        encounter.Begin();
        RunUntil(MagicianEncounter.Phase.Waiting);
        Assert.IsTrue(FindImage("Room").gameObject.activeInHierarchy, "The room returns for conversation.");
        Assert.IsFalse(FindGameBackground().gameObject.activeInHierarchy);
        Advance();
        Assert.AreEqual(MagicianEncounter.Phase.LeavingForTrick, encounter.CurrentPhase);
        Assert.IsTrue(FindImage("Room").gameObject.activeInHierarchy);
        RunUntil(MagicianEncounter.Phase.EnteringTrick);
        Assert.IsFalse(FindImage("Room").gameObject.activeInHierarchy, "Switch backgrounds behind the fade.");
        Assert.IsFalse(FindGameBackground().gameObject.activeInHierarchy);
        RunUntil(MagicianEncounter.Phase.Playing);
        Assert.IsFalse(FindImage("Room").gameObject.activeInHierarchy);
        Assert.IsTrue(FindGameBackground().gameObject.activeInHierarchy);
    }

    [Test]
    public void FinishingTheHatGameDoesNotBringBackTheConversationBackgroundBehindTheReward()
    {
        GiveWand();
        ShellMinigame game = root.GetComponent<ShellMinigame>();
        MethodInfo tick = typeof(ShellMinigame).GetMethod("Tick", PrivateInstance);
        for (int round = 0; round < 4; round++)
        {
            for (int frame = 0; frame < 2000 && !game.AcceptsGuess; frame++)
                tick.Invoke(game, new object[] { 0.05f });
            Assert.IsTrue(game.AcceptsGuess);
            int count = game.PantherCount;
            for (int type = 0; type < count; type++)
            {
                int winner = -1;
                for (int i = 0; i < game.HatCount; i++)
                    if (game.GetHatPose(i).panther == type) winner = i;
                Assert.GreaterOrEqual(winner, 0);
                game.GuessHat(winner);
                tick.Invoke(game, new object[] { 0.81f });
            }
            if (round < 3) game.ContinueRound();
        }
        Assert.IsTrue(game.IsSolved);
        typeof(ShellMinigame).GetMethod("UpdateVisuals", PrivateInstance).Invoke(game, null);
        encounter.Tick(0.01f, false);
        Assert.IsTrue(FindImage("Clue letter").gameObject.activeInHierarchy);
        Assert.IsFalse(FindGameBackground().gameObject.activeInHierarchy);
        Assert.IsFalse(FindImage("Room").gameObject.activeInHierarchy);
        Assert.IsTrue(FindImage("Room backdrop").enabled, "Keep the world covered behind the reward.");
    }

    [Test]
    public void WindowRevealWaitsUntilTheTransitionHasFinished()
    {
        inventory.TryAdd(MagicianEncounter.WandItemName);
        encounter.Begin();
        RunUntil(MagicianEncounter.Phase.Waiting);
        Advance();
        RunUntil(MagicianEncounter.Phase.Waiting);
        Advance();
        RunUntil(MagicianEncounter.Phase.Waiting);
        Assert.AreEqual(MagicianEncounter.Thanks, encounter.CurrentLine);
        Advance();
        Assert.AreEqual(MagicianEncounter.Phase.LeavingForTrick, encounter.CurrentPhase, "Straight from the thanks to the trick.");
        Assert.IsFalse(root.GetComponent<ShellMinigame>().IsShowing);
        RunUntil(MagicianEncounter.Phase.EnteringTrick);
        ShellMinigame game = root.GetComponent<ShellMinigame>();
        Assert.IsFalse(game.IsShowing);
        typeof(ShellMinigame).GetMethod("Update", PrivateInstance).Invoke(game, null);
        Assert.AreEqual(0f, (float)typeof(ShellMinigame).GetField("phaseElapsed", PrivateInstance).GetValue(game));
        encounter.Tick(0.01f, false, true);
        RunUntil(MagicianEncounter.Phase.Idle);
        Assert.IsTrue(encounter.HasWand);
        Assert.IsFalse(game.IsShowing);
    }

    [Test]
    public void MagicianCanBeAskedAgainAsOftenAsWantedUntilTheWandIsFound()
    {
        VisitWithoutWand();
        Assert.IsTrue(encounter.HasRequestedWand);
        for (int visit = 0; visit < 2; visit++)
        {
            Assert.AreEqual(0f, encounter.InteractDistanceSqr());
            encounter.Begin();
            Assert.IsTrue(MagicianEncounter.IsOpen);
            RunUntil(MagicianEncounter.Phase.Waiting);
            Assert.AreEqual(MagicianEncounter.Greeting, encounter.CurrentLine);
            Advance();
            Assert.AreEqual(MagicianEncounter.MissingWand, encounter.CurrentLine);
            RunUntil(MagicianEncounter.Phase.Waiting);
            Advance();
            RunUntil(MagicianEncounter.Phase.Idle);
            Assert.IsFalse(MagicianEncounter.IsOpen);
            Assert.IsFalse(encounter.HasWand);
        }
        inventory.TryAdd(MagicianEncounter.WandItemName);
        Assert.AreEqual(0f, encounter.InteractDistanceSqr());
        encounter.Begin();
        Assert.AreEqual(MagicianEncounter.Thanks, encounter.CurrentLine);
    }

    [Test]
    public void DuringThePotionHuntTheMagicianOnlySaysHeIsBusy()
    {
        var hunt = new GameObject("Hunt test");
        try
        {
            ClueMinigame clue = hunt.AddComponent<ClueMinigame>();
            SetHunt(clue, ClueMinigame.HuntState.Playing);
            encounter.Begin();
            Assert.IsFalse(MagicianEncounter.IsOpen, "No visit while the hunt is on.");
            Assert.AreEqual(MagicianEncounter.Phase.Idle, encounter.CurrentPhase);
            Assert.IsFalse(encounter.HasVisited, "Being sent away isn't a visit.");
            Assert.AreEqual(MagicianEncounter.BusyLine, BusyNotice.Showing);

            BusyNotice.Clear();
            SetHunt(clue, ClueMinigame.HuntState.Found);
            encounter.Begin();
            Assert.IsFalse(MagicianEncounter.IsOpen, "Still busy until the potions are handed in.");
            Assert.AreEqual(MagicianEncounter.BusyLine, BusyNotice.Showing);

            BusyNotice.Clear();
            SetHunt(clue, ClueMinigame.HuntState.Delivered);
            encounter.Begin();
            Assert.IsTrue(MagicianEncounter.IsOpen, "Free to talk once the hunt is over.");
            Assert.IsNull(BusyNotice.Showing);
        }
        finally
        {
            typeof(ClueMinigame).GetProperty("Current").SetValue(null, null);
            Object.DestroyImmediate(hunt);
            BusyNotice.Clear();
        }
    }

    private static void SetHunt(ClueMinigame clue, ClueMinigame.HuntState state)
    {
        typeof(ClueMinigame).GetProperty("Current").SetValue(null, clue);
        typeof(ClueMinigame).GetProperty("State").SetValue(clue, state);
    }

    [Test]
    public void TheVisitIsRememberedAndTheRoomCoversTheWorldOnlyWhileInside()
    {
        Assert.IsFalse(encounter.HasVisited);
        Assert.IsFalse(encounter.InRoom);
        encounter.Begin();
        Assert.IsTrue(encounter.HasVisited);
        Assert.IsTrue(encounter.InRoom);
        RunUntil(MagicianEncounter.Phase.Waiting);
        Assert.IsTrue(encounter.InRoom);
        encounter.Tick(0.01f, false, true);
        RunUntil(MagicianEncounter.Phase.Idle);
        Assert.IsFalse(encounter.InRoom);
        Assert.IsTrue(encounter.HasVisited);
    }

    [Test]
    public void AnInterruptedUnseenRequestCanStillBeReadOnTheNextVisit()
    {
        encounter.Begin();
        RunUntil(MagicianEncounter.Phase.Waiting);
        Advance();
        Assert.IsFalse(encounter.HasRequestedWand);
        encounter.Tick(0.01f, false, true);
        RunUntil(MagicianEncounter.Phase.Idle);
        encounter.Begin();
        RunUntil(MagicianEncounter.Phase.Waiting);
        Advance();
        Assert.AreEqual(MagicianEncounter.MissingWand, encounter.CurrentLine);
        Advance();
        Assert.IsTrue(encounter.HasRequestedWand, "Finishing the line with Enter also counts as seeing it.");
    }

    [Test]
    public void ReturningWithWandButEscapingBeforeEnteringTheRoomKeepsIt()
    {
        VisitWithoutWand();
        inventory.TryAdd(MagicianEncounter.WandItemName);
        encounter.Begin();
        encounter.Tick(0.01f, false, true);
        RunUntil(MagicianEncounter.Phase.Idle);
        Assert.IsTrue(inventory.Contains(MagicianEncounter.WandItemName));
        Assert.IsFalse(encounter.HasWand);
        encounter.Begin();
        RunUntil(MagicianEncounter.Phase.Waiting);
        Assert.AreEqual(MagicianEncounter.Thanks, encounter.CurrentLine);
        Assert.IsTrue(encounter.HasWand);
    }

    [Test]
    public void WindowGameBlocksMovementAndReservesEscapeAndOtherInteractions()
    {
        GiveWand();
        Assert.IsTrue(MagicianEncounter.IsOpen);
        Assert.IsTrue(Interactables.Busy);
        Assert.IsTrue(MagicianEncounter.BlocksMovement);
        Assert.IsTrue(Interactables.BlocksMovement);
        Assert.IsFalse(MagicianEncounter.TryGetGameBounds(out _));
        encounter.Tick(0.01f, false, true);
        RunUntil(MagicianEncounter.Phase.Idle);
        Assert.AreEqual(MagicianEncounter.Phase.Idle, encounter.CurrentPhase);
        Assert.IsFalse(Interactables.Busy);
        Assert.IsFalse(Interactables.BlocksMovement);
        Assert.IsFalse(MagicianEncounter.TryGetGameBounds(out _));
    }

    [Test]
    public void WindowGameDoesNotUseAnOutdoorDistanceLimitAndCanBeReopened()
    {
        GiveWand();
        Vector3 outside = root.transform.position + Vector3.right * 100f;
        player.transform.position = outside;
        encounter.Tick(0.01f, false);
        Assert.AreEqual(MagicianEncounter.Phase.Playing, encounter.CurrentPhase);
        Assert.IsTrue(root.GetComponent<ShellMinigame>().IsShowing);
        encounter.Tick(0.01f, false, true);
        RunUntil(MagicianEncounter.Phase.Idle);
        Assert.AreEqual(MagicianEncounter.Phase.Idle, encounter.CurrentPhase);
        Assert.AreEqual(outside, player.transform.position);
        Assert.IsFalse(root.GetComponent<ShellMinigame>().IsShowing);
        Assert.IsFalse(Interactables.BlocksMovement);
        Assert.IsTrue(encounter.HasWand);
        player.transform.position = root.transform.position;
        encounter.Begin();
        RunUntil(MagicianEncounter.Phase.Waiting);
        Assert.AreEqual(MagicianEncounter.Greeting, encounter.CurrentLine);
        Advance();
        RunUntil(MagicianEncounter.Phase.Playing);
        Assert.IsTrue(Interactables.BlocksMovement);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void PlacementUsesTheControllerFootOffsetAndResetsFallingWithoutChangingItsEnabledState(bool enabled)
    {
        PlayerController movement = player.AddComponent<PlayerController>();
        CharacterController controller = player.GetComponent<CharacterController>();
        controller.height = 1.82f;
        controller.center = new Vector3(0f, -0.2f, 0f);
        controller.enabled = enabled;
        player.transform.localScale = new Vector3(1f, 1.5f, 1f);
        typeof(PlayerController).GetField("verticalVelocity", PrivateInstance).SetValue(movement, -20f);
        Vector3 ground = new Vector3(5f, 0.7f, 8f);
        movement.PlaceOnGround(ground);
        Assert.AreEqual(ground.x, player.transform.position.x);
        Assert.AreEqual(ground.z, player.transform.position.z);
        Assert.AreEqual(ground.y + (0.91f + 0.2f) * 1.5f + 0.02f, player.transform.position.y, 0.001f);
        Assert.AreEqual(enabled, controller.enabled);
        Assert.AreEqual(0f, typeof(PlayerController).GetField("verticalVelocity", PrivateInstance).GetValue(movement));
    }

    private Image FindImage(string name)
    {
        foreach (Image image in root.GetComponentsInChildren<Image>(true))
            if (image.name == name) return image;
        Assert.Fail("Missing image: " + name);
        return null;
    }

    private RawImage FindGameBackground()
    {
        foreach (RawImage image in root.GetComponentsInChildren<RawImage>(true))
            if (image.name == "Background") return image;
        Assert.Fail("Missing hat game background.");
        return null;
    }

    private void GiveWand()
    {
        inventory.TryAdd(MagicianEncounter.WandItemName);
        encounter.Begin();
        RunUntil(MagicianEncounter.Phase.Waiting);
        Advance();
        RunUntil(MagicianEncounter.Phase.Waiting);
        Advance();
        RunUntil(MagicianEncounter.Phase.Waiting);
        Advance();
        RunUntil(MagicianEncounter.Phase.Playing);
        Assert.AreEqual(MagicianEncounter.Phase.Playing, encounter.CurrentPhase);
    }

    private void VisitWithoutWand()
    {
        encounter.Begin();
        RunUntil(MagicianEncounter.Phase.Waiting);
        Advance();
        RunUntil(MagicianEncounter.Phase.Waiting);
        Advance();
        RunUntil(MagicianEncounter.Phase.Idle);
    }

    private void Advance() => encounter.Tick(0.01f, true);

    private void RunUntil(MagicianEncounter.Phase phase)
    {
        for (int i = 0; i < 1000 && encounter.CurrentPhase != phase; i++)
            encounter.Tick(1f / 60f, false);
        Assert.AreEqual(phase, encounter.CurrentPhase);
    }
}

public class InventoryHandInTests
{
    private GameObject root;
    private InventorySystem inventory;
    private Sprite sprite;

    [SetUp]
    public void SetUp()
    {
        root = new GameObject("Inventory hand-in test");
        inventory = root.AddComponent<InventorySystem>();
        typeof(InventorySystem).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(inventory, null);
        sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), Vector2.zero);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(root);
        Object.DestroyImmediate(sprite);
    }

    [Test]
    public void RemovingFromFullInventoryKeepsSpritesWithItemsAndFreesASlot()
    {
        inventory.TryAdd("First");
        inventory.TryAdd(MagicianEncounter.WandItemName);
        inventory.TryAdd("Third", sprite);
        inventory.TryAdd("Fourth");
        inventory.TryAdd("Fifth");
        int notifications = 0;
        inventory.Interacted += () => notifications++;
        Assert.IsTrue(inventory.IsFull());
        Assert.IsTrue(inventory.TryRemove(MagicianEncounter.WandItemName));
        Assert.IsFalse(inventory.IsFull());
        Assert.AreEqual(1, notifications);
        Assert.AreEqual("Third", inventory.GetItem(1));
        Assert.AreEqual(sprite, inventory.GetSprite(1));
        Assert.AreEqual("", inventory.GetItem(4));
        Assert.IsNull(inventory.GetSprite(4));
        Assert.IsTrue(inventory.TryAdd("New item"));
        Assert.AreEqual("New item", inventory.GetItem(4));
    }

    [Test]
    public void UnknownItemDoesNotRemoveOrNotifyAndOnlyOneDuplicateIsRemoved()
    {
        int notifications = 0;
        inventory.Interacted += () => notifications++;
        Assert.IsFalse(inventory.Contains(MagicianEncounter.WandItemName));
        Assert.IsFalse(inventory.TryRemove(MagicianEncounter.WandItemName));
        Assert.AreEqual(0, notifications);
        inventory.TryAdd(MagicianEncounter.WandItemName);
        inventory.TryAdd(MagicianEncounter.WandItemName);
        Assert.IsTrue(inventory.TryRemove(MagicianEncounter.WandItemName));
        Assert.AreEqual(1, inventory.ItemCount());
        Assert.IsTrue(inventory.Contains(MagicianEncounter.WandItemName));
        Assert.AreEqual(3, notifications);
    }
}
