using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public class DolphinWordPuzzleTests
{
    private GameObject root;
    private DolphinWordPuzzle puzzle;

    [SetUp]
    public void SetUp()
    {
        root = new GameObject("Dolphin puzzle test");
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
        Assert.IsFalse(DolphinWordPuzzle.IsOpen);
        puzzle.Begin();
        Assert.IsTrue(Interactables.Busy);
        puzzle.ChooseTile(false, 4); // J
        puzzle.ChooseTile(true, 0);
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
            puzzle.ChooseTile(false, index);
            puzzle.ChooseTile(true, slot);
            Assert.AreEqual(slot == answer.Length - 1, puzzle.IsSolved);
        }
        Assert.AreEqual(answer, puzzle.ArrangedLetters);
        puzzle.ChooseTile(true, 0);
        puzzle.ChooseTile(true, 1);
        Assert.AreEqual(answer, puzzle.ArrangedLetters, "Solved puzzles stay solved.");
    }

    [Test]
    public void SelectingAPlacedLetterAndEmptyBankSlotReturnsIt()
    {
        puzzle.Begin();
        puzzle.ChooseTile(false, 4);
        puzzle.ChooseTile(true, 0);
        puzzle.ChooseTile(true, 0);
        puzzle.ChooseTile(false, 4);
        Assert.AreEqual("_____________", puzzle.ArrangedLetters);
        Assert.IsFalse(puzzle.IsSolved);
    }

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
