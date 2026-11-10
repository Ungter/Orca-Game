using NUnit.Framework;
using UnityEngine;

public class LighthouseStackMatchTests
{
    [TestCase(0)]
    [TestCase(-1)]
    public void ThresholdMustBePositive(int threshold) =>
        Assert.Throws<System.ArgumentOutOfRangeException>(() => new LighthouseStackMatch(threshold));

    [Test]
    public void TenSectionsWinsWithoutEndingTheRun()
    {
        var match = new LighthouseStackMatch();
        for (int i = 1; i <= 40; i++)
        {
            Centre(match);
            Assert.IsTrue(match.Drop());
            Assert.AreEqual(i, match.Score);
            Assert.AreEqual(i >= 10, match.HasWon);
            Assert.IsFalse(match.Failed);
            Assert.AreEqual(i % 2, match.Axis);
        }
    }

    [TestCase(-2f)]
    [TestCase(2f)]
    public void OverhangFallsAwayAndOnlyTheOverlapSupportsTheNextSection(float offset)
    {
        var match = new LighthouseStackMatch(3);
        match.Tick((match.Travel + offset) / match.Speed);
        Assert.IsTrue(match.Drop());
        Assert.IsFalse(match.Perfect);
        Assert.AreEqual(offset * 0.5f, match.Centre.x, 0.0001f);
        Assert.AreEqual(6f, match.Size.x, 0.0001f);
        Assert.AreEqual(8f, match.Size.y);
        Assert.AreEqual(2f, match.CutSize.x, 0.0001f);
        Assert.AreEqual(Mathf.Sign(offset) * 5f, match.CutCentre.x, 0.0001f);
        Assert.AreEqual(1, match.Axis);
        match.Tick((match.Travel + 3f) / match.Speed);
        Assert.IsTrue(match.Drop());
        Assert.AreEqual(6f, match.Size.x, 0.0001f);
        Assert.AreEqual(5f, match.Size.y, 0.0001f);
        Assert.AreEqual(1.5f, match.Centre.y, 0.0001f);
    }

    [Test]
    public void PerfectPlacementsSnapToThePreviousSectionWithoutLosingWidth()
    {
        var match = new LighthouseStackMatch();
        match.Tick((match.Travel + 0.04f) / match.Speed);
        Assert.IsTrue(match.Drop());
        Assert.IsTrue(match.Perfect);
        Assert.AreEqual(Vector2.zero, match.Centre);
        Assert.AreEqual(Vector2.one * 8f, match.Size);
        Assert.AreEqual(Vector2.zero, match.CutSize);
    }

    [Test]
    public void ACompleteMissEndsTheRunAndCannotBeScoredRepeatedly()
    {
        var match = new LighthouseStackMatch();
        Assert.IsFalse(match.Drop());
        Assert.IsTrue(match.Failed);
        Assert.IsFalse(match.HasWon);
        match.Tick(100f);
        Assert.IsFalse(match.Drop());
        Assert.AreEqual(0, match.Score);
    }

    [Test]
    public void MissingAfterTheThresholdDoesNotTakeTheWinAway()
    {
        var match = new LighthouseStackMatch(1);
        Centre(match);
        Assert.IsTrue(match.Drop());
        Assert.IsFalse(match.Drop());
        Assert.IsTrue(match.HasWon);
        Assert.IsTrue(match.Failed);
    }

    [Test]
    public void TenThousandSectionsKeepWorkingAndMovementStaysBounded()
    {
        var match = new LighthouseStackMatch();
        for (int i = 0; i < 10000; i++)
        {
            Centre(match);
            Assert.IsTrue(match.Drop(), "Section " + i);
        }
        Assert.AreEqual(10000, match.Score);
        Assert.AreEqual(Vector2.one * 8f, match.Size);
        Assert.LessOrEqual(match.Speed, 7f);
        match.Tick(float.MaxValue);
        Assert.That(match.Offset, Is.InRange(-match.Travel, match.Travel));
        float offset = match.Offset;
        match.Tick(-1f);
        Assert.AreEqual(offset, match.Offset);
    }

    private static void Centre(LighthouseStackMatch match) => match.Tick(-match.Offset / match.Speed);
}
