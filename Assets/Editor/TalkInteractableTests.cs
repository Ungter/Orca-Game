using NUnit.Framework;
using UnityEngine;

public class TalkInteractableTests
{
    private GameObject root;
    private TalkInteractable talk;

    [SetUp]
    public void SetUp()
    {
        root = new GameObject("Talk test");
        talk = root.AddComponent<TalkInteractable>();
    }

    [TearDown]
    public void TearDown()
    {
        talk.Cancel();
        Object.DestroyImmediate(root);
    }

    [Test]
    public void LineIsTheGravestoneMessage()
    {
        Assert.AreEqual("Something is in the forest", talk.Text);
    }

    [Test]
    public void PlayerStaysLockedUntilThePortraitHasLeft()
    {
        Assert.IsFalse(TalkInteractable.IsTalking);
        Assert.IsFalse(talk.HasSpoken);
        talk.Begin();
        Assert.IsTrue(TalkInteractable.IsTalking);

        RunUntil(TalkInteractable.Phase.Waiting);
        talk.Tick(0.01f, true);
        Assert.AreEqual(TalkInteractable.Phase.Leaving, talk.CurrentPhase);
        Assert.IsTrue(TalkInteractable.IsTalking);

        talk.Tick(TalkInteractable.LeaveDuration * 0.5f, false);
        Assert.IsTrue(TalkInteractable.IsTalking);
        Assert.IsFalse(talk.HasSpoken);
        talk.Tick(TalkInteractable.LeaveDuration, false);
        Assert.AreEqual(TalkInteractable.Phase.Idle, talk.CurrentPhase);
        Assert.IsFalse(TalkInteractable.IsTalking);
        Assert.IsTrue(talk.HasSpoken);
    }

    [Test]
    public void PressesAreIgnoredWhileThePortraitEntersAndTheBubblePops()
    {
        talk.Begin();
        talk.Tick(0.01f, true);
        Assert.AreEqual(TalkInteractable.Phase.Entering, talk.CurrentPhase);
        talk.Tick(TalkInteractable.EnterDuration, true);
        Assert.AreEqual(TalkInteractable.Phase.Popping, talk.CurrentPhase);
        talk.Tick(TalkInteractable.PopDuration, true);
        Assert.AreEqual(TalkInteractable.Phase.Typing, talk.CurrentPhase);
        Assert.AreEqual(0, talk.VisibleCharacters);
    }

    [Test]
    public void FirstPressFinishesTypingAndTheSecondCloses()
    {
        talk.Begin();
        RunUntil(TalkInteractable.Phase.Typing);
        talk.Tick(0.05f, false);
        Assert.Less(talk.VisibleCharacters, talk.Text.Length);

        talk.Tick(0.01f, true);
        Assert.AreEqual(talk.Text.Length, talk.VisibleCharacters);
        Assert.AreEqual(TalkInteractable.Phase.Waiting, talk.CurrentPhase);

        talk.Tick(0.01f, false);
        Assert.AreEqual(TalkInteractable.Phase.Waiting, talk.CurrentPhase);
        talk.Tick(0.01f, true);
        Assert.AreEqual(TalkInteractable.Phase.Leaving, talk.CurrentPhase);
    }

    [Test]
    public void LineTypesOutOnItsOwn()
    {
        talk.Begin();
        RunUntil(TalkInteractable.Phase.Waiting);
        Assert.AreEqual(talk.Text.Length, talk.VisibleCharacters);
    }

    private void RunUntil(TalkInteractable.Phase phase)
    {
        for (int i = 0; i < 1000 && talk.CurrentPhase != phase; i++)
        {
            talk.Tick(1f / 60f, false);
        }
        Assert.AreEqual(phase, talk.CurrentPhase);
    }
}
