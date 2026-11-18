using System;
using UnityEngine;
using UnityEngine.SceneManagement;

// The story around the minigames: Orca waits by the dock while the boat comes in, greets the
// player once they are ashore (StoryScript.Arrival), asks their name and whether he may help.
// Accepted, he leads the player to whoever the story needs next (the player follows him) and
// joins every conversation; declined, he leaves, and comes
// back to wait at the Orca statue once the player holds all three clue words. It also plays the
// conversations that belong to no one character (the statue, the killer's name).
[DefaultExecutionOrder(-60)]
public class StoryDirector : MonoBehaviour
{
    public const string DefaultName = "Alethi";
    public static readonly string[] ClueWords = { "FELL", "JERK", "THIEF" };

    private static string playerName = DefaultName;
    private static bool following;

    public static StoryDirector Current { get; private set; }
    public static string PlayerName => playerName;
    // Orca travels with the player and speaks up in conversations.
    public static bool OrcaFollowing => following;
    // The player answered Orca's offer with NO: the doors' beacons light the way instead of him.
    public static bool ExploringAlone { get; private set; }
    public static bool IsTalking => Current != null && Current.runner.IsActive;

    public bool HasArrived { get; private set; }
    public bool OrcaWaitsAtStatue { get; private set; }
    public bool HasMetAtStatue { get; private set; }
    public OrcaSpirit Orca => orca;
    public DialogueRunner Runner => runner;

    private readonly DialogueRunner runner = new DialogueRunner();
    private OrcaSpirit orca;
    private Transform player;
    private bool sawTitle;
    private float nextGuide;
    private bool reachedDock;
    private CachudCallout cachud;
    private AlchemistEncounter alchemist;
    private MagicianEncounter magician;
    private TalkInteractable grave;
    private DigSite dig;
    private DolphinWordPuzzle statue;
    private LighthouseFerry ferry;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Current = null;
        playerName = DefaultName;
        following = false;
        ExploringAlone = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Single) Bootstrap();
    }

    // Gameplay scenes only (the ones with a follow camera), once per load.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindAnyObjectByType<CameraFollow>() != null && FindAnyObjectByType<StoryDirector>() == null)
            new GameObject("Story director", typeof(StoryDirector));
    }

    // Blank or unprintable names fall back to Alethi.
    public static void SetPlayerName(string name)
    {
        string clean = "";
        foreach (char c in (name ?? "").Trim())
            if (PixelFont.HasGlyph(c) && clean.Length < DialogueRunner.MaxNameLength) clean += c;
        playerName = clean.Trim().Length > 0 ? clean.Trim() : DefaultName;
    }

    public static void SetFollowing(bool on)
    {
        following = on;
        if (Current == null || Current.orca == null) return;
        if (on) Current.orca.Follow();
    }

    // A conversation over the game itself. False (and nothing played) without a director.
    public static bool Say(Speaker npc, DialogueLine[] lines, Action done = null)
    {
        if (Current == null) return false;
        Current.runner.Play(npc, lines, done);
        return true;
    }

    // Orca waiting at the statue speaks first; then the statue opens. True when it did.
    public static bool InterceptStatue(Action open)
    {
        StoryDirector story = Current;
        if (story == null || !story.OrcaWaitsAtStatue || story.HasMetAtStatue || story.runner.IsActive) return false;
        story.runner.Escapable = false;
        story.runner.Play(Speaker.Orca, StoryScript.StatueMeeting, () =>
        {
            story.HasMetAtStatue = true;
            SetFollowing(true);
            open?.Invoke();
        });
        return true;
    }

    private void Awake()
    {
        Current = this;
        playerName = DefaultName;
        following = false;
        ExploringAlone = false;
    }

    private void OnDestroy()
    {
        if (Current == this) Current = null;
        if (orca == null) return;
        if (Application.isPlaying) Destroy(orca.gameObject);
        else DestroyImmediate(orca.gameObject);
    }

    private void Update()
    {
        if (GameplayDebugMenu.BlocksInput || PauseMenu.IsPaused) return;
        if (player == null) player = Interactables.FindPlayer();
        if (orca == null && TitleScreen.ActiveVoyage != null) SpawnAtDock(TitleScreen.ActiveVoyage);
        if (TitleScreen.IsOpen)
        {
            sawTitle = true;
            return;
        }
        Guide();

        if (runner.IsActive)
        {
            DialogueInput input = DialogueInput.Read();
            if (input.Escape && runner.Escapable && runner.IsWaiting && runner.CurrentState == DialogueRunner.State.Waiting)
                runner.Stop();
            else runner.Tick(Time.unscaledDeltaTime, input);
            return;
        }

        if (!HasArrived && sawTitle && player != null && !Interactables.Busy) BeginArrival();
        else if (HasArrived && !following && !OrcaWaitsAtStatue && HoldsEveryClue()) WaitAtStatue();
    }

    private void SpawnAtDock(OpeningVoyage voyage)
    {
        Vector3 inland = voyage.Inland - voyage.Landing;
        inland.y = 0f;
        Vector3 at = voyage.Landing + inland.normalized * 3.5f + Vector3.Cross(Vector3.up, inland.normalized) * 0.8f;
        orca = OrcaSpirit.Create(gameObject.scene, at, player);
    }

    public void BeginArrival()
    {
        HasArrived = true;
        if (orca == null && player != null)
        {
            Camera view = Camera.main;
            Vector3 ahead = view != null ? Vector3.ProjectOnPlane(view.transform.forward, Vector3.up).normalized : Vector3.forward;
            orca = OrcaSpirit.Create(gameObject.scene, player.position + ahead * 3f, player);
        }
        runner.Escapable = false;
        runner.Play(Speaker.Orca, StoryScript.Arrival, null, StoryScript.AcceptHelp, AnswerHelp);
    }

    public void AnswerHelp(int choice)
    {
        bool accepted = choice == 0;
        ExploringAlone = !accepted;
        runner.Escapable = false;
        runner.Play(Speaker.Orca, accepted ? StoryScript.HelpAccepted : StoryScript.HelpDeclined, () =>
        {
            runner.Escapable = true;
            if (accepted) SetFollowing(true);
            else if (orca != null) orca.Vanish();
        });
    }

    // While Orca travels along, he leads the way to the story's next stop; with nowhere to lead
    // (the potion hunt, the lighthouse) he follows the player instead.
    private void Guide()
    {
        if (!following || orca == null || orca.CurrentMode == OrcaSpirit.Mode.Vanishing) return;
        if (Time.unscaledTime < nextGuide) return;
        nextGuide = Time.unscaledTime + 0.5f;
        if (TryGetDestination(out Vector3 goal)) orca.Lead(goal);
        else if (orca.CurrentMode != OrcaSpirit.Mode.Following) orca.Follow();
    }

    // The next stop in story-script.md's order: Al Cachud (and the fountain for his dice, then the
    // game of 21), Iguana (and back with the potions), the Magician, Panther's grave, the dig site,
    // the Magician again, the Orca statue and finally the boat to the lighthouse.
    public bool TryGetDestination(out Vector3 goal)
    {
        goal = default;
        FindStops();
        InventorySystem inventory = InventorySystem.instance;
        bool Has(string item) => inventory != null && inventory.Contains(item);

        if (cachud != null && cachud.isActiveAndEnabled)
        {
            if (!cachud.HasRequestedDice) return At(cachud.MeetingPoint, out goal);
            if (!cachud.HasDice && !Has(CachudCallout.DiceItemName))
            {
                PickupItem dice = FindPickup(CachudCallout.DiceItemName);
                if (dice != null) return At(dice.transform.position, out goal);
            }
            else if (!cachud.HasDice || inventory == null || !inventory.HasRevealedWord(TwentyOneGame.Keyword))
                return At(cachud.MeetingPoint, out goal);
        }

        ClueMinigame clue = ClueMinigame.Current;
        if (alchemist != null)
        {
            if (!alchemist.HasGivenLetter || clue != null && clue.IsFound) return At(alchemist.transform.position, out goal);
            // The hunt itself is the player's to lead.
            if (clue != null && !clue.IsDelivered) return false;
        }

        if (magician != null)
        {
            if (!magician.HasRequestedWand) return At(magician.transform.position, out goal);
            if (!magician.HasWand && !Has(MagicianEncounter.WandItemName))
            {
                if (grave != null && !grave.HasSpoken) return At(grave.transform.position, out goal);
                if (dig != null && !dig.IsDug) return At(dig.transform.position, out goal);
                PickupItem wand = FindPickup(MagicianEncounter.WandItemName);
                if (wand != null) return At(wand.transform.position, out goal);
            }
            if (!magician.HasRevealedSecret) return At(magician.transform.position, out goal);
        }

        if (statue != null && !statue.HasNamedKiller) return At(statue.transform.position, out goal);

        if (ferry != null && !reachedDock && player != null)
        {
            Vector3 toBoat = ferry.transform.position - player.position;
            toBoat.y = 0f;
            if (toBoat.magnitude < 6f) reachedDock = true;
            else return At(ferry.transform.position, out goal);
        }
        return false;
    }

    private static bool At(Vector3 point, out Vector3 goal)
    {
        goal = point;
        return true;
    }

    private void FindStops()
    {
        if (cachud == null) cachud = FindAnyObjectByType<CachudCallout>();
        if (alchemist == null) alchemist = FindAnyObjectByType<AlchemistEncounter>();
        if (magician == null) magician = FindAnyObjectByType<MagicianEncounter>();
        if (grave == null) grave = FindAnyObjectByType<TalkInteractable>();
        if (dig == null) dig = FindAnyObjectByType<DigSite>();
        if (statue == null) statue = FindAnyObjectByType<DolphinWordPuzzle>();
        if (ferry == null) ferry = FindAnyObjectByType<LighthouseFerry>();
    }

    private static PickupItem FindPickup(string item)
    {
        foreach (PickupItem pickup in FindObjectsByType<PickupItem>(FindObjectsSortMode.None))
            if (pickup.ItemName == item && pickup.isActiveAndEnabled) return pickup;
        return null;
    }

    public static bool HoldsEveryClue()
    {
        InventorySystem inventory = InventorySystem.instance;
        if (inventory == null) return false;
        foreach (string word in ClueWords) if (!inventory.HasRevealedWord(word)) return false;
        return true;
    }

    private void WaitAtStatue()
    {
        DolphinWordPuzzle statue = FindAnyObjectByType<DolphinWordPuzzle>();
        if (statue == null) return;
        OrcaWaitsAtStatue = true;
        Vector3 at = statue.OrcaWaitingSpot(player != null ? player.position : statue.transform.position);
        if (orca == null) orca = OrcaSpirit.Create(gameObject.scene, at, player);
        orca.WaitAt(at);
    }

    private void OnGUI()
    {
        if (!runner.IsActive) return;
        int depth = GUI.depth;
        GUI.depth = -900;
        runner.Draw();
        GUI.depth = depth;
    }
}
