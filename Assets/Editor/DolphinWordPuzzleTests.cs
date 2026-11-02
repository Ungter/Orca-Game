using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public class DolphinWordPuzzleTests
{
    private GameObject root;
    private DolphinWordPuzzle puzzle;
    private InventorySystem inventory;

    [SetUp]
    public void SetUp()
    {
        root = new GameObject("Dolphin puzzle test");
        inventory = root.AddComponent<InventorySystem>();
        typeof(InventorySystem).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(inventory, null);
        puzzle = root.AddComponent<DolphinWordPuzzle>();
    }

    [TearDown]
    public void TearDown()
    {
        // Edit-mode destruction does not call the component's play-mode OnDisable.
        puzzle.Close();
        Object.DestroyImmediate(root);
        Assert.IsFalse(DolphinWordPuzzle.IsOpen);
    }

    [Test]
    public void PuzzleLocksControlsUntilClosedAndKeepsProgress()
    {
        RevealAll();
        Assert.IsFalse(DolphinWordPuzzle.IsOpen);
        puzzle.Begin();
        Assert.IsTrue(Interactables.Busy);
        Move(false, 4, true, 0); // J
        Assert.AreEqual('J', puzzle.ArrangedLetters[0]);
        Assert.IsFalse(puzzle.IsSolved);
        puzzle.Close();
        Assert.IsFalse(Interactables.Busy);
        puzzle.Begin();
        Assert.AreEqual('J', puzzle.ArrangedLetters[0]);
    }

    [Test]
    public void OnlyTheExactSentenceWins()
    {
        RevealAll();
        puzzle.Begin();
        const string answer = "JEFFTHEKILLER";
        // Put each letter in a slot. Duplicates are tracked by position, not by type.
        string bank = "FELLJERKTHIEF";
        bool[] used = new bool[bank.Length];
        for (int slot = 0; slot < answer.Length; slot++)
        {
            int index = -1;
            for (int i = 0; i < bank.Length; i++)
            {
                if (!used[i] && bank[i] == answer[slot]) { index = i; break; }
            }
            Assert.GreaterOrEqual(index, 0);
            used[index] = true;
            Move(false, index, true, slot);
            Assert.AreEqual(slot == answer.Length - 1, puzzle.IsSolved);
        }
        Assert.AreEqual(answer, puzzle.ArrangedLetters);
        Assert.IsFalse(puzzle.BeginDrag(true, 0));
        Assert.IsFalse(puzzle.DropTile(true, 1));
        Assert.AreEqual(answer, puzzle.ArrangedLetters, "Solved puzzles stay solved.");
    }

    [Test]
    public void DraggingAPlacedLetterBackToTheBankReturnsIt()
    {
        RevealAll();
        puzzle.Begin();
        Move(false, 4, true, 0);
        Move(true, 0, false, 4);
        Assert.AreEqual("_____________", puzzle.ArrangedLetters);
        Assert.IsFalse(puzzle.IsSolved);
    }

    [Test]
    public void UndiscoveredWordsNeverSupplyLetters()
    {
        puzzle.Begin();
        Assert.AreEqual(0, puzzle.AvailableLetterCount);
        Assert.AreEqual("_____________", puzzle.BankLetters);
        Assert.IsFalse(puzzle.BeginDrag(false, 4));
        Assert.IsFalse(puzzle.IsSolved);
    }

    [Test]
    public void OnlyRevealedWordLettersUnlockAndLaterWordsDoNotRefillUsedTiles()
    {
        inventory.RevealClueWords("Jerk");
        puzzle.Begin();
        Assert.AreEqual(4, puzzle.AvailableLetterCount);
        Assert.AreEqual("____JERK_____", puzzle.BankLetters);
        Assert.IsFalse(puzzle.BeginDrag(false, 0));
        Move(false, 4, true, 0);
        puzzle.Close();
        inventory.RevealClueWords("Fell, JERK, fell");
        puzzle.Begin();
        Assert.AreEqual(8, puzzle.AvailableLetterCount);
        Assert.AreEqual("FELL_ERK_____", puzzle.BankLetters);
        Assert.AreEqual('J', puzzle.ArrangedLetters[0]);
    }

    [Test]
    public void PointerMovesTheFloatingTileButCommitsOnlyOnRelease()
    {
        RevealAll();
        puzzle.Begin();
        Vector2 source = TileCentre(false, 4);
        Vector2 destination = TileCentre(true, 0);
        puzzle.HandlePointer(source, true, false);
        Assert.IsTrue(puzzle.IsDragging);
        puzzle.HandlePointer(destination, false, false);
        Assert.AreEqual("_____________", puzzle.ArrangedLetters);
        Assert.AreEqual('J', puzzle.BankLetters[4]);
        puzzle.HandlePointer(destination, false, true);
        Assert.IsFalse(puzzle.IsDragging);
        Assert.AreEqual('J', puzzle.ArrangedLetters[0]);
        Assert.AreEqual('_', puzzle.BankLetters[4]);
    }

    [Test]
    public void DroppingOutsideOrClosingDuringADragDoesNotLoseOrDuplicateLetters()
    {
        RevealAll();
        puzzle.Begin();
        string before = puzzle.BankLetters;
        puzzle.HandlePointer(TileCentre(false, 4), true, false);
        puzzle.HandlePointer(new Vector2(-50f, -50f), false, true);
        Assert.AreEqual(before, puzzle.BankLetters);
        Assert.IsFalse(puzzle.IsDragging);
        Assert.IsTrue(puzzle.BeginDrag(false, 4));
        puzzle.Close();
        puzzle.Begin();
        Assert.IsFalse(puzzle.IsDragging);
        Assert.AreEqual(before, puzzle.BankLetters);
        Assert.AreEqual("_____________", puzzle.ArrangedLetters);
    }

    [Test]
    public void DroppingOntoAnOccupiedTileSwapsTheLetters()
    {
        RevealAll();
        puzzle.Begin();
        Move(false, 4, true, 0);
        Move(false, 5, true, 1);
        Move(true, 0, true, 1);
        Assert.AreEqual("EJ___________", puzzle.ArrangedLetters);
        Assert.AreEqual("FELL__RKTHIEF", puzzle.BankLetters);
    }

    [Test]
    public void LockedBankCellsAndInvalidIndicesRejectDropsWithoutChangingLetters()
    {
        inventory.RevealClueWords("Jerk");
        puzzle.Begin();
        Move(false, 4, true, 0);
        Assert.IsTrue(puzzle.BeginDrag(true, 0));
        Assert.IsFalse(puzzle.DropTile(false, 0));
        Assert.AreEqual('J', puzzle.ArrangedLetters[0]);
        Assert.IsFalse(puzzle.IsDragging);
        Assert.IsFalse(puzzle.BeginDrag(false, -1));
        Assert.IsFalse(puzzle.BeginDrag(false, 13));
        Assert.IsTrue(puzzle.BeginDrag(true, 0));
        Assert.IsFalse(puzzle.DropTile(true, 13));
        Assert.AreEqual('J', puzzle.ArrangedLetters[0]);
    }

    [Test]
    public void UsingEveryLetterInTheWrongOrderDoesNotWin()
    {
        RevealAll();
        puzzle.Begin();
        for (int i = 0; i < 13; i++) Move(false, i, true, i);
        Assert.AreEqual("FELLJERKTHIEF", puzzle.ArrangedLetters);
        Assert.IsFalse(puzzle.IsSolved);
    }

    [Test]
    public void RevealedCluesAreCaseInsensitiveAndDeduplicated()
    {
        inventory.RevealClueWords(" Fell, fell, JERK, thief, , 123, two words ");
        inventory.RevealClueWords(null);
        Assert.AreEqual(3, inventory.RevealedWords.Count);
        Assert.IsTrue(inventory.HasRevealedWord(" fell "));
        Assert.IsFalse(inventory.HasRevealedWord("Unknown"));
        Assert.IsFalse(inventory.HasRevealedWord(null));
    }

    private void RevealAll() => inventory.RevealClueWords("Fell, Jerk, Thief");

    private void Move(bool sourceSlot, int source, bool targetSlot, int target)
    {
        Assert.IsTrue(puzzle.BeginDrag(sourceSlot, source));
        Assert.IsTrue(puzzle.DropTile(targetSlot, target));
    }

    private static Vector2 TileCentre(bool isSlot, int index) =>
        ((Rect)typeof(DolphinWordPuzzle).GetMethod("TileRect", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { index, isSlot })).center;

    [Test]
    public void TallStatuePromptStaysNearThePlayerAndBase()
    {
        var bounds = new Bounds(new Vector3(40f, 6.8f, 25f), new Vector3(6f, 13.7f, 9f));
        Vector3 player = new Vector3(36f, 1f, 25f);
        Vector3 anchor = DolphinWordPuzzle.GroundPromptAnchor(bounds, player);
        Assert.AreEqual(bounds.min.x, anchor.x, 0.001f);
        Assert.AreEqual(25f, anchor.z, 0.001f);
        Assert.AreEqual(bounds.min.y + 2.5f, anchor.y, 0.001f);
        Assert.Less(anchor.y, bounds.max.y - 8f);
    }

    [Test]
    public void SampleSceneHasPuzzleOnDolphinStatue()
    {
        string previousPath = SceneManager.GetActiveScene().path;
        try
        {
            Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
            DolphinWordPuzzle statue = Object.FindAnyObjectByType<DolphinWordPuzzle>();
            Assert.IsNotNull(statue);
            Assert.AreEqual("DolphinStatue", statue.name);
            Assert.AreEqual(scene, statue.gameObject.scene);
        }
        finally
        {
            if (!string.IsNullOrEmpty(previousPath))
                EditorSceneManager.OpenScene(previousPath, OpenSceneMode.Single);
            else
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
    }
}
