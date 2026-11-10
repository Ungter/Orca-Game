using System.Collections.Generic;
using NUnit.Framework;

public class TwentyOneMatchTests
{
    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(4)]
    [TestCase(100)]
    public void OnlyTheActivePlayerCanTakeOneToThreeTiles(int invalid)
    {
        var match = new TwentyOneMatch(21);
        Assert.IsFalse(match.Take(TwentyOneMatch.Side.Player, 1));
        match.Start(true);
        Assert.IsFalse(match.Take(TwentyOneMatch.Side.Player, invalid));
        Assert.IsFalse(match.Take(TwentyOneMatch.Side.Opponent, 1));
        Assert.AreEqual(0, match.Total);
        Assert.IsTrue(match.Take(TwentyOneMatch.Side.Player, 2));
        Assert.AreEqual(TwentyOneMatch.Side.Opponent, match.Turn);
    }

    [TestCase(0)]
    [TestCase(19)]
    [TestCase(31)]
    public void TheLastTileIsAlwaysFromTwentyToThirty(int limit)
    {
        Assert.AreEqual(20, TwentyOneMatch.MinLimit);
        Assert.AreEqual(30, TwentyOneMatch.MaxLimit);
        Assert.Throws<System.ArgumentOutOfRangeException>(() => new TwentyOneMatch(limit));
    }

    [Test]
    public void TakingOrPassingTheLastTileLoses(
        [Range(TwentyOneMatch.MinLimit, TwentyOneMatch.MaxLimit)] int limit, [Values(1, 2, 3)] int finalRoll)
    {
        var match = new TwentyOneMatch(limit);
        match.Start(true);
        while (match.Total < limit - 1) Assert.IsTrue(match.Take(match.Turn, 1));
        Assert.IsFalse(match.Finished);
        TwentyOneMatch.Side mover = match.Turn;
        Assert.IsTrue(match.Take(mover, finalRoll));
        Assert.AreEqual(limit, match.Total);
        Assert.AreEqual(mover, match.Loser);
        Assert.AreNotEqual(mover, match.Winner);
        Assert.IsFalse(match.Take(match.Turn, 1));
    }

    [Test]
    public void GoingSecondOnlyWinsWhenTheLastTileIs21Or25Or29()
    {
        for (int limit = 20; limit <= 30; limit++)
            Assert.AreEqual(limit == 21 || limit == 25 || limit == 29, !Solve(limit)[0], "Last tile " + limit);
    }

    [Test]
    public void AlTakesAWinningRollWheneverThereIsOne(
        [Range(TwentyOneMatch.MinLimit, TwentyOneMatch.MaxLimit)] int limit)
    {
        bool[] wins = Solve(limit);
        for (int total = 0; total < limit; total++)
        {
            int roll = OpponentToRollAt(limit, total).OpponentRoll();
            Assert.That(roll, Is.InRange(1, 3));
            if (wins[total])
                Assert.IsFalse(total + roll >= limit || wins[total + roll], "Missed the winning roll at " + total);
            else if (total < limit - 1)
                Assert.Less(total + roll, limit, "Took the last tile with safe rolls left at " + total);
        }
    }

    [Test]
    public void AlWinsEveryGameWhenTheTurnOrderFavoursHim(
        [Range(TwentyOneMatch.MinLimit, TwentyOneMatch.MaxLimit)] int limit)
    {
        // Saying yes lets him go second, which only favours him when whoever rolls first loses.
        bool yes = !Solve(limit)[0];
        int games = 0;
        foreach (TwentyOneMatch match in EverySequence(limit, yes, TwentyOneMatch.Side.Player, m => m.OpponentRoll()))
        {
            games++;
            Assert.AreEqual(TwentyOneMatch.Side.Opponent, match.Winner, "Last tile " + limit);
        }
        Assert.Greater(games, 100);
    }

    [Test]
    public void TheRightTurnOrderBeatsEveryOpponentSequence(
        [Range(TwentyOneMatch.MinLimit, TwentyOneMatch.MaxLimit)] int limit)
    {
        bool[] wins = Solve(limit);
        int games = 0;
        foreach (TwentyOneMatch match in EverySequence(limit, wins[0], TwentyOneMatch.Side.Opponent,
            m => WinningRoll(wins, m.Total)))
        {
            games++;
            Assert.AreEqual(TwentyOneMatch.Side.Player, match.Winner, "Last tile " + limit);
        }
        Assert.Greater(games, 100);
    }

    [Test]
    public void TheRightTurnOrderStillLosesToCarelessRolls(
        [Range(TwentyOneMatch.MinLimit, TwentyOneMatch.MaxLimit)] int limit)
    {
        var match = new TwentyOneMatch(limit);
        match.Start(Solve(limit)[0]);
        while (!match.Finished)
            Assert.IsTrue(match.Take(match.Turn, match.Turn == TwentyOneMatch.Side.Opponent ? match.OpponentRoll() : 1));
        Assert.AreEqual(TwentyOneMatch.Side.Opponent, match.Winner);
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void EachDieFaceHasTheRightNumberOfPips(int face)
    {
        var texture = TwentyOneDieArt.Create(face);
        try
        {
            int dark = 0;
            for (int y = 9; y <= 24; y++)
            for (int x = 7; x <= 22; x++)
                if (texture.GetPixel(x, y).r < 0.3f) dark++;
            Assert.AreEqual(face * 9, dark);
            Assert.AreEqual(UnityEngine.FilterMode.Point, texture.filterMode);
        }
        finally { UnityEngine.Object.DestroyImmediate(texture); }
    }

    // Brute force from the rules alone: wins[total] says whether the side about to roll can force
    // a win. Reaching or passing the last tile loses, so only rolls that stop short of it can win.
    internal static bool[] Solve(int limit)
    {
        var wins = new bool[limit];
        for (int total = limit - 1; total >= 0; total--)
            for (int roll = 1; roll <= 3 && total + roll < limit; roll++)
                wins[total] |= !wins[total + roll];
        return wins;
    }

    // A roll that leaves the other side unable to force a win, or 0 when there is none.
    internal static int WinningRoll(bool[] wins, int total)
    {
        for (int roll = 1; roll <= 3 && total + roll < wins.Length; roll++)
            if (!wins[total + roll]) return roll;
        return 0;
    }

    private static TwentyOneMatch OpponentToRollAt(int limit, int total)
    {
        var match = new TwentyOneMatch(limit);
        // One tile per turn, so who starts decides whose turn it is at this total.
        match.Start(total % 2 == 1);
        for (int i = 0; i < total; i++) Assert.IsTrue(match.Take(match.Turn, 1));
        Assert.AreEqual(TwentyOneMatch.Side.Opponent, match.Turn);
        return match;
    }

    // Every sequence of rolls for one side, the other side answering with its strategy, each
    // played from the start to the end of a match.
    private static IEnumerable<TwentyOneMatch> EverySequence(int limit, bool opponentGoesSecond,
        TwentyOneMatch.Side enumerated, System.Func<TwentyOneMatch, int> answer)
    {
        var pending = new Stack<List<int>>();
        pending.Push(new List<int>());
        while (pending.Count > 0)
        {
            List<int> rolls = pending.Pop();
            var match = new TwentyOneMatch(limit);
            match.Start(opponentGoesSecond);
            int next = 0;
            while (!match.Finished && (match.Turn != enumerated || next < rolls.Count))
                Assert.IsTrue(match.Take(match.Turn, match.Turn == enumerated ? rolls[next++] : answer(match)));
            if (match.Finished) yield return match;
            else
                for (int roll = 1; roll <= 3; roll++) pending.Push(new List<int>(rolls) { roll });
        }
    }
}
