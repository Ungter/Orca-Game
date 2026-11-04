using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(200)]
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public class CachudCallout : MonoBehaviour, IInteractable
{
    public enum Phase { Idle, Panning, Calling, Typing, Waiting, Questions, Returning, Complete }

    public const string DiceItemName = "Dice";
    public const string Greeting = "Want to play a game?";
    public const string MissingDice = "Oh no! I left my dice somewhere.";
    public const string LastLocationQuestion = "Where were you last?";
    public const string FountainClue = "At the fountain, throwing coins.";
    public const string Thanks = "You found my dice! Thanks!";
    public const string Ready = "All right, let's play!";
    public const float PanDuration = 1.25f;
    public const float CallDuration = 3f;
    public const float ReturnDuration = 1.2f;
    private const float FadeDuration = 0.25f;

    [SerializeField] private Transform player;
    [SerializeField] private Transform landmark;
    [SerializeField, Min(1f)] private float calloutRadius = 10f;
    [SerializeField, Min(1f)] private float talkRadius = 3f;
    [SerializeField, TextArea] private string line = "HEY, YOU! OVER HERE!";
    [SerializeField, Min(1f)] private float charactersPerSecond = 40f;
    [SerializeField] private TwentyOneGame diceGame;

    private static CachudCallout active;
    public static bool IsOpen => active != null;
    public Phase CurrentPhase { get; private set; }
    public bool HasCalledOut { get; private set; }
    public bool HasRequestedDice { get; private set; }
    public bool HasLearnedLocation { get; private set; }
    public bool HasDice { get; private set; }
    public bool IsReadyForGame => HasDice;
    // Start the game only after the conversation has restored its camera and controls.
    public event System.Action GameRequested;
    public static System.Collections.Generic.IReadOnlyList<string> QuestionOptions { get; } =
        System.Array.AsReadOnly(new[] { LastLocationQuestion });
    public Transform Landmark => landmark;
    public string Text => line;
    public float Radius => calloutRadius;
    public string CurrentLine { get; private set; }
    public int VisibleCharacters { get; private set; }

    private Camera view;
    private CameraFollow follow;
    private PlayerController playerController;
    private bool followWasEnabled;
    private bool automaticCallout;
    private bool requestGameAfterReturn;
    private float phaseTime;
    private float typed;
    private float promptAlpha;
    private string wrappedLine;
    private Rect questionRect;
    private InventorySystem inventory;
    private Vector3 originalPosition;
    private Quaternion originalRotation;
    private Vector3 shotPosition;
    private Quaternion shotRotation;
    private Vector3 returnPosition;
    private Quaternion returnRotation;

    public float BarsAmount => CurrentPhase == Phase.Panning ? Ease(phaseTime / FadeDuration) :
        CurrentPhase == Phase.Calling || IsDialogue ? 1f :
        CurrentPhase == Phase.Returning ? 1f - Ease(phaseTime / ReturnDuration) : 0f;

    public float MessageOpacity => CurrentPhase == Phase.Panning ? (automaticCallout ? Ease(phaseTime / FadeDuration) : 0f) :
        CurrentPhase == Phase.Calling ? Ease((CallDuration - phaseTime) / FadeDuration) :
        IsDialogue ? 1f : 0f;

    private bool IsDialogue => CurrentPhase == Phase.Typing || CurrentPhase == Phase.Waiting ||
        CurrentPhase == Phase.Questions;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => active = null;

    private void OnEnable() => Interactables.Add(this);

    private void Update()
    {
        if (PauseMenu.IsPaused) return;
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        if (active == this)
        {
            bool enter = keyboard != null &&
                (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame);
            bool click = mouse != null && mouse.leftButton.wasPressedThisFrame;
            bool choose = CurrentPhase == Phase.Questions &&
                (enter || (keyboard != null && keyboard.digit1Key.wasPressedThisFrame) ||
                 (click && questionRect.Contains(MousePosition())));
            bool escape = keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
            Tick(Time.unscaledDeltaTime, enter || (click && CurrentPhase != Phase.Questions), choose, escape);
            return;
        }
        if (CurrentPhase == Phase.Idle && TryBegin()) return;
        bool canTalk = CanBegin() && Interactables.IsNearest(this);
        promptAlpha = Mathf.MoveTowards(promptAlpha, canTalk ? 1f : 0f, Time.unscaledDeltaTime * 8f);
        if (canTalk && keyboard != null && keyboard.eKey.wasPressedThisFrame) TryTalk();
    }

    public bool TryBegin()
    {
        if (HasCalledOut || CurrentPhase != Phase.Idle) return false;
        return Begin(calloutRadius, true);
    }

    public bool TryTalk() => Begin(talkRadius, false);

    private bool CanBegin() => isActiveAndEnabled && !TitleScreen.IsOpen && !PauseMenu.IsPaused &&
        !InventoryInspection.IsOpen && !Interactables.Busy &&
        (CurrentPhase == Phase.Idle || CurrentPhase == Phase.Complete);

    public float InteractDistanceSqr()
    {
        if (player == null) player = Interactables.FindPlayer();
        if (!isActiveAndEnabled || player == null || player.gameObject.scene != gameObject.scene)
            return float.MaxValue;
        Vector3 delta = player.position - transform.position;
        if (Mathf.Abs(delta.y) > 3f) return float.MaxValue;
        float distance = delta.x * delta.x + delta.z * delta.z;
        return distance <= talkRadius * talkRadius ? distance : float.MaxValue;
    }

    private bool Begin(float radius, bool callOut)
    {
        if (!CanBegin()) return false;
        if (player == null) player = Interactables.FindPlayer();
        if (player == null || player.gameObject.scene != gameObject.scene) return false;
        Vector3 distance = player.position - transform.position;
        if (Mathf.Abs(distance.y) > 3f) return false;
        distance.y = 0f;
        if (distance.sqrMagnitude > radius * radius) return false;
        if (view == null) view = Camera.main;
        if (view == null) return false;

        originalPosition = view.transform.position;
        originalRotation = view.transform.rotation;
        playerController = player.GetComponent<PlayerController>();
        follow = view.GetComponent<CameraFollow>();
        followWasEnabled = follow != null && follow.enabled;
        if (follow != null) follow.enabled = false;
        CalculateShot(distance.normalized);
        inventory = InventorySystem.instance;
        automaticCallout = callOut;
        requestGameAfterReturn = false;
        questionRect = default;
        promptAlpha = 0f;
        active = this;
        HasCalledOut = true;
        SetPhase(Phase.Panning);
        return true;
    }

    private void CalculateShot(Vector3 towardsPlayer)
    {
        if (towardsPlayer.sqrMagnitude < 0.01f)
            towardsPlayer = Vector3.ProjectOnPlane(view.transform.position - transform.position, Vector3.up).normalized;
        if (towardsPlayer.sqrMagnitude < 0.01f) towardsPlayer = Vector3.back;
        Vector3 focus = transform.position + Vector3.up * 1.1f;
        // A nearby player would fill the original approach shot. Move to the
        // speaker's side instead, keeping him in front of the player on return visits.
        bool close = Vector3.ProjectOnPlane(player.position - transform.position, Vector3.up).sqrMagnitude < 16f;
        Vector3 desired = transform.position + towardsPlayer * (close ? -2.5f : 6.2f) +
            Vector3.Cross(Vector3.up, towardsPlayer) * (close ? 6f : 1.25f) + Vector3.up * 2.7f;
        Vector3 direction = desired - focus;
        float clearDistance = direction.magnitude;
        direction.Normalize();
        foreach (RaycastHit hit in Physics.SphereCastAll(focus, 0.2f, direction, clearDistance,
                     Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.transform.IsChildOf(transform) || hit.collider.transform.IsChildOf(player)) continue;
            clearDistance = Mathf.Min(clearDistance, Mathf.Max(0.5f, hit.distance - 0.15f));
        }
        shotPosition = focus + direction * clearDistance;
        shotRotation = Quaternion.LookRotation(focus - shotPosition, Vector3.up);
    }

    public void Tick(float deltaTime, bool advance = false, bool chooseQuestion = false, bool escape = false)
    {
        if (active != this || PauseMenu.IsPaused) return;
        if (view == null || player == null)
        {
            Cancel();
            return;
        }
        if (escape && CurrentPhase != Phase.Returning)
        {
            requestGameAfterReturn = false;
            BeginReturn();
            return;
        }
        float dt = Mathf.Max(0f, deltaTime);
        phaseTime += dt;
        if (CurrentPhase == Phase.Panning && phaseTime >= PanDuration)
        {
            if (automaticCallout) SetPhase(Phase.Calling);
            else StartDialogue();
        }
        else if (CurrentPhase == Phase.Calling && phaseTime >= CallDuration)
            StartDialogue();
        else if (CurrentPhase == Phase.Typing)
        {
            typed = advance ? wrappedLine.Length : Mathf.Min(wrappedLine.Length, typed + dt * charactersPerSecond);
            VisibleCharacters = Mathf.FloorToInt(typed);
            if (VisibleCharacters >= wrappedLine.Length)
            {
                if (CurrentLine == MissingDice) HasRequestedDice = true;
                SetPhase(Phase.Waiting);
            }
        }
        else if (CurrentPhase == Phase.Waiting && advance) AdvanceLine();
        else if (CurrentPhase == Phase.Questions && chooseQuestion) ChooseQuestion(0);
        else if (CurrentPhase == Phase.Returning && phaseTime >= ReturnDuration)
        {
            RestoreFollow();
            if (!followWasEnabled || follow == null)
                view.transform.SetPositionAndRotation(originalPosition, originalRotation);
            active = null;
            SetPhase(Phase.Complete);
            bool startGame = requestGameAfterReturn;
            requestGameAfterReturn = false;
            if (startGame)
            {
                GameRequested?.Invoke();
                if (diceGame != null) diceGame.Begin(this, player, view);
            }
        }
    }

    private void StartDialogue()
    {
        ShowLine(HasDice ? Ready :
            inventory != null && inventory.Contains(DiceItemName) ? Thanks :
            HasRequestedDice ? MissingDice : Greeting);
    }

    private void ShowLine(string message)
    {
        CurrentLine = message;
        wrappedLine = string.Join("\n", PixelFont.Wrap(message, 200));
        typed = 0f;
        VisibleCharacters = 0;
        SetPhase(Phase.Typing);
    }

    private void AdvanceLine()
    {
        if (CurrentLine == Greeting) ShowLine(MissingDice);
        else if (CurrentLine == MissingDice) SetPhase(Phase.Questions);
        else if (CurrentLine == FountainClue) BeginReturn();
        else if (CurrentLine == Thanks)
        {
            if (inventory != null && inventory.TryRemove(DiceItemName))
            {
                HasDice = true;
                ShowLine(Ready);
            }
            else ShowLine(MissingDice);
        }
        else if (CurrentLine == Ready && HasDice)
        {
            requestGameAfterReturn = true;
            BeginReturn();
        }
    }

    public bool ChooseQuestion(int index)
    {
        if (active != this || CurrentPhase != Phase.Questions || index != 0) return false;
        HasLearnedLocation = true;
        ShowLine(FountainClue);
        return true;
    }

    private void BeginReturn()
    {
        returnPosition = view.transform.position;
        returnRotation = view.transform.rotation;
        RestoreFollow();
        SetPhase(Phase.Returning);
    }

    private void LateUpdate()
    {
        if (active == this && view != null)
        {
            if (CurrentPhase == Phase.Panning)
            {
                float t = Ease(phaseTime / PanDuration);
                Vector3 position = Vector3.Lerp(originalPosition, shotPosition, t) +
                    Vector3.up * (Mathf.Sin(t * Mathf.PI) * 0.6f);
                view.transform.SetPositionAndRotation(position, Quaternion.Slerp(originalRotation, shotRotation, t));
            }
            else if (CurrentPhase == Phase.Calling || IsDialogue)
                view.transform.SetPositionAndRotation(shotPosition, shotRotation);
            else if (CurrentPhase == Phase.Returning)
            {
                // The follow rig runs first and supplies the live destination each frame.
                Vector3 destination = followWasEnabled && follow != null ? view.transform.position : originalPosition;
                Quaternion rotation = followWasEnabled && follow != null ? view.transform.rotation : originalRotation;
                float t = Ease(phaseTime / ReturnDuration);
                view.transform.SetPositionAndRotation(Vector3.Lerp(returnPosition, destination, t),
                    Quaternion.Slerp(returnRotation, rotation, t));
            }
            if (playerController != null) playerController.AnimateCinematicWalk(Vector3.zero, 0f);
        }

        Camera camera = view != null ? view : Camera.main;
        if (camera != null)
            transform.rotation = Quaternion.Euler(0f, camera.transform.eulerAngles.y, 0f);
    }

    private void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;
        if (active != this)
        {
            InteractPrompt.Draw(Interactables.TopCentre(gameObject), 12f, "E", "Talk", 3, promptAlpha);
            return;
        }
        int previousDepth = GUI.depth;
        GUI.depth = -900;
        CinematicOverlay.DrawBars(BarsAmount);
        if (IsDialogue)
            questionRect = CinematicOverlay.DrawDialogue(wrappedLine, VisibleCharacters,
                CurrentPhase == Phase.Waiting, CurrentPhase == Phase.Questions ? LastLocationQuestion : null);
        else CinematicOverlay.DrawMessage(line, MessageOpacity);
        GUI.depth = previousDepth;
    }

    public void Cancel()
    {
        if (active != this) return;
        if (view != null) view.transform.SetPositionAndRotation(originalPosition, originalRotation);
        RestoreFollow();
        requestGameAfterReturn = false;
        active = null;
        SetPhase(Phase.Complete);
    }

    private void OnDisable()
    {
        Interactables.Remove(this);
        Cancel();
    }

    private void RestoreFollow()
    {
        if (follow != null) follow.enabled = followWasEnabled;
    }

    private void SetPhase(Phase phase)
    {
        CurrentPhase = phase;
        phaseTime = 0f;
    }

    private static float Ease(float t) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));

    private static Vector2 MousePosition()
    {
        Vector2 position = Mouse.current.position.ReadValue();
        return new Vector2(position.x, Screen.height - position.y);
    }
}
