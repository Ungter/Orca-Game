using NUnit.Framework;

public class TwentyOneMatchTests
{
    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(4)]
    [TestCase(100)]
    public void OnlyTheActivePlayerCanTakeOneToThreeTiles(int invalid)
    {
        var match = new TwentyOneMatch();
        Assert.IsFalse(match.Take(TwentyOneMatch.Side.Player, 1));
        match.Start(true);
        Assert.IsFalse(match.Take(TwentyOneMatch.Side.Player, invalid));
        Assert.IsFalse(match.Take(TwentyOneMatch.Side.Opponent, 1));
        Assert.AreEqual(0, match.Total);
        Assert.IsTrue(match.Take(TwentyOneMatch.Side.Player, 2));
        Assert.AreEqual(TwentyOneMatch.Side.Opponent, match.Turn);
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void TakingOrPassingTheTwentyFirstTileLoses(int finalRoll)
    {
        var match = new TwentyOneMatch();
        match.Start(true);
        for (int i = 0; i < 5; i++)
        {
            Assert.IsTrue(match.Take(TwentyOneMatch.Side.Player, 1));
            Assert.IsTrue(match.Take(TwentyOneMatch.Side.Opponent, 3));
        }
        Assert.AreEqual(20, match.Total);
        Assert.IsTrue(match.Take(TwentyOneMatch.Side.Player, finalRoll));
        Assert.AreEqual(21, match.Total);
        Assert.AreEqual(TwentyOneMatch.Side.Player, match.Loser);
        Assert.AreEqual(TwentyOneMatch.Side.Opponent, match.Winner);
        Assert.IsFalse(match.Take(TwentyOneMatch.Side.Opponent, 1));
    }

    [Test]
    public void OpponentGoingSecondWinsAgainstEveryPossiblePlayerSequence()
    {
        // All 3^6 player sequences, including every possible final losing roll.
        for (int sequence = 0; sequence < 729; sequence++)
        {
            var match = new TwentyOneMatch();
            match.Start(true);
            int choices = sequence;
            while (!match.Finished)
            {
                Assert.IsTrue(match.Take(TwentyOneMatch.Side.Player, choices % 3 + 1));
                choices /= 3;
                if (match.Finished) break;
                int reply = match.OpponentRoll();
                Assert.IsTrue(match.Take(TwentyOneMatch.Side.Opponent, reply));
                Assert.AreEqual(0, match.Total % 4);
            }
            Assert.AreEqual(TwentyOneMatch.Side.Opponent, match.Winner, "Sequence " + sequence);
        }
    }

    [Test]
    public void PlayerGoingSecondCanBeatEveryOpponentSequenceByReplyingToFour()
    {
        for (int sequence = 0; sequence < 729; sequence++)
        {
            var match = new TwentyOneMatch();
            match.Start(false);
            int choices = sequence;
            while (!match.Finished)
            {
                int roll = choices % 3 + 1;
                choices /= 3;
                Assert.IsTrue(match.Take(TwentyOneMatch.Side.Opponent, roll));
                if (match.Finished) break;
                Assert.IsTrue(match.Take(TwentyOneMatch.Side.Player, 4 - roll));
                Assert.AreEqual(0, match.Total % 4);
            }
            Assert.AreEqual(TwentyOneMatch.Side.Player, match.Winner, "Sequence " + sequence);
        }
    }

    [Test]
    public void GoingSecondDoesNotAutomaticallyWinWhenThePlayerChoosesBadly()
    {
        var match = new TwentyOneMatch();
        match.Start(false);
        while (!match.Finished)
            match.Take(match.Turn, match.Turn == TwentyOneMatch.Side.Opponent ? match.OpponentRoll() : 1);
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
}
