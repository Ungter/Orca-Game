using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class CircleMazeTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private GameObject root;
    private ClueMinigame game;
    private InventorySystem inventory;

    [SetUp]
    public void SetUp()
    {
        root = new GameObject("Circle maze test", typeof(RectTransform), typeof(Canvas));
        inventory = root.AddComponent<InventorySystem>();
        typeof(InventorySystem).GetMethod("Awake", Private).Invoke(inventory, null);
        game = root.AddComponent<ClueMinigame>();
        Call("Awake");
    }

    [TearDown]
    public void TearDown() => UnityEngine.Object.DestroyImmediate(root);

    [Test]
    public void PiecesAreCorkedPotionsOfTheirColourWithTransparentCorners()
    {
        Call("EnsureCanvas");
        Call("PaintPotion", 100, 100, new Color32(200, 50, 40, 255));
        Color32[] pixels = Get<Color32[]>("pixels");
        // Art pixels are 3 canvas pixels wide; the flask is 9 x 10 of them.
        foreach (Vector2Int corner in new[] {
            new Vector2Int(101, 101), new Vector2Int(126, 101), new Vector2Int(101, 128) })
            Assert.AreEqual(0, pixels[Index(corner.x, corner.y)].a);
        foreach (Vector2Int liquid in new[] {
            new Vector2Int(107, 122), new Vector2Int(113, 119), new Vector2Int(119, 122), new Vector2Int(113, 125) })
        {
            Color32 color = pixels[Index(liquid.x, liquid.y)];
            Assert.Greater(color.a, 200);
            Assert.Greater(color.r, color.g * 2);
        }
        Color32 cork = pixels[Index(113, 104)];
        Assert.Greater(cork.a, 200);
        Assert.Greater(cork.r, cork.b + 40, "The stopper is cork brown.");
        Color32 ink = pixels[Index(113, 101)];
        Assert.Less(ink.r + ink.g + ink.b, 200, "The flask is outlined in ink.");
    }

    [Test]
    public void DrawingCirclesAndHintSwatchesDoesNotChangeTheMazeRulesOrProgress()
    {
        int[,] before = (int[,])Get<int[,]>("board").Clone();
        int steps = Get<int>("stepsLeft");
        Vector2Int position = Get<Vector2Int>("player");
        game.Show(root.GetComponent<RectTransform>(), null);
        Call("Redraw");
        CollectionAssert.AreEqual(before, Get<int[,]>("board"));
        Assert.AreEqual(steps, Get<int>("stepsLeft"));
        Assert.AreEqual(position, Get<Vector2Int>("player"));
        Texture2D[] swatches = Get<Texture2D[]>("swatchTextures");
        Assert.AreEqual(7, swatches.Length);
        foreach (Texture2D swatch in swatches)
        {
            Color32[] pixels = swatch.GetPixels32();
            Assert.IsTrue(Array.Exists(pixels, p => p.a > 200));
            Assert.IsTrue(Array.Exists(pixels, p => p.a == 0));
        }
        Assert.IsEmpty(inventory.RevealedWords);
    }

    [Test]
    public void SeeingTheSolvedMazeRevealsItsExistingThreeClueWordsOnlyOnce()
    {
        FieldInfo outcome = typeof(ClueMinigame).GetField("outcome", Private);
        outcome.SetValue(game, Enum.Parse(outcome.FieldType, "Won"));
        game.Show(root.GetComponent<RectTransform>(), null);
        Assert.AreEqual(3, inventory.RevealedWords.Count);
        Assert.IsTrue(inventory.HasRevealedWord("Fell"));
        Assert.IsTrue(inventory.HasRevealedWord("Jerk"));
        Assert.IsTrue(inventory.HasRevealedWord("Thief"));
        game.Hide();
        game.Show(root.GetComponent<RectTransform>(), null);
        Assert.AreEqual(3, inventory.RevealedWords.Count);
        Assert.AreEqual(0, inventory.ItemCount(), "Clue words do not consume inventory slots.");
    }

    [Test]
    public void ClosingDuringTheClueWritingCompletesAndRemembersTheRevealedWords()
    {
        game.Show(root.GetComponent<RectTransform>(), null);
        FieldInfo outcome = typeof(ClueMinigame).GetField("outcome", Private);
        outcome.SetValue(game, Enum.Parse(outcome.FieldType, "Won"));
        Call("EnsureClueWriter");
        Get<CursiveWriter>("clueWriter").Play();
        Call("RememberClue");
        Assert.IsEmpty(inventory.RevealedWords);
        game.Hide();
        Assert.AreEqual(3, inventory.RevealedWords.Count);
        Assert.IsFalse(Get<CursiveWriter>("clueWriter").IsPlaying);
    }

    private static int Index(int x, int y) =>
        (int)typeof(ClueMinigame).GetMethod("Index", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { x, y });
    private T Get<T>(string field) => (T)typeof(ClueMinigame).GetField(field, Private).GetValue(game);
    private void Call(string method, params object[] arguments) =>
        typeof(ClueMinigame).GetMethod(method, Private).Invoke(game, arguments);
}
