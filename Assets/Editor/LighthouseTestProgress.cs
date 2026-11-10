using System.Reflection;
using NUnit.Framework;
using UnityEngine;

internal static class LighthouseTestProgress
{
    // Exercise the real letter bank and drops, not a test-only progression switch.
    public static DolphinWordPuzzle Solve(GameObject owner)
    {
        DolphinWordPuzzle puzzle = null;
        foreach (GameObject root in owner.scene.GetRootGameObjects())
        {
            puzzle = root.GetComponentInChildren<DolphinWordPuzzle>(true);
            if (puzzle != null) break;
        }
        if (puzzle == null)
        {
            var holder = new GameObject("Test anagram");
            holder.transform.SetParent(owner.transform, false);
            puzzle = holder.AddComponent<DolphinWordPuzzle>();
        }
        InventorySystem previous = InventorySystem.instance;
        var bag = new GameObject("Test clue inventory");
        bag.transform.SetParent(owner.transform, false);
        try
        {
            var inventory = bag.AddComponent<InventorySystem>();
            typeof(InventorySystem).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(inventory, null);
            inventory.RevealClueWords("Fell, Jerk, Thief");
            puzzle.Begin();
            const string answer = "JEFFTHEKILLER";
            for (int slot = 0; slot < answer.Length; slot++)
            {
                if (puzzle.ArrangedLetters[slot] == answer[slot]) continue;
                int index = puzzle.BankLetters.IndexOf(answer[slot]);
                Assert.GreaterOrEqual(index, 0);
                Assert.IsTrue(puzzle.BeginDrag(false, index));
                Assert.IsTrue(puzzle.DropTile(true, slot));
            }
            Assert.IsTrue(puzzle.IsSolved);
            return puzzle;
        }
        finally
        {
            puzzle.Close();
            Object.DestroyImmediate(bag);
            InventorySystem.instance = previous;
        }
    }
}
