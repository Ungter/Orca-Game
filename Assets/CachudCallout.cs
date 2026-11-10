using UnityEngine;
using UnityEngine.InputSystem;

// Al Cachud. He keeps to his house (pasted__building2) until the player knocks on its door
// (pasted__pCube54, E): the camera pans to the door, it swings open, he walks out
// (CachudDoorway) and the conversation starts - the dice he lost, where he was last, the
// fountain - and once the dice are back, a game of twenty-one. Afterwards he stays outside the
// door and E talks to him again. Without the door in the scene he simply talks where he stands.
[DefaultExecutionOrder(200)]
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public class CachudCallout : MonoBehaviour, IInteractable
{
    public enum Phase { Idle, Panning, Emerging, Typing, Waiting, Questions, Returning, Complete }

    public const string DiceItemName = "Dice";
    public const string Greeting = "Want to play a game?";
    public const string MissingDice = "Oh no! I left my dice somewhere.";
    public const string LastLocationQuestion = "Where were you last?";
    public const string FountainClue = "At the fountain, throwing coins.";
    public const string Thanks = "You found my dice! Thanks!";
    public const string Ready = "All right, let's play!";
    public const string BusyLine = "AL CACHUD: You look busy. Come back when you are done.";
    public const float PanDuration = 1.25f;
    public const float ReturnDuration = 1.2f;
    private const float FadeDuration = 0.25f;

    [SerializeField] private Transform player;
    [SerializeField, Min(1f)] private float talkRadius = 3f;
    [SerializeField, Min(1f)] private float charactersPerSecond = 40f;
    [SerializeField] private TwentyOneGame diceGame;

    private static CachudCallout active;
    public static bool IsOpen => active != null;
    public Phase CurrentPhase { get; private set; }
    // He has come out of his house (or, without the door, been talked to).
    public bool HasComeOut { get; private set; }
    public bool HasRequestedDice { get; private set; }
    public bool HasLearnedLocation { get; private set; }
    public bool HasDice { get; private set; }
    public bool IsReadyForGame => HasDice;
    // Start the game only after the conversation has restored its camera and controls.
    public event System.Action GameRequested;
    public static System.Collections.Generic.IReadOnlyList<string> QuestionOptions { get; } =
        System.Array.AsReadOnly(new[] { LastLocationQuestion });
    public string CurrentLine { get; private set; }
    public int VisibleCharacters { get; private set; }

    private Camera view;
    private CameraFollow follow;
    private PlayerController playerController;
    private bool followWasEnabled;
    private bool requestGameAfterReturn;
    private float phaseTime;
    private float typed;
    private float promptAlpha;
    private string wrappedLine;
    private Rect questionRect;
    private InventorySystem inventory;
    private Transform door;
    private CachudDoorway doorway;
    private Vector3 originalPosition;
    private Quaternion originalRotation;
    private Vector3 shotPosition;
    private Quaternion shotRotation;
    private Vector3 returnPosition;
    private Quaternion returnRotation;
    private Vector3 dialogueStartPosition;
    private Quaternion dialogueStartRotation;
    private float shotBlend = 1f;

    public float BarsAmount => CurrentPhase == Phase.Panning ? Ease(phaseTime / FadeDuration) :
        CurrentPhase == Phase.Emerging || IsDialogue ? 1f :
        CurrentPhase == Phase.Returning ? 1f - Ease(phaseTime / ReturnDuration) : 0f;

    // Before he has come out, E is for knocking on his door rather than talking to him.
    private bool AtDoor => !HasComeOut && door != null;

    private bool IsDialogue => CurrentPhase == Phase.Typing || CurrentPhase == Phase.Waiting ||
        CurrentPhase == Phase.Questions;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => active = null;

    private void OnEnable() => Interactables.Add(this);

    // He waits indoors, unseen and out of the way, until the door is knocked on.
    private void Start()
    {
        GameObject map = GameObject.Find(CachudDoorway.MapName);
        door = CachudDoorway.FindDoor(map != null ? map.transform : null);
        if (AtDoor) SetPresent(false);
    }

    private void SetPresent(bool present)
    {
        GetComponent<SpriteRenderer>().enabled = present;
        foreach (Collider body in GetComponentsInChildren<Collider>(true)) body.enabled = present;
    }

    private void Update()
    {
        if (GameplayDebugMenu.BlocksInput) return;
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
        bool canTalk = CanBegin() && Interactables.IsNearest(this);
        promptAlpha = Mathf.MoveTowards(promptAlpha, canTalk ? 1f : 0f, Time.unscaledDeltaTime * 8f);
        if (canTalk && keyboard != null && keyboard.eKey.wasPressedThisFrame)
        {
            if (AtDoor) TryKnock();
            else TryTalk();
        }
    }

    // The first meeting: knocking on his door brings him out (straight to talking without one).
    public bool TryKnock() => !HasComeOut && Begin(true);

    public bool TryTalk() => (HasComeOut || door == null) && Begin(false);

    private bool CanBegin() => isActiveAndEnabled && !TitleScreen.IsOpen && !PauseMenu.IsPaused &&
        !InventoryInspection.IsOpen && !Interactables.Busy &&
        (CurrentPhase == Phase.Idle || CurrentPhase == Phase.Complete);

    public float InteractDistanceSqr()
    {
        if (player == null) player = Interactables.FindPlayer();
        if (!isActiveAndEnabled || player == null || player.gameObject.scene != gameObject.scene)
            return float.MaxValue;
        Vector3 delta = player.position - Anchor();
        if (Mathf.Abs(delta.y) > 3f) return float.MaxValue;
        float distance = delta.x * delta.x + delta.z * delta.z;
        return distance <= talkRadius * talkRadius ? distance : float.MaxValue;
    }

    // The knock prompt goes on the part of the door nearest the player, at about head height
    // (as at the alchemist's door), rather than on top of the tall door.
    public static Vector3 DoorPromptAnchor(Bounds door, Vector3 playerPosition)
    {
        Vector3 nearest = door.ClosestPoint(playerPosition);
        return new Vector3(nearest.x, Mathf.Min(door.max.y, door.min.y + 1.7f), nearest.z);
    }

    // Where the player has to be: by his door until he has come out, then by him.
    private Vector3 Anchor()
    {
        if (!AtDoor) return transform.position;
        Bounds bounds = door.GetComponent<Renderer>().bounds;
        return new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
    }

    private bool Begin(bool knock)
    {
        if (!CanBegin()) return false;
        if (player == null) player = Interactables.FindPlayer();
        if (player == null || player.gameObject.scene != gameObject.scene) return false;
        Vector3 distance = player.position - Anchor();
        if (Mathf.Abs(distance.y) > 3f) return false;
        distance.y = 0f;
        if (distance.sqrMagnitude > talkRadius * talkRadius) return false;
        // Not while the alchemist's potion hunt is on: he waves the player off instead.
        if (ClueMinigame.HuntUnderway)
        {
            BusyNotice.Show(BusyLine);
            return false;
        }
        if (view == null) view = Camera.main;
        if (view == null) return false;

        originalPosition = view.transform.position;
        originalRotation = view.transform.rotation;
        shotBlend = 1f;
        playerController = player.GetComponent<PlayerController>();
        follow = view.GetComponent<CameraFollow>();
        followWasEnabled = follow != null && follow.enabled;
        if (follow != null) follow.enabled = false;
        doorway = null;
        if (knock && AtDoor && CachudDoorway.TryCreate(door.root, transform, player, out doorway))
            doorway.GetCameraPose(0f, out shotPosition, out shotRotation);
        else CalculateShot((player.position - transform.position).normalized);
        SetPresent(true);
        HasComeOut = true;
        inventory = InventorySystem.instance;
        requestGameAfterReturn = false;
        questionRect = default;
        promptAlpha = 0f;
        active = this;
        SetPhase(Phase.Panning);
        return true;
    }

    private void CalculateShot(Vector3 towardsPlayer)
    {
        towardsPlayer = Vector3.ProjectOnPlane(towardsPlayer, Vector3.up).normalized;
        if (towardsPlayer.sqrMagnitude < 0.01f)
            towardsPlayer = Vector3.ProjectOnPlane(view.transform.position - transform.position, Vector3.up).normalized;
        if (towardsPlayer.sqrMagnitude < 0.01f) towardsPlayer = Vector3.back;
        SpriteRenderer sprite = GetComponent<SpriteRenderer>();
        Bounds bounds = sprite.sprite != null ? sprite.bounds :
            new Bounds(transform.position + Vector3.up, new Vector3(1.5f, 2f, 0.2f));
        Vector3 focus = bounds.center;
        // A nearby player would fill the original approach shot. Move to the
        // speaker's side instead, keeping him in front of the player on return visits.
        bool close = Vector3.ProjectOnPlane(player.position - transform.position, Vector3.up).sqrMagnitude < 16f;
        Vector3 desired = transform.position + towardsPlayer * (close ? -2.5f : 6.2f) +
            Vector3.Cross(Vector3.up, towardsPlayer) * (close ? 6f : 1.25f) + Vector3.up * 2.7f;
        Vector3 offset = desired - focus;
        float halfAngle = Mathf.Tan(view.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float width = sprite.sprite != null ? sprite.sprite.bounds.size.x * Mathf.Abs(transform.lossyScale.x) : 1.5f;
        float distance = Mathf.Max(offset.magnitude,
            Mathf.Max(bounds.extents.y, width * 0.5f / Mathf.Max(0.3f, view.aspect)) / halfAngle * 1.8f);
        float bestScore = float.PositiveInfinity;
        shotPosition = desired;
        // Rotate around obstructions instead of pulling the lens into his face. Try both
        // sides of the house and higher angles, while keeping the full sprite above the dialogue.
        for (int elevation = 0; elevation < 3; elevation++)
        for (int step = 0; step < 24; step++)
        {
            float angle = step == 0 ? 0f : ((step + 1) / 2) * 15f * (step % 2 == 0 ? -1f : 1f);
            Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * offset.normalized;
            direction = (direction + Vector3.up * (elevation * 0.35f)).normalized;
            Vector3 candidate = focus + direction * distance;
            if (!ClearShot(focus, candidate)) continue;
            Quaternion rotation = Quaternion.LookRotation(focus - candidate, Vector3.up);
            Vector3 right = Vector3.ProjectOnPlane(rotation * Vector3.right, Vector3.up).normalized;
            bool framed = true;
            for (int corner = 0; corner < 4; corner++)
            {
                Vector3 point = focus + right * (width * 0.5f * ((corner & 1) == 0 ? -1f : 1f)) +
                    Vector3.up * (bounds.extents.y * ((corner & 2) == 0 ? -1f : 1f));
                Vector3 local = Quaternion.Inverse(rotation) * (point - candidate);
                float x = 0.5f + local.x / (2f * local.z * halfAngle * view.aspect);
                float y = 0.5f + local.y / (2f * local.z * halfAngle);
                if (local.z <= view.nearClipPlane + 0.2f || x < 0.12f || x > 0.88f || y < 0.16f || y > 0.82f)
                    framed = false;
            }
            if (!framed) continue;
            float playerDepth = Vector3.Dot(player.position + Vector3.up * 1.1f - candidate, rotation * Vector3.forward);
            float score = Mathf.Abs(angle) + elevation * 40f;
            if (playerDepth < distance + 0.2f) score += 100f;
            if (score >= bestScore) continue;
            bestScore = score;
            shotPosition = candidate;
        }
        shotRotation = Quaternion.LookRotation(focus - shotPosition, Vector3.up);
    }

    private bool ClearShot(Vector3 focus, Vector3 position)
    {
        Vector3 delta = position - focus;
        foreach (RaycastHit hit in Physics.SphereCastAll(focus, 0.25f, delta.normalized, delta.magnitude,
                     Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            if (!hit.collider.transform.IsChildOf(transform) && !hit.collider.transform.IsChildOf(player)) return false;
        foreach (Collider collider in Physics.OverlapSphere(position, 0.3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            if (!collider.transform.IsChildOf(transform) && !collider.transform.IsChildOf(player)) return false;
        return true;
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
        shotBlend = Mathf.Min(1f, shotBlend + dt / 0.6f);
        phaseTime += dt;
        if (CurrentPhase == Phase.Panning && phaseTime >= PanDuration)
        {
            if (doorway != null) SetPhase(Phase.Emerging);
            else StartDialogue();
        }
        else if (CurrentPhase == Phase.Emerging)
        {
            doorway.Tick(phaseTime);
            if (phaseTime >= CachudDoorway.Duration)
            {
                FinishEmerging();
                StartDialogue();
            }
        }
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

    // Move smoothly from the wide entrance to the same clear, framed conversation shot
    // used on repeat visits.
    private void FinishEmerging()
    {
        if (doorway == null) return;
        doorway.Dispose();
        doorway = null;
        dialogueStartPosition = view.transform.position;
        dialogueStartRotation = view.transform.rotation;
        CalculateShot((player.position - transform.position).normalized);
        shotBlend = 0f;
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
        FinishEmerging();
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
            else if (CurrentPhase == Phase.Emerging && doorway != null)
            {
                doorway.GetCameraPose(phaseTime / CachudDoorway.Duration, out Vector3 position, out Quaternion rotation);
                view.transform.SetPositionAndRotation(position, rotation);
            }
            else if (IsDialogue)
                view.transform.SetPositionAndRotation(Vector3.Lerp(dialogueStartPosition, shotPosition, Ease(shotBlend)),
                    Quaternion.Slerp(dialogueStartRotation, shotRotation, Ease(shotBlend)));
            else if (CurrentPhase == Phase.Returning)
            {
                // The follow rig runs first and supplies the live destination each frame.
                Vector3 destination = followWasEnabled && follow != null ? view.transform.position : originalPosition;
                Quaternion rotation = followWasEnabled && follow != null ? view.transform.rotation : originalRotation;
                float t = Ease(phaseTime / ReturnDuration);
                view.transform.SetPositionAndRotation(Vector3.Lerp(returnPosition, destination, t),
                    Quaternion.Slerp(returnRotation, rotation, t));
            }
            if (playerController != null)
            {
                // Waiting at the door, the player watches it with their back to the camera.
                if (doorway != null) playerController.FaceAway();
                playerController.AnimateCinematicWalk(Vector3.zero, 0f);
            }
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
            Vector3 anchor = AtDoor ? DoorPromptAnchor(door.GetComponent<Renderer>().bounds,
                player != null ? player.position : Anchor()) : Interactables.TopCentre(gameObject);
            InteractPrompt.Draw(anchor, 12f, "E", AtDoor ? "Knock" : "Talk", 3, promptAlpha);
            return;
        }
        int previousDepth = GUI.depth;
        GUI.depth = -900;
        CinematicOverlay.DrawBars(BarsAmount);
        if (IsDialogue)
            questionRect = CinematicOverlay.DrawDialogue(wrappedLine, VisibleCharacters,
                CurrentPhase == Phase.Waiting, CurrentPhase == Phase.Questions ? LastLocationQuestion : null);
        GUI.depth = previousDepth;
    }

    public void Cancel()
    {
        if (active != this) return;
        FinishEmerging();
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
