using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

public class ShellMinigameTests
{
    private GameObject root;
    private ShellMinigame game;
    private UnityEngine.Random.State randomState;

    [SetUp]
    public void SetUp()
    {
        randomState = UnityEngine.Random.state;
        UnityEngine.Random.InitState(73);
        root = new GameObject("Hat game test", typeof(RectTransform), typeof(Canvas));
        root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        game = root.AddComponent<ShellMinigame>();
        game.Show(root.GetComponent<RectTransform>());
    }

    [TearDown]
    public void TearDown()
    {
        UnityEngine.Object.DestroyImmediate(root);
        UnityEngine.Random.state = randomState;
    }

    [Test]
    public void StartsWithTwoHatsAndOnePanther()
    {
        Assert.AreEqual("Reveal", Phase);
        Assert.AreEqual(2, ActiveCount);
        Assert.IsFalse(game.IsSolved);
        Assert.AreEqual(1, PantherCount());
        Assert.IsFalse(HatImage(2).gameObject.activeSelf);
        Assert.IsFalse(HatImage(3).gameObject.activeSelf);
    }

    [TestCase(1280f, 720f)]
    [TestCase(1024f, 768f)]
    [TestCase(2560f, 1080f)]
    [TestCase(720f, 1280f)]
    public void ShowFillsItsParentImmediatelyWithoutStretchingTheHats(float width, float height)
    {
        RectTransform viewport = new GameObject("Resized viewport", typeof(RectTransform)).GetComponent<RectTransform>();
        viewport.SetParent(root.transform, false);
        viewport.sizeDelta = new Vector2(width, height);
        game.Hide();
        game.Show(viewport);
        RectTransform surface = Get<RectTransform>("surface");
        Assert.AreEqual(viewport, surface.parent, "Reopening must use the requested parent.");
        var corners = new Vector3[4];
        Get<RawImage>("backgroundImage").rectTransform.GetWorldCorners(corners);
        Vector3 min = viewport.InverseTransformPoint(corners[0]);
        Vector3 max = viewport.InverseTransformPoint(corners[2]);
        Assert.AreEqual(-width * 0.5f, min.x, 0.01f);
        Assert.AreEqual(-height * 0.5f, min.y, 0.01f);
        Assert.AreEqual(width * 0.5f, max.x, 0.01f);
        Assert.AreEqual(height * 0.5f, max.y, 0.01f);
        Assert.AreEqual(Mathf.Min(width / 800f, height / 500f), surface.localScale.x, 0.001f);
        Assert.AreEqual(surface.localScale.x, surface.localScale.y);
        Assert.AreEqual(game.Controls, Get<PixelText>("controls").text);
    }

    [Test]
    public void PantherImagesDisplayOnlyOneSquareFrameFromTheVerticalSheet()
    {
        for (int i = 0; i < Hats.Length; i++)
        {
            RawImage panther = HatField<RawImage>(Hats.GetValue(i), "panther");
            Assert.AreEqual(new Rect(0f, 0.5f, 1f, 0.5f), panther.uvRect);
            Vector2 size = panther.rectTransform.sizeDelta;
            float frameAspect = panther.texture.width * panther.uvRect.width /
                (panther.texture.height * panther.uvRect.height);
            Assert.AreEqual(frameAspect, size.x / size.y, 0.001f);
        }
        Call("Tick", 0.4f);
        Call("UpdateVisuals");
        int visible = 0;
        for (int i = 0; i < Hats.Length; i++)
            if (HatField<RawImage>(Hats.GetValue(i), "panther").gameObject.activeSelf) visible++;
        Assert.AreEqual(1, visible);
    }

    [Test]
    public void CorrectGuessesAdvanceThroughFourRoundsAndUnlockKeyword()
    {
        float previousDuration = float.MaxValue;
        int previousMoves = 0;
        int[] counts = { 2, 3, 4, 5 };
        for (int round = 0; round < 4; round++)
        {
            Assert.AreEqual(counts[round], ActiveCount);
            float duration = Property<float>("SwapSeconds");
            int moves = Property<int>("ShuffleMoves");
            Assert.Less(duration, previousDuration);
            Assert.Greater(moves, previousMoves);
            previousDuration = duration;
            previousMoves = moves;
            FinishShuffle();
            MatchAllPanthers();
            Assert.AreEqual(round < 3 ? "RoundWon" : "Won", Phase);
            Assert.AreEqual(round == 3, game.IsSolved);
            Call("Tick", 1f);
            Call("UpdateVisuals");
            if (round < 3) Click(Get<Image>("roundAction").rectTransform);
        }

        Assert.IsTrue(Get<RectTransform>("victory").gameObject.activeSelf);
        Assert.IsFalse(Get<RectTransform>("gameRoot").gameObject.activeSelf);
        Assert.AreEqual("Jerk", Get<string>("victoryKeyword"));
        Assert.AreEqual(Get<CursiveWriter>("keywordWriter").Texture, Get<RawImage>("keywordInk").texture);
        Assert.IsTrue(Get<CursiveWriter>("keywordWriter").IsPlaying);
        Assert.IsEmpty(Get<RectTransform>("victory").GetComponentsInChildren<PixelText>());
        Assert.AreEqual("[ESC] CLOSE", game.Controls);
        Assert.IsFalse(game.ShouldClose);
    }

    [Test]
    public void RevealingTheHatRewardAddsItsWordToTheAnagramBankWithoutDuplicates()
    {
        InventorySystem inventory = root.AddComponent<InventorySystem>();
        typeof(InventorySystem).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(inventory, null);
        Call("BeginRound", 3);
        FinishShuffle();
        MatchAllPanthers();
        Assert.IsEmpty(inventory.RevealedWords);
        Call("Tick", 1f);
        Assert.IsTrue(Get<CursiveWriter>("keywordWriter").IsPlaying);
        Assert.IsEmpty(inventory.RevealedWords);
        game.Hide();
        Assert.IsTrue(inventory.HasRevealedWord("Jerk"));
        Assert.AreEqual(1, inventory.RevealedWords.Count);
        game.Show(root.GetComponent<RectTransform>());
        Assert.AreEqual(1, inventory.RevealedWords.Count);
        Assert.IsFalse(inventory.HasRevealedWord("Fell"));
        Assert.IsFalse(inventory.HasRevealedWord("Thief"));
    }

    [Test]
    public void ExitingBeforeTheRewardAppearsDoesNotRevealItsWord()
    {
        InventorySystem inventory = root.AddComponent<InventorySystem>();
        typeof(InventorySystem).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(inventory, null);
        Call("BeginRound", 3);
        FinishShuffle();
        MatchAllPanthers();
        game.Hide();
        Assert.IsEmpty(inventory.RevealedWords);
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void ShuffleKeepsUniqueSlotsAndEveryPantherIdentity(int round)
    {
        Call("BeginRound", round);
        FinishShuffle();
        Assert.AreEqual(new[] { 1, 1, 2, 2 }[round], PantherCount());
        for (int i = 0; i < ActiveCount; i++)
        {
            object hat = Hats.GetValue(i);
            Assert.AreEqual(HatField<float>(hat, "targetX"), HatField<float>(hat, "x"), 0.001f);
            for (int j = i + 1; j < ActiveCount; j++)
                Assert.AreNotEqual(HatField<float>(hat, "x"), HatField<float>(Hats.GetValue(j), "x"));
        }
    }

    [Test]
    public void WrongGuessRevealsPantherAndRetriesCurrentRound()
    {
        Call("BeginRound", 1);
        FinishShuffle();
        int winner = Winner();
        Call("ChooseHat", (winner + 1) % ActiveCount);
        Assert.AreEqual("Lost", Phase);
        Assert.IsTrue(game.GetHatPose(winner).revealed);
        Call("Tick", 1f);
        Call("UpdateVisuals");
        Click(Get<Image>("roundAction").rectTransform);
        Assert.AreEqual("Reveal", Phase);
        Assert.AreEqual(3, ActiveCount);
        Assert.IsFalse(Get<RectTransform>("victory").gameObject.activeSelf);
        Assert.IsFalse(game.ShouldClose);
    }

    [Test]
    public void CannotGuessBeforeShuffleOrSelectInactiveHat()
    {
        Call("ChooseHat", Winner());
        Assert.AreEqual("Reveal", Phase);
        FinishShuffle();
        Call("ChooseHat", -1);
        Call("ChooseHat", 2);
        Call("ChooseHat", 3);
        Assert.AreEqual("Guess", Phase);
        object winner = Hats.GetValue(Winner());
        Vector2 point = new Vector2(HatField<float>(winner, "x") - 400f, -15f);
        Call("HandleClick", point);
        Assert.AreEqual("RoundWon", Phase);
    }

    [Test]
    public void RevealAndShuffleAnimateHatsWithoutLeavingPantherVisible()
    {
        int winner = Winner();
        Call("Tick", 0.4f);
        Call("UpdateVisuals");
        Assert.Greater(HatImage(winner).rectTransform.anchoredPosition.y, -15f);
        Assert.IsTrue(HatField<RawImage>(Hats.GetValue(winner), "panther").gameObject.activeSelf);
        Call("Tick", 3f);
        Call("Tick", 0.35f);
        Call("UpdateVisuals");
        Assert.AreEqual("Shuffle", Phase);
        Assert.IsFalse(HatField<RawImage>(Hats.GetValue(winner), "panther").gameObject.activeSelf);
        Assert.Greater(Mathf.Abs(HatField<float>(Hats.GetValue(winner), "arc")), 1f);
        Assert.Greater(Mathf.Abs(HatField<float>(Hats.GetValue(winner), "tilt")), 1f);
        FinishShuffle();
        Assert.AreEqual(-15f, HatImage(winner).rectTransform.anchoredPosition.y, 0.001f);
    }

    [TestCase(0.03333333f)]
    [TestCase(0.00694444f)]
    public void FinalRoundFinishesAtDifferentFrameRates(float deltaTime)
    {
        Call("BeginRound", 3);
        for (int i = 0; i < 6000 && Phase != "Guess"; i++) Call("Tick", deltaTime);
        Assert.AreEqual("Guess", Phase);
        Assert.AreEqual(0, Get<int>("movesLeft"));
        Assert.AreEqual(2, PantherCount());
    }

    [Test]
    public void ResultCannotBeSkippedByTheWinningClickOrAnEarlyRetry()
    {
        FinishShuffle();
        Call("ChooseHat", Winner());
        Call("UpdateVisuals");
        Click(Get<Image>("roundAction").rectTransform);
        Assert.AreEqual("RoundWon", Phase);
        Assert.AreEqual(2, ActiveCount);
        Assert.IsFalse(Get<Image>("roundAction").gameObject.activeSelf);
    }

    [Test]
    public void KeywordWritesThenHoldsFadesAndClosesWithoutClicking()
    {
        Call("BeginRound", 3);
        FinishShuffle();
        MatchAllPanthers();
        Call("Tick", 1f);
        Call("UpdateVisuals");
        CursiveWriter writer = Get<CursiveWriter>("keywordWriter");
        for (int i = 0; i < 500 && writer.IsPlaying; i++) Call("Tick", 0.05f);
        Assert.IsFalse(writer.IsPlaying, "The clue writing must finish.");
        Assert.IsFalse(game.ShouldClose, "Writing must not consume the reading time.");
        Call("HandleClick", Vector2.zero);
        Assert.IsTrue(game.IsSolved, "A click cannot restart the solved letter.");
        Call("Tick", 2.9f);
        Call("UpdateVisuals");
        Assert.AreEqual(1f, Get<CanvasGroup>("victoryFade").alpha);
        Assert.IsFalse(game.ShouldClose);
        Call("Tick", 0.4f);
        Call("UpdateVisuals");
        Assert.That(Get<CanvasGroup>("victoryFade").alpha, Is.GreaterThan(0f).And.LessThan(1f));
        Assert.IsFalse(game.ShouldClose);
        Call("Tick", 0.4f);
        Call("UpdateVisuals");
        Assert.IsTrue(game.ShouldClose);
        Assert.AreEqual(0f, Get<CanvasGroup>("victoryFade").alpha);
        Assert.IsTrue(game.IsSolved);
    }

    [Test]
    public void KeywordWaitsForFinalHatReveal()
    {
        Call("BeginRound", 3);
        FinishShuffle();
        MatchAllPanthers();
        Call("Tick", 0.4f);
        Call("UpdateVisuals");
        Assert.IsFalse(Get<CursiveWriter>("keywordWriter").HasStarted);
        Assert.IsFalse(Get<RectTransform>("victory").gameObject.activeSelf);
        Assert.IsTrue(Get<RectTransform>("gameRoot").gameObject.activeSelf);
        Assert.IsFalse(game.ShouldClose);
    }

    [Test]
    public void HidingAndReopeningPreservesRoundAndSolvedReward()
    {
        Call("BeginRound", 3);
        game.Hide();
        Assert.IsFalse(game.IsShowing);
        game.Show(root.GetComponent<RectTransform>());
        Assert.AreEqual(5, ActiveCount);
        FinishShuffle();
        MatchAllPanthers();
        Call("Tick", 1f);
        game.Hide();
        game.Show(root.GetComponent<RectTransform>());
        Assert.IsTrue(game.IsSolved);
        Assert.IsTrue(Get<RectTransform>("victory").gameObject.activeSelf);
        Assert.IsFalse(Get<CursiveWriter>("keywordWriter").IsPlaying);
        Call("Tick", 4f);
        Assert.IsTrue(game.ShouldClose);
        game.Hide();
        game.Show(root.GetComponent<RectTransform>());
        Assert.IsFalse(game.ShouldClose);
        Assert.AreEqual(1f, Get<CanvasGroup>("victoryFade").alpha);
        Assert.IsFalse(Get<RectTransform>("gameRoot").gameObject.activeSelf);
    }

    [Test]
    public void FinishedKeywordClosesInspectionAndRequiresInventoryKeyRelease()
    {
        Call("BeginRound", 3);
        FinishShuffle();
        MatchAllPanthers();
        game.Hide();
        game.Show(root.GetComponent<RectTransform>());
        Call("Tick", 4f);
        Assert.IsTrue(game.ShouldClose);

        InventoryInspection inspection = root.AddComponent<InventoryInspection>();
        ClueMinigame clue = root.GetComponent<ClueMinigame>();
        if (clue == null) clue = root.AddComponent<ClueMinigame>();
        SetInspection(inspection, "inventory", root.GetComponent<InventorySystem>());
        SetInspection(inspection, "minigame", clue);
        SetInspection(inspection, "shellMinigame", game);
        SetInspection(inspection, "canvas", root.GetComponent<Canvas>());
        SetInspection(inspection, "shellPuzzleOpen", true);
        SetInspection(inspection, "inspectedSlot", 1);
        typeof(InventoryInspection).GetProperty("IsOpen").SetValue(null, true);
        typeof(InventoryInspection).GetMethod("Update", PrivateInstance).Invoke(inspection, null);
        Assert.IsFalse(InventoryInspection.IsOpen);
        Assert.IsFalse(root.activeSelf, "The inspection backdrop must disappear with the clue.");
        Assert.IsFalse(game.IsShowing);
        Assert.IsTrue(game.IsSolved);
        Assert.IsTrue((bool)typeof(InventoryInspection).GetField("waitForRelease", PrivateInstance)
            .GetValue(inspection));
    }

    [Test]
    public void MagicianCanReplayASolvedTrickWithoutReplayingLegacyClueInspections()
    {
        Call("BeginRound", 3);
        FinishShuffle();
        MatchAllPanthers();
        game.Hide();
        game.Show(root.GetComponent<RectTransform>());
        Assert.IsTrue(game.IsSolved);
        game.Hide();
        game.Play(root.GetComponent<RectTransform>());
        Assert.IsFalse(game.IsSolved);
        Assert.AreEqual("Reveal", Phase);
        Assert.AreEqual(2, ActiveCount);
        Assert.IsFalse(game.ShouldClose);
        Assert.IsFalse(Get<RectTransform>("victory").gameObject.activeSelf);
        Assert.IsNull(Get<CursiveWriter>("keywordWriter"));
        FinishShuffle();
        Call("ChooseHat", Winner());
        Assert.AreEqual("RoundWon", Phase);
    }

    [Test]
    public void ThirdRoundRequiresBothCorrectIdentitiesNotJustAnyOccupiedHat()
    {
        Call("BeginRound", 2);
        Assert.AreEqual(4, ActiveCount);
        Assert.AreEqual(2, PantherCount());
        StringAssert.Contains("BLUE PANTHER first, then YELLOW PANTHER", game.Instructions);
        Assert.AreEqual(game.Instructions, Get<PixelText>("footer").text);
        Assert.IsFalse(HatImage(4).gameObject.activeSelf);
        FinishShuffle();
        Call("ChooseHat", Winner(1));
        Assert.AreEqual("Lost", Phase, "Yellow Panther cannot stand in for Blue Panther.");
        Call("Tick", 1f);
        game.ContinueRound();
        Assert.AreEqual("Reveal", Phase);
        Assert.AreEqual(0, game.RequestedPanther);
        Assert.AreEqual(4, ActiveCount);
        FinishShuffle();
        int first = Winner(0);
        Call("ChooseHat", first);
        Assert.AreEqual("Found", Phase);
        Call("ChooseHat", Winner(1));
        Assert.AreEqual(0, game.RequestedPanther, "Feedback must finish before the next guess.");
        Call("Tick", 0.81f);
        Assert.AreEqual(1, game.RequestedPanther);
        Assert.AreEqual(1f, game.GetHatPose(first).lift);
        Call("ChooseHat", first);
        Assert.AreEqual("Guess", Phase, "A previously found hat cannot be guessed twice.");
        Call("ChooseHat", Winner(1));
        Assert.AreEqual("RoundWon", Phase, "The inserted third round leads to the five-hat finale.");
        Assert.IsFalse(game.IsSolved);
        Call("Tick", 0.81f);
        game.ContinueRound();
        Assert.AreEqual("ROUND 4", game.RoundTitle);
        Assert.AreEqual(5, ActiveCount);
    }

    [Test]
    public void FinalRoundPutsTwoDistinctPanthersUnderFiveHats()
    {
        Call("BeginRound", 3);
        Assert.AreEqual(5, ActiveCount);
        Assert.AreEqual(5, Hats.Length, "The five-hat finale follows the inserted four-hat round.");
        Assert.AreEqual(2, PantherCount());
        var textures = new System.Collections.Generic.HashSet<Texture2D>();
        for (int type = 0; type < 2; type++)
        {
            int index = Winner(type);
            Assert.AreEqual(type, game.GetHatPose(index).panther);
            Assert.IsNotNull(ShellMinigame.PantherTexture(type));
            textures.Add(ShellMinigame.PantherTexture(type));
        }
        Assert.AreEqual(2, textures.Count);
        Call("Tick", 0.4f);
        for (int i = 0; i < 5; i++)
            Assert.AreEqual(game.GetHatPose(i).panther >= 0, game.GetHatPose(i).showPanther);
        FinishShuffle();
        for (int i = 0; i < 5; i++) Assert.IsFalse(game.GetHatPose(i).showPanther);
        MatchAllPanthers();
        Assert.IsTrue(game.IsSolved);
    }

    [TestCase(0.5f)]
    [TestCase(1f)]
    [TestCase(1.5f)]
    [TestCase(2f)]
    public void PanthersHaveNoIndividualLabelsButThePromptStillIdentifiesTheRequestedColor(float canvasScale)
    {
        root.GetComponent<Canvas>().scaleFactor = canvasScale;
        Call("BeginRound", 3);
        Call("Tick", 0.4f);
        Call("UpdateVisuals");
        string[] names = { "BLUE PANTHER", "YELLOW PANTHER" };
        string[] resources = { "Azure", "Amber" };
        for (int type = 0; type < names.Length; type++)
        {
            Assert.AreEqual(names[type], ShellMinigame.PantherName(type));
            Assert.AreEqual(Resources.Load<Texture2D>("ShellGame/Panthers/" + resources[type]),
                ShellMinigame.PantherTexture(type));
            Assert.IsTrue(game.GetHatPose(Winner(type)).showPanther);
        }
        foreach (PixelText label in root.GetComponentsInChildren<PixelText>(true))
            Assert.AreNotEqual("Panther name", label.name);
        StringAssert.DoesNotContain("number", game.Message.ToLowerInvariant());
        StringAssert.DoesNotContain("number", game.Instructions.ToLowerInvariant());
        FinishShuffle();
        StringAssert.Contains("Find BLUE PANTHER", game.Message);
        StringAssert.DoesNotContain("#", game.Message);
        StringAssert.Contains("Click", game.Instructions);
        StringAssert.Contains("[CLICK]", game.Controls);
        game.GuessHat(Winner(0));
        Call("Tick", 0.81f);
        StringAssert.Contains("Find YELLOW PANTHER", game.Message);
        game.GuessHat(Winner(1));
        Assert.IsTrue(game.IsSolved);
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void RoundHeadingOnlyShowsTheCurrentRound(int round)
    {
        Call("BeginRound", round);
        Assert.AreEqual($"ROUND {round + 1}", game.RoundTitle);
        Assert.AreEqual(game.RoundTitle, Get<PixelText>("roundLabel").text);
    }

    [Test]
    public void EveryLossShowsOnlyOneTauntAndStillRetriesTheSameRound()
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            FinishShuffle();
            game.GuessHat((Winner() + 1) % ActiveCount);
            Assert.AreEqual("Lost", Phase);
            Assert.AreEqual("OUTPLAYED BY A HAT.", game.Message);
            Assert.AreEqual("", game.Instructions);
            Call("Tick", 0.81f);
            Call("UpdateVisuals");
            Assert.IsTrue(game.CanContinue);
            var shown = new System.Collections.Generic.List<string>();
            foreach (PixelText label in root.GetComponentsInChildren<PixelText>())
                if (!string.IsNullOrEmpty(label.text)) shown.Add(label.text);
            CollectionAssert.AreEquivalent(
                new[] { "THE PANTHER'S HATS", "ROUND 1", "OUTPLAYED BY A HAT.", game.Controls, "RETRY ROUND" },
                shown, "The taunt is the only sentence on the loss screen.");
            Assert.LessOrEqual(PixelFont.Measure(game.Message) * Get<PixelText>("message").PixelSize(),
                Get<PixelText>("message").rectTransform.rect.width);
            game.ContinueRound();
            Assert.AreEqual("Reveal", Phase);
            Assert.AreEqual("ROUND 1", game.RoundTitle);
        }
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void EveryHatChangesToRandomColorsDuringEverySwapButKeepsItsPanther(int round)
    {
        Call("BeginRound", round);
        int[] identities = new int[ActiveCount];
        for (int i = 0; i < ActiveCount; i++)
        {
            identities[i] = game.GetHatPose(i).panther;
            Assert.AreEqual(Color.white, HatImage(i).color);
        }
        Call("Tick", Property<float>("RevealSeconds") + 0.01f);
        for (int swap = 0; swap < Property<int>("ShuffleMoves"); swap++)
        {
            Assert.AreEqual("Shuffle", Phase);
            Color[] targets = new Color[ActiveCount];
            Color[] starts = new Color[ActiveCount];
            for (int i = 0; i < ActiveCount; i++)
            {
                starts[i] = game.GetHatPose(i).color;
                targets[i] = HatField<Color>(Hats.GetValue(i), "targetColor");
                Assert.AreNotEqual(starts[i], targets[i], "Each swap picks a new color for every hat.");
                Assert.AreEqual(1f, targets[i].a);
            }
            Call("Tick", 0.08f + Property<float>("SwapSeconds") * 0.5f);
            Call("UpdateVisuals");
            for (int i = 0; i < ActiveCount; i++)
            {
                Assert.AreNotEqual(starts[i], HatImage(i).color);
                Assert.AreNotEqual(targets[i], HatImage(i).color, "Colors animate during movement.");
                Assert.AreEqual(game.GetHatPose(i).color, HatImage(i).color);
                Assert.IsFalse(game.GetHatPose(i).showPanther);
                Assert.AreEqual(identities[i], game.GetHatPose(i).panther);
            }
            Call("Tick", Property<float>("SwapSeconds") * 0.5f + 0.001f);
            Call("UpdateVisuals");
            for (int i = 0; i < ActiveCount; i++) Assert.AreEqual(targets[i], HatImage(i).color);
        }
        Assert.AreEqual("Guess", Phase);
        Color finalColor = HatImage(0).color;
        Call("Tick", 1f);
        Call("UpdateVisuals");
        Assert.AreEqual(finalColor, HatImage(0).color, "Colors stop changing when it is time to guess.");
        MatchAllPanthers();
        Assert.AreEqual(round == 3 ? "Won" : "RoundWon", Phase);
        Call("BeginRound", round);
        for (int i = 0; i < ActiveCount; i++) Assert.AreEqual(Color.white, HatImage(i).color);
    }

    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private Array Hats => Get<Array>("hats");
    private int ActiveCount => Property<int>("ActiveHatCount");
    private string Phase => Get<object>("phase").ToString();

    private T Get<T>(string name) =>
        (T)typeof(ShellMinigame).GetField(name, PrivateInstance).GetValue(game);

    private T Property<T>(string name) =>
        (T)typeof(ShellMinigame).GetProperty(name, PrivateInstance).GetValue(game);

    private static T HatField<T>(object hat, string name) =>
        (T)hat.GetType().GetField(name).GetValue(hat);

    private void Call(string name, params object[] arguments) =>
        typeof(ShellMinigame).GetMethod(name, PrivateInstance).Invoke(game, arguments);

    private static void SetInspection(InventoryInspection inspection, string name, object value) =>
        typeof(InventoryInspection).GetField(name, PrivateInstance).SetValue(inspection, value);

    private RawImage HatImage(int index) => HatField<RawImage>(Hats.GetValue(index), "image");

    private int Winner(int type = -1)
    {
        if (type < 0) type = game.RequestedPanther;
        for (int i = 0; i < ActiveCount; i++)
            if (game.GetHatPose(i).panther == type) return i;
        Assert.Fail("No panther in the active hats.");
        return -1;
    }

    private int PantherCount()
    {
        int count = 0;
        for (int i = 0; i < ActiveCount; i++)
            if (game.GetHatPose(i).panther >= 0) count++;
        return count;
    }

    private void MatchAllPanthers()
    {
        int count = game.PantherCount;
        for (int i = 0; i < count; i++)
        {
            Assert.AreEqual(i, game.RequestedPanther);
            Call("ChooseHat", Winner());
            if (i + 1 == count) continue;
            Assert.AreEqual("Found", Phase);
            Call("Tick", 0.81f);
            Assert.AreEqual("Guess", Phase);
        }
    }

    private void FinishShuffle()
    {
        for (int i = 0; i < 2000 && Phase != "Guess"; i++) Call("Tick", 0.05f);
        Assert.AreEqual("Guess", Phase, "The shuffle must finish and accept a guess.");
        Call("UpdateVisuals");
    }

    private void Click(RectTransform rect)
    {
        Vector3 point = Get<RectTransform>("surface").InverseTransformPoint(rect.TransformPoint(Vector3.zero));
        Call("HandleClick", new Vector2(point.x, point.y));
    }
}
