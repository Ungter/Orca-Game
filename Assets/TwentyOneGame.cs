using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(220)]
[DisallowMultipleComponent]
public class TwentyOneGame : MonoBehaviour
{
    public enum Phase { Closed, ChoosingOrder, Rules, PlayerRolling, OpponentRolling, Throwing, TakingTiles, Lost, Drat, Insult }
    public const string Keyword = "JERK";
    public const string OrderQuestion = "Can I go second?";
    public const string DratLine = "Drat!";
    public const string InsultLine = "You're a JERK!";
    public const float FaceDuration = 0.6f;
    public const float RulesDuration = 1.2f;
    public const float ThrowDuration = 1.1f;
    public const float TileDuration = 0.2f;
    public const float OpponentThinkDuration = 0.75f;

    [SerializeField] private Sprite[] tileSprites = new Sprite[4];
    private static TwentyOneGame active;
    public static bool IsOpen => active != null;
    public Phase CurrentPhase { get; private set; }
    public TwentyOneMatch Match { get; private set; } = new TwentyOneMatch(TwentyOneMatch.MinLimit);
    public float PhaseTime { get; private set; }
    public int Face { get; private set; } = 1;
    public int SelectedOrder { get; private set; }
    public int VisibleCharacters { get; private set; }
    public bool OpponentGoesSecond { get; private set; }
    public bool IsSolved { get; private set; }
    public bool KeywordRecorded { get; private set; }
    // Where the counter is along the tiles: it stays put while the die is in the air, then hops
    // one tile per TileDuration.
    public float TokenProgress => CurrentPhase == Phase.Throwing ? beforeRoll :
        CurrentPhase == Phase.TakingTiles ? Mathf.Min(Match.Total, beforeRoll + PhaseTime / TileDuration) : Match.Total;
    public int VisibleTotal => Mathf.FloorToInt(TokenProgress);
    public float ThrowProgress => CurrentPhase == Phase.Throwing ? Mathf.Clamp01(PhaseTime / ThrowDuration) : 1f;
    public bool IsRolling => CurrentPhase == Phase.PlayerRolling || CurrentPhase == Phase.OpponentRolling;
    public string Speech => CurrentPhase == Phase.Drat ? DratLine : InsultLine;
    public TwentyOneView View => view;

    private TwentyOneView view;
    private CameraFollow follow;
    private bool followWasEnabled;
    private CursorLockMode cursorLock;
    private bool cursorVisible;
    private int beforeRoll;
    private int opponentDecision;
    private int dealtLimit;
    private float typed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => active = null;

    public bool Begin(CachudCallout owner, Transform player, Camera camera = null)
    {
        if (!isActiveAndEnabled || IsOpen || Interactables.Busy || TitleScreen.IsOpen ||
            InventoryInspection.IsOpen || PauseMenu.IsPaused || owner == null || !owner.IsReadyForGame ||
            player == null || player.gameObject.scene != gameObject.scene || !HasTileArt()) return false;
        if (view == null) view = new TwentyOneView(transform, tileSprites, owner.GetComponent<SpriteRenderer>().sprite);
        if (camera == null) camera = Camera.main;
        follow = camera != null ? camera.GetComponent<CameraFollow>() : null;
        followWasEnabled = follow != null && follow.enabled;
        if (follow != null) follow.enabled = false;
        cursorLock = Cursor.lockState;
        cursorVisible = Cursor.visible;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        active = this;
        ChooseAgain();
        view.Show(camera);
        view.Refresh(this);
        return true;
    }

    private bool HasTileArt()
    {
        if (tileSprites == null || tileSprites.Length != 4) return false;
        foreach (Sprite sprite in tileSprites) if (sprite == null) return false;
        return true;
    }

    private void Update()
    {
        if (active != this || PauseMenu.IsPaused) return;
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            Close();
            return;
        }
        bool enter = keyboard != null &&
            (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame);
        if (CurrentPhase == Phase.ChoosingOrder)
        {
            if (keyboard != null && (keyboard.leftArrowKey.wasPressedThisFrame || keyboard.rightArrowKey.wasPressedThisFrame ||
                keyboard.aKey.wasPressedThisFrame || keyboard.dKey.wasPressedThisFrame)) SelectedOrder = 1 - SelectedOrder;
            if (keyboard != null && keyboard.digit1Key.wasPressedThisFrame) ChooseOrder(true);
            else if (keyboard != null && keyboard.digit2Key.wasPressedThisFrame) ChooseOrder(false);
            else if (enter) ChooseOrder(SelectedOrder == 0);
            else if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                int choice = view.OrderChoiceAt(mouse.position.ReadValue());
                if (choice >= 0) ChooseOrder(choice == 0);
            }
            view.Refresh(this);
            return;
        }
        bool stop = keyboard != null && keyboard.spaceKey.wasPressedThisFrame;
        Tick(Time.unscaledDeltaTime, stop, enter);
    }

    public bool ChooseOrder(bool letHimGoSecond)
    {
        if (active != this || CurrentPhase != Phase.ChoosingOrder) return false;
        OpponentGoesSecond = letHimGoSecond;
        Match.Start(letHimGoSecond);
        Face = 1;
        SetPhase(Phase.Rules);
        view.Refresh(this);
        return true;
    }

    public void Tick(float deltaTime, bool stop = false, bool advance = false)
    {
        if (active != this || PauseMenu.IsPaused) return;
        float dt = Mathf.Max(0f, deltaTime);
        PhaseTime += dt;
        if (CurrentPhase == Phase.Rules && PhaseTime >= RulesDuration) BeginTurn();
        else if (CurrentPhase == Phase.PlayerRolling || CurrentPhase == Phase.OpponentRolling)
        {
            if (CurrentPhase == Phase.PlayerRolling && stop) TakeRoll(Face);
            else
            {
                Face = 1 + Mathf.FloorToInt(PhaseTime / FaceDuration) % 3;
                if (CurrentPhase == Phase.OpponentRolling &&
                    PhaseTime >= OpponentThinkDuration && Face == opponentDecision) TakeRoll(Face);
            }
        }
        else if (CurrentPhase == Phase.Throwing && PhaseTime >= ThrowDuration) SetPhase(Phase.TakingTiles);
        else if (CurrentPhase == Phase.TakingTiles &&
                 PhaseTime >= (Match.Total - beforeRoll + 1) * TileDuration)
        {
            if (!Match.Finished) BeginTurn();
            else if (Match.Loser == TwentyOneMatch.Side.Player) SetPhase(Phase.Lost);
            else
            {
                IsSolved = true;
                SetPhase(Phase.Drat);
            }
        }
        else if (CurrentPhase == Phase.Lost && advance) ChooseAgain();
        else if (CurrentPhase == Phase.Drat || CurrentPhase == Phase.Insult)
        {
            bool finished = VisibleCharacters >= Speech.Length;
            typed = advance ? Speech.Length : Mathf.Min(Speech.Length, typed + dt * 34f);
            VisibleCharacters = Mathf.FloorToInt(typed);
            if (CurrentPhase == Phase.Insult && VisibleCharacters == Speech.Length) RememberKeyword();
            if (advance && finished)
            {
                if (CurrentPhase == Phase.Drat) SetPhase(Phase.Insult);
                else Close();
            }
        }
        if (active == this) view.Refresh(this);
    }

    private void BeginTurn()
    {
        if (Match.Turn == TwentyOneMatch.Side.Player) SetPhase(Phase.PlayerRolling);
        else
        {
            opponentDecision = Match.OpponentRoll();
            SetPhase(Phase.OpponentRolling);
        }
        Face = 1;
    }

    private void TakeRoll(int face)
    {
        beforeRoll = Match.Total;
        if (Match.Take(Match.Turn, face)) SetPhase(Phase.Throwing);
    }

    // Every offer, retries included, deals a last tile different from the one before. He always
    // asks to go second, but that only wins when the last tile is one more than a multiple of four.
    private void ChooseAgain()
    {
        Match = new TwentyOneMatch(NextLimit(dealtLimit));
        dealtLimit = Match.Limit;
        SelectedOrder = 0;
        Face = 1;
        SetPhase(Phase.ChoosingOrder);
    }

    // Any last tile from 20 to 30 except the previous one (0 for none).
    public static int NextLimit(int previous)
    {
        bool hadOne = previous >= TwentyOneMatch.MinLimit && previous <= TwentyOneMatch.MaxLimit;
        int limit = Random.Range(TwentyOneMatch.MinLimit, TwentyOneMatch.MaxLimit + (hadOne ? 0 : 1));
        return hadOne && limit >= previous ? limit + 1 : limit;
    }

    private void SetPhase(Phase next)
    {
        CurrentPhase = next;
        PhaseTime = typed = 0f;
        VisibleCharacters = 0;
    }

    private void RememberKeyword()
    {
        if (Match.Winner != TwentyOneMatch.Side.Player || InventorySystem.instance == null) return;
        InventorySystem.instance.RevealClueWords(Keyword);
        KeywordRecorded = true;
    }

    public void Close()
    {
        if (active != this) return;
        // A won clue must survive Escape or a scene exit during the victory dialogue.
        if (Match.Winner == TwentyOneMatch.Side.Player) RememberKeyword();
        active = null;
        CurrentPhase = Phase.Closed;
        if (view != null) view.Hide();
        if (follow != null) follow.enabled = followWasEnabled;
        Cursor.lockState = cursorLock;
        Cursor.visible = cursorVisible;
    }

    private void OnDisable() => Close();

    private void OnDestroy()
    {
        Close();
        view?.Dispose();
        view = null;
    }
}
