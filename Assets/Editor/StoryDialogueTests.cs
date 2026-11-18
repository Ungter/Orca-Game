using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class StoryDialogueTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private GameObject root;
    private GameObject player;
    private InventorySystem inventory;
    private InventorySystem previousInventory;

    [SetUp]
    public void SetUp()
    {
        StoryDirector.SetPlayerName(StoryDirector.DefaultName);
        StoryDirector.SetFollowing(false);
        root = new GameObject("Story test");
        player = new GameObject("Player") { tag = "Player" };
        player.transform.SetParent(root.transform);
        previousInventory = InventorySystem.instance;
        inventory = player.AddComponent<InventorySystem>();
        typeof(InventorySystem).GetMethod("Awake", Private).Invoke(inventory, null);
    }

    [TearDown]
    public void TearDown()
    {
        StoryDirector director = Object.FindAnyObjectByType<StoryDirector>();
        if (director != null && director.Orca != null) Object.DestroyImmediate(director.Orca.gameObject);
        Object.DestroyImmediate(root);
        InventorySystem.instance = previousInventory;
        StoryDirector.SetPlayerName(StoryDirector.DefaultName);
        StoryDirector.SetFollowing(false);
        typeof(StoryDirector).GetProperty("Current").SetValue(null, null);
    }

    [Test]
    public void LinesTypeThenEnterMovesOnAndTheEndRunsOnce()
    {
        var runner = new DialogueRunner();
        int done = 0;
        runner.Play(Speaker.Iguana, new[] { DialogueLine.Say(Speaker.Iguana, "Hello."), DialogueLine.Say(Speaker.Player, "Hi {NAME}.") }, () => done++);
        Assert.AreEqual(Speaker.Iguana, runner.Npc);
        CollectionAssert.AreEqual(new[] { Speaker.Player }, runner.RightSide);
        Assert.IsTrue(runner.IsTyping);
        runner.Tick(0.05f, DialogueInput.None);
        Assert.Less(runner.VisibleCharacters, runner.Text.Length);
        runner.Tick(0f, DialogueInput.Continue);
        Assert.AreEqual(DialogueRunner.State.Waiting, runner.CurrentState, "The first press only finishes the line.");
        runner.Tick(0f, DialogueInput.Continue);
        Assert.AreEqual("Hi Alethi.", runner.Text);
        Assert.AreEqual(Speaker.Player, runner.CurrentSpeaker);
        runner.Tick(10f, DialogueInput.None);
        runner.Tick(0f, DialogueInput.Continue);
        Assert.IsFalse(runner.IsActive);
        Assert.AreEqual(1, done);
        runner.Tick(0f, DialogueInput.Continue);
        Assert.AreEqual(1, done);
    }

    [Test]
    public void ALastLineCanEndInAChoicePickedByNumberArrowsOrEnter()
    {
        var runner = new DialogueRunner();
        int picked = -1;
        string[] options = { "YES", "NO" };
        runner.Play(Speaker.Magician, new[] { DialogueLine.Say(Speaker.Magician, "Ready?") }, null, options, i => picked = i);
        runner.Tick(10f, DialogueInput.None);
        Assert.AreEqual(DialogueRunner.State.Choosing, runner.CurrentState);
        var down = DialogueInput.None;
        down.Move = 1;
        runner.Tick(0f, down);
        Assert.AreEqual(1, runner.Selected);
        runner.Tick(0f, down);
        Assert.AreEqual(0, runner.Selected, "Wraps around.");
        runner.Tick(0f, DialogueInput.Continue);
        Assert.AreEqual(0, picked);

        runner.Play(Speaker.Magician, new[] { DialogueLine.Say(Speaker.Magician, "Ready?") }, null, options, i => picked = i);
        runner.Tick(10f, DialogueInput.None);
        var two = DialogueInput.None;
        two.Choose = 1;
        runner.Tick(0f, two);
        Assert.AreEqual(1, picked);
        Assert.IsFalse(runner.IsActive);
    }

    [Test]
    public void TheNameIsTypedFilteredAndUsedInLaterLines()
    {
        var runner = new DialogueRunner();
        runner.Play(Speaker.Orca, new[] { new DialogueLine(Speaker.Orca, "What's your name?", true), DialogueLine.Say(Speaker.Orca, "Well, {NAME}!") });
        runner.Tick(10f, DialogueInput.None);
        Assert.AreEqual(DialogueRunner.State.Naming, runner.CurrentState);
        var typing = DialogueInput.None;
        typing.Typed = " kaiXYZWVUTSRQP";
        runner.Tick(0f, typing);
        Assert.AreEqual("kaiXYZWVUTSR", runner.NameDraft, "No leading space, at most 12 letters.");
        var back = DialogueInput.None;
        back.Backspace = true;
        for (int i = 0; i < 9; i++) runner.Tick(0f, back);
        Assert.AreEqual("kai", runner.NameDraft);
        runner.Tick(0f, DialogueInput.Continue);
        Assert.AreEqual("kai", StoryDirector.PlayerName);
        Assert.AreEqual("Well, kai!", runner.Text);
        Assert.AreEqual("KAI", DialoguePortraits.DisplayName(Speaker.Player));
    }

    [Test]
    public void ABlankNameIsAlethi()
    {
        var runner = new DialogueRunner();
        StoryDirector.SetPlayerName("Someone");
        runner.Play(Speaker.Orca, new[] { new DialogueLine(Speaker.Orca, "Name?", true) });
        runner.Tick(10f, DialogueInput.None);
        runner.Tick(0f, DialogueInput.Continue);
        Assert.AreEqual(StoryDirector.DefaultName, StoryDirector.PlayerName);
    }

    [Test]
    public void TheMagicianHasAQuestionMarkAndEverySpeakerWithArtHasAPortrait()
    {
        Assert.AreEqual(DialoguePortraits.QuestionMarkTexture, DialoguePortraits.Get(Speaker.Magician).Texture);
        foreach (Speaker speaker in new[] { Speaker.Orca, Speaker.Iguana, Speaker.Panther, Speaker.Player, Speaker.AlCachud })
        {
            Portrait portrait = DialoguePortraits.Get(speaker);
            Assert.IsTrue(portrait.IsValid, speaker + " has art.");
            Assert.Greater(portrait.UV.width, 0f);
            Assert.LessOrEqual(portrait.UV.width, 1f);
        }
        var set = Resources.Load<DialoguePortraitSet>(DialoguePortraits.SetResource);
        Assert.IsNotNull(set);
        Assert.IsNotNull(set.Find(Speaker.Orca).ghostHead, "Ghost heads are preferred.");
    }

    [Test]
    public void ArrivalAsksTheNameAndAcceptingMakesOrcaFollow()
    {
        StoryDirector director = NewDirector();
        director.BeginArrival();
        Assert.IsTrue(StoryDirector.IsTalking);
        Assert.IsTrue(Interactables.BlocksMovement);
        Assert.AreEqual(Speaker.Orca, director.Runner.Npc);
        Assert.AreEqual("HALT! Who goes there?!", director.Runner.Text);
        Assert.IsNotNull(director.Orca, "Orca waits near the player.");
        ReadUntil(director.Runner, DialogueRunner.State.Naming);
        Assert.IsTrue(director.Runner.SubmitName("Kai"));
        Assert.AreEqual("Kai.", director.Runner.Text);
        ReadUntil(director.Runner, DialogueRunner.State.Choosing);
        CollectionAssert.AreEqual(StoryScript.AcceptHelp, director.Runner.Choices);
        director.Runner.Tick(0f, DialogueInput.Continue);
        Assert.AreEqual("Very well! Follow me.", director.Runner.Text);
        Assert.IsFalse(StoryDirector.OrcaFollowing);
        director.Runner.Tick(10f, DialogueInput.None);
        director.Runner.Tick(0f, DialogueInput.Continue);
        Assert.IsFalse(StoryDirector.IsTalking);
        Assert.IsTrue(StoryDirector.OrcaFollowing);
        Assert.IsFalse(StoryDirector.ExploringAlone, "With Orca along the doors' beacons stay dark.");
        Assert.AreEqual(OrcaSpirit.Mode.Following, director.Orca.CurrentMode);
    }

    [Test]
    public void DecliningSendsOrcaAwayUntilEveryClueIsFoundThenHeWaitsAtTheStatue()
    {
        StoryDirector director = NewDirector();
        director.BeginArrival();
        ReadUntil(director.Runner, DialogueRunner.State.Naming);
        director.Runner.SubmitName("");
        ReadUntil(director.Runner, DialogueRunner.State.Choosing);
        var no = DialogueInput.None;
        no.Choose = 1;
        director.Runner.Tick(0f, no);
        Assert.AreEqual("Very well. Good luck, Alethi.", director.Runner.Text);
        director.Runner.Tick(10f, DialogueInput.None);
        director.Runner.Tick(0f, DialogueInput.Continue);
        Assert.IsFalse(StoryDirector.OrcaFollowing);
        Assert.IsTrue(StoryDirector.ExploringAlone, "Alone, the doors' beacons light the way.");
        Assert.AreEqual(OrcaSpirit.Mode.Vanishing, director.Orca.CurrentMode);

        var statueObject = new GameObject("Orca statue");
        statueObject.transform.SetParent(root.transform);
        DolphinWordPuzzle statue = statueObject.AddComponent<DolphinWordPuzzle>();
        Assert.IsFalse(StoryDirector.HoldsEveryClue());
        inventory.RevealClueWords("Fell, Jerk");
        Assert.IsFalse(StoryDirector.HoldsEveryClue());
        inventory.RevealClueWords("Thief");
        Assert.IsTrue(StoryDirector.HoldsEveryClue());
        typeof(StoryDirector).GetMethod("WaitAtStatue", Private).Invoke(director, null);
        Assert.IsTrue(director.OrcaWaitsAtStatue);
        Assert.AreEqual(OrcaSpirit.Mode.Waiting, director.Orca.CurrentMode);

        statue.Begin();
        Assert.IsFalse(DolphinWordPuzzle.IsOpen, "Orca speaks first.");
        Assert.AreEqual(StoryScript.StatueMeeting[0].Text.Replace("{NAME}", "Alethi"), director.Runner.Text);
        for (int i = 0; i < 10 && director.Runner.IsActive; i++)
        {
            director.Runner.Tick(10f, DialogueInput.None);
            director.Runner.Tick(0f, DialogueInput.Continue);
        }
        Assert.IsTrue(DolphinWordPuzzle.IsOpen, "Then the statue opens.");
        Assert.IsTrue(StoryDirector.OrcaFollowing);
        statue.Close();
        statue.Begin();
        Assert.IsTrue(DolphinWordPuzzle.IsOpen, "Only the first time.");
        statue.Close();
    }

    [Test]
    public void ClosingTheSolvedStatueHasOrcaNameTheKiller()
    {
        StoryDirector director = NewDirector();
        var statueObject = new GameObject("Orca statue");
        statueObject.transform.SetParent(root.transform);
        DolphinWordPuzzle statue = statueObject.AddComponent<DolphinWordPuzzle>();
        inventory.RevealClueWords("Fell, Jerk, Thief");
        statue.Begin();
        typeof(DolphinWordPuzzle).GetProperty("IsSolved").SetValue(statue, true);
        statue.Close();
        Assert.IsTrue(statue.HasNamedKiller);
        Assert.AreEqual("Jeff?! That's Al Cachud's real name!", director.Runner.Text);
        Assert.AreEqual(Speaker.None, director.Runner.Npc);
        CollectionAssert.Contains(director.Runner.RightSide, Speaker.Orca);
    }

    [Test]
    public void LeadingOrcaGoesAheadWaitsForAPlayerWhoFallsBehindAndStopsAtTheGoal()
    {
        typeof(ClueMinigame).GetProperty("Current").SetValue(null, null);
        player.transform.position = Vector3.zero;
        OrcaSpirit orca = OrcaSpirit.Create(player.scene, Vector3.zero, player.transform);
        try
        {
            orca.Lead(new Vector3(30f, 0f, 0f));
            Assert.AreEqual(OrcaSpirit.Mode.Leading, orca.CurrentMode);
            orca.StepLead(1f);
            Assert.AreEqual(OrcaSpirit.LeadSpeed, orca.Anchor.x, 0.01f, "The player's pace.");
            for (int i = 0; i < 10 && !orca.IsWaitingForPlayer; i++) orca.StepLead(0.5f);
            Assert.IsTrue(orca.IsWaitingForPlayer, "The player stayed put, so he stops for them.");
            float stopped = orca.Anchor.x;
            orca.StepLead(1f);
            Assert.AreEqual(stopped, orca.Anchor.x, 0.001f);
            player.transform.position = new Vector3(stopped - 2f, 0f, 0f);
            orca.StepLead(0.5f);
            Assert.IsFalse(orca.IsWaitingForPlayer);
            Assert.Greater(orca.Anchor.x, stopped, "Caught up, he goes on.");
            for (int i = 0; i < 100 && !orca.HasArrived; i++)
            {
                player.transform.position = orca.Anchor - Vector3.right * 2f;
                orca.StepLead(0.25f);
            }
            Assert.IsTrue(orca.HasArrived);
            Assert.LessOrEqual(Mathf.Abs(30f - orca.Anchor.x), OrcaSpirit.ArriveRadius + 0.01f);
            float x = orca.Anchor.x;
            orca.StepLead(1f);
            Assert.AreEqual(x, orca.Anchor.x, 0.001f, "At the goal he waits for the player.");
        }
        finally { Object.DestroyImmediate(orca.gameObject); }
    }

    [Test]
    public void APlayerWhoGoesAheadIsNotWaitedForAndOrcaHurries()
    {
        typeof(ClueMinigame).GetProperty("Current").SetValue(null, null);
        player.transform.position = new Vector3(20f, 0f, 0f);
        OrcaSpirit orca = OrcaSpirit.Create(player.scene, Vector3.zero, player.transform);
        try
        {
            orca.Lead(new Vector3(28f, 0f, 0f));
            orca.StepLead(1f);
            Assert.IsFalse(orca.IsWaitingForPlayer);
            Assert.Greater(orca.Anchor.x, OrcaSpirit.LeadSpeed + 0.01f);
        }
        finally { Object.DestroyImmediate(orca.gameObject); }
    }

    [Test]
    public void TheWayLeadsThroughTheStoryInOrder()
    {
        StoryDirector director = NewDirector();
        var stops = new GameObject("Stops");
        stops.transform.SetParent(root.transform);
        var away = new GameObject("Cachud (away)");
        away.transform.SetParent(stops.transform);
        away.SetActive(false);
        CachudCallout cachud = away.AddComponent<CachudCallout>();
        MagicianEncounter magician = Stop<MagicianEncounter>(stops, new Vector3(10f, 0f, 0f));
        TalkInteractable grave = Stop<TalkInteractable>(stops, new Vector3(20f, 0f, 0f));
        DolphinWordPuzzle statue = Stop<DolphinWordPuzzle>(stops, new Vector3(30f, 0f, 0f));
        var digObject = new GameObject("Dug site");
        digObject.transform.SetParent(stops.transform);
        digObject.SetActive(false);
        DigSite dig = digObject.AddComponent<DigSite>();
        typeof(DigSite).GetProperty("CurrentPhase").SetValue(dig, DigSite.Phase.Dug);
        Inject(director, "cachud", cachud);
        Inject(director, "magician", magician);
        Inject(director, "grave", grave);
        Inject(director, "dig", dig);
        Inject(director, "statue", statue);
        Inject(director, "ferry", null);

        Assert.IsTrue(director.TryGetDestination(out Vector3 goal));
        Assert.AreEqual(magician.transform.position, goal, "First the Magician.");
        typeof(MagicianEncounter).GetProperty("HasRequestedWand").SetValue(magician, true);
        Assert.IsTrue(director.TryGetDestination(out goal));
        Assert.AreEqual(grave.transform.position, goal, "Then Panther's grave.");
        typeof(TalkInteractable).GetProperty("HasSpoken").SetValue(grave, true);
        inventory.TryAdd(MagicianEncounter.WandItemName);
        Assert.IsTrue(director.TryGetDestination(out goal));
        Assert.AreEqual(magician.transform.position, goal, "With the wand, back to the Magician.");
        typeof(MagicianEncounter).GetProperty("HasRevealedSecret").SetValue(magician, true);
        Assert.IsTrue(director.TryGetDestination(out goal));
        Assert.AreEqual(statue.transform.position, goal, "Then the statue.");
        typeof(DolphinWordPuzzle).GetProperty("HasNamedKiller").SetValue(statue, true);
        Assert.IsFalse(director.TryGetDestination(out _), "Nowhere left to lead: he follows.");
    }

    private T Stop<T>(GameObject parent, Vector3 at) where T : Component
    {
        var stop = new GameObject(typeof(T).Name);
        stop.transform.SetParent(parent.transform);
        stop.transform.position = at;
        return stop.AddComponent<T>();
    }

    private static void Inject(StoryDirector director, string field, Object value) =>
        typeof(StoryDirector).GetField(field, Private).SetValue(director, value);

    private StoryDirector NewDirector()
    {
        var host = new GameObject("Director");
        host.transform.SetParent(root.transform);
        StoryDirector director = host.AddComponent<StoryDirector>();
        typeof(StoryDirector).GetMethod("Awake", Private).Invoke(director, null);
        typeof(StoryDirector).GetField("player", Private).SetValue(director, player.transform);
        return director;
    }

    private static void ReadUntil(DialogueRunner runner, DialogueRunner.State state)
    {
        for (int i = 0; i < 40 && runner.CurrentState != state; i++)
        {
            runner.Tick(10f, DialogueInput.None);
            if (runner.CurrentState == state) break;
            runner.Tick(0f, DialogueInput.Continue);
        }
        Assert.AreEqual(state, runner.CurrentState);
    }
}
