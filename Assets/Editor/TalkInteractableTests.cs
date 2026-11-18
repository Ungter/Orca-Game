using NUnit.Framework;
using UnityEngine;

public class TalkInteractableTests
{
    private GameObject root;
    private TalkInteractable talk;

    [SetUp]
    public void SetUp()
    {
        StoryDirector.SetFollowing(false);
        StoryDirector.SetPlayerName(StoryDirector.DefaultName);
        root = new GameObject("Talk test");
        talk = root.AddComponent<TalkInteractable>();
    }

    [TearDown]
    public void TearDown()
    {
        talk.Cancel();
        StoryDirector.SetFollowing(false);
        Object.DestroyImmediate(root);
    }

    [Test]
    public void ThePlayerAsksAboutTheWandAndPantherAnswersWithTheForest()
    {
        talk.Begin();
        Assert.AreEqual(Speaker.Panther, talk.Runner.Npc);
        Assert.AreEqual(Speaker.Player, talk.Runner.CurrentSpeaker);
        StringAssert.Contains("Alethi", talk.Text, "{NAME} becomes the player's name.");
        StringAssert.Contains("wand", talk.Text);
        RunUntil(TalkInteractable.Phase.Waiting);
        talk.Tick(0.01f, true);
        Assert.AreEqual(Speaker.Panther, talk.Runner.CurrentSpeaker);
        StringAssert.Contains("behind a tree in the forest", talk.Text);
    }

    [Test]
    public void WithOrcaAlongOrcaAsksInsteadAndStandsBesideThePlayer()
    {
        StoryDirector.SetFollowing(true);
        talk.Begin();
        Assert.AreEqual(Speaker.Orca, talk.Runner.CurrentSpeaker);
        CollectionAssert.AreEqual(new[] { Speaker.Orca, Speaker.Player }, talk.Runner.RightSide);
    }

    [Test]
    public void PlayerStaysLockedUntilTheConversationEnds()
    {
        Assert.IsFalse(TalkInteractable.IsTalking);
        Assert.IsFalse(talk.HasSpoken);
        talk.Begin();
        Assert.IsTrue(TalkInteractable.IsTalking);

        RunUntil(TalkInteractable.Phase.Waiting);
        talk.Tick(0.01f, true);
        Assert.IsTrue(TalkInteractable.IsTalking, "Panther still has to answer.");
        Assert.IsFalse(talk.HasSpoken);
        RunUntil(TalkInteractable.Phase.Waiting);
        talk.Tick(0.01f, true);
        Assert.AreEqual(TalkInteractable.Phase.Idle, talk.CurrentPhase);
        Assert.IsFalse(TalkInteractable.IsTalking);
        Assert.IsTrue(talk.HasSpoken);
    }

    [Test]
    public void FirstPressFinishesTypingAndTheSecondMovesOn()
    {
        talk.Begin();
        talk.Tick(0.05f, false);
        Assert.AreEqual(TalkInteractable.Phase.Typing, talk.CurrentPhase);
        Assert.Less(talk.VisibleCharacters, talk.Text.Length);

        talk.Tick(0.01f, true);
        Assert.AreEqual(talk.Text.Length, talk.VisibleCharacters);
        Assert.AreEqual(TalkInteractable.Phase.Waiting, talk.CurrentPhase);

        talk.Tick(0.01f, false);
        Assert.AreEqual(TalkInteractable.Phase.Waiting, talk.CurrentPhase);
        talk.Tick(0.01f, true);
        Assert.AreEqual(TalkInteractable.Phase.Typing, talk.CurrentPhase);
        Assert.AreEqual(1, talk.Runner.Index);
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
        for (int i = 0; i < 2000 && talk.CurrentPhase != phase; i++)
        {
            talk.Tick(1f / 60f, false);
        }
        Assert.AreEqual(phase, talk.CurrentPhase);
    }
}
