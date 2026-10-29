using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class DigSiteTests
{
    private GameObject root;
    private DigSite dig;
    private Random.State randomState;

    [SetUp]
    public void SetUp()
    {
        randomState = Random.state;
        Random.InitState(11);
        root = new GameObject("Dig test");
        dig = root.AddComponent<DigSite>();
    }

    [TearDown]
    public void TearDown()
    {
        dig.Cancel();
        Object.DestroyImmediate(root);
        Random.state = randomState;
    }

    [Test]
    public void ThreeChecksShrinkFromHalfASecondToAFifth()
    {
        dig.Begin();
        Assert.IsTrue(DigSite.IsDigging);
        var windows = new List<float>();
        for (int i = 0; i < 3; i++)
        {
            RunUntil(DigSite.Phase.Sweep);
            windows.Add(dig.Window);
            PressAt(dig.ZoneStart + dig.Window * 0.5f);
        }
        CollectionAssert.AreEqual(new[] { 0.5f, 0.3f, 0.2f }, windows);
        Assert.AreEqual(DigSite.Phase.Complete, dig.CurrentPhase);
        Assert.IsFalse(DigSite.IsDigging);
        RunUntil(DigSite.Phase.Dug);
        Assert.IsTrue(dig.IsDug);
    }

    [Test]
    public void ZoneOpensExactlyForTheWindow()
    {
        dig.Begin();
        RunUntil(DigSite.Phase.Sweep);
        PressAt(dig.ZoneStart + dig.Window - 0.005f);
        Assert.AreEqual(DigSite.Phase.Hit, dig.CurrentPhase);

        RunUntil(DigSite.Phase.Sweep);
        PressAt(dig.ZoneStart - 0.005f);
        Assert.AreEqual(DigSite.Phase.Failed, dig.CurrentPhase);
    }

    [Test]
    public void ZoneAlwaysFitsOnTheBarAfterTheNeedleStarts()
    {
        for (int attempt = 0; attempt < 50; attempt++)
        {
            dig.Begin();
            for (int i = 0; i < 3; i++)
            {
                RunUntil(DigSite.Phase.Sweep);
                Assert.Greater(dig.ZoneStart, 0f);
                Assert.LessOrEqual(dig.ZoneStart + dig.Window, DigSite.SweepDuration);
                PressAt(dig.ZoneStart + dig.Window * 0.5f);
            }
            RunUntil(DigSite.Phase.Dug);
            Object.DestroyImmediate(root);
            root = new GameObject("Dig test");
            dig = root.AddComponent<DigSite>();
        }
    }

    [Test]
    public void MissShattersTheBarFreesThePlayerAndStartsOver()
    {
        dig.Begin();
        RunUntil(DigSite.Phase.Sweep);
        PressAt(dig.ZoneStart + dig.Window * 0.5f);
        RunUntil(DigSite.Phase.Sweep);
        Assert.AreEqual(1, dig.Round);

        PressAt(0.01f);
        Assert.AreEqual(DigSite.Phase.Failed, dig.CurrentPhase);
        Assert.IsFalse(DigSite.IsDigging);
        RunUntil(DigSite.Phase.Shatter);
        RunUntil(DigSite.Phase.Idle);
        Assert.AreEqual(0, dig.Round);

        dig.Begin();
        RunUntil(DigSite.Phase.Sweep);
        Assert.AreEqual(0.5f, dig.Window);
    }

    [Test]
    public void LettingTheNeedlePassTheZoneIsAMiss()
    {
        dig.Begin();
        RunUntil(DigSite.Phase.Sweep);
        dig.Tick(DigSite.LeadIn + dig.ZoneStart + dig.Window + 0.01f, false);
        Assert.AreEqual(DigSite.Phase.Failed, dig.CurrentPhase);
    }

    [Test]
    public void PressesBeforeTheBarIsUpAndBetweenChecksAreIgnored()
    {
        dig.Begin();
        dig.Tick(0.01f, true);
        Assert.AreEqual(DigSite.Phase.Appearing, dig.CurrentPhase);
        RunUntil(DigSite.Phase.Sweep);
        PressAt(dig.ZoneStart + dig.Window * 0.5f);
        dig.Tick(0.01f, true);
        Assert.AreEqual(DigSite.Phase.Hit, dig.CurrentPhase);
    }

    // Moves the needle to needleTime seconds into its sweep, then presses.
    private void PressAt(float needleTime)
    {
        Assert.AreEqual(DigSite.Phase.Sweep, dig.CurrentPhase);
        dig.Tick(DigSite.LeadIn + needleTime - dig.PhaseTime, false);
        dig.Tick(0f, true);
    }

    private void RunUntil(DigSite.Phase phase)
    {
        for (int i = 0; i < 1000 && dig.CurrentPhase != phase; i++)
        {
            dig.Tick(1f / 60f, false);
        }
        Assert.AreEqual(phase, dig.CurrentPhase);
    }
}
