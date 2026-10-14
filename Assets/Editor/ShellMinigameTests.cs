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
    public void CorrectGuessesAdvanceThroughThreeRoundsAndUnlockKeyword()
    {
        float previousDuration = float.MaxValue;
        int previousMoves = 0;
        for (int round = 0; round < 3; round++)
        {
            Assert.AreEqual(round + 2, ActiveCount);
            float duration = Property<float>("SwapSeconds");
            int moves = Property<int>("ShuffleMoves");
            Assert.Less(duration, previousDuration);
            Assert.Greater(moves, previousMoves);
            previousDuration = duration;
            previousMoves = moves;
            FinishShuffle();
            Call("ChooseHat", Winner());
            Assert.AreEqual(round < 2 ? "RoundWon" : "Won", Phase);
            Assert.AreEqual(round == 2, game.IsSolved);
            Call("Tick", 1f);
            Call("UpdateVisuals");
            if (round < 2) Click(Get<Image>("roundAction").rectTransform);
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

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void ShuffleKeepsUniqueSlotsAndExactlyOnePanther(int round)
    {
        Call("BeginRound", round);
        FinishShuffle();
        Assert.AreEqual(1, PantherCount());
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
        Assert.IsTrue(HatField<bool>(Hats.GetValue(winner), "revealed"));
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
        Call("BeginRound", 2);
        for (int i = 0; i < 3000 && Phase != "Guess"; i++) Call("Tick", deltaTime);
        Assert.AreEqual("Guess", Phase);
        Assert.AreEqual(0, Get<int>("movesLeft"));
        Assert.AreEqual(1, PantherCount());
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
        Call("BeginRound", 2);
        FinishShuffle();
        Call("ChooseHat", Winner());
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
        Call("BeginRound", 2);
        FinishShuffle();
        Call("ChooseHat", Winner());
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
        Call("BeginRound", 2);
        game.Hide();
        Assert.IsFalse(game.IsShowing);
        game.Show(root.GetComponent<RectTransform>());
        Assert.AreEqual(4, ActiveCount);
        FinishShuffle();
        Call("ChooseHat", Winner());
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
        Call("BeginRound", 2);
        FinishShuffle();
        Call("ChooseHat", Winner());
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
        SetInspection(inspection, "puzzleOpen", true);
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

    private int Winner()
    {
        for (int i = 0; i < ActiveCount; i++)
            if (HatField<bool>(Hats.GetValue(i), "hasPanther")) return i;
        Assert.Fail("No panther in the active hats.");
        return -1;
    }

    private int PantherCount()
    {
        int count = 0;
        for (int i = 0; i < ActiveCount; i++)
            if (HatField<bool>(Hats.GetValue(i), "hasPanther")) count++;
        return count;
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
