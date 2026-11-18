using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// A doorway conversation followed by a windowed hat trick. The magician's room is the backdrop;
// he speaks in the shared dialogue box (DialogueRunner) with a pixel question mark for a face. He
// asks for his lost wand, takes it back, offers the trick (YES/NO), and when it is won, his wand
// writes THIEF and sketches the Orca statue in the air outside (WandSpell).
[DefaultExecutionOrder(-50)]
public class MagicianEncounter : MonoBehaviour, IInteractable
{
    public enum Phase
    {
        Idle, FadingToRoom, EnteringRoom, Typing, Waiting,
        LeavingForTrick, EnteringTrick, Playing, LeavingRoom, Returning, Casting, Aftermath
    }

    // The round door of world/building1: the model's pCylinder1, renamed magDoor in SampleScene
    // (which also gives it a DoorBeacon).
    public const string DoorName = "magDoor";
    private const string ModelDoorName = "pCylinder1";
    public const string WandItemName = "Magic Wand";
    public const string BusyLine = "MAGICIAN: I can see you are busy. Come back later, when you are done!";
    public const float FadeDuration = 0.35f;
    public const float RoomEnterDuration = 0.6f;

    public static bool IsOpen { get; private set; }
    public static bool BlocksMovement => IsOpen;
    public bool HasWand { get; private set; }
    public bool HasRequestedWand { get; private set; }
    // True from the first E at the door. The house's windows light up once the player is back outside.
    public bool HasVisited { get; private set; }
    // The secret has been revealed by the wand at least once.
    public bool HasRevealedSecret { get; private set; }
    // The room, or the fade into or out of it, covers the world.
    public bool InRoom => CurrentPhase != Phase.Idle && CurrentPhase != Phase.Returning &&
        CurrentPhase != Phase.Casting && CurrentPhase != Phase.Aftermath;
    public Phase CurrentPhase { get; private set; }
    public float PhaseTime { get; private set; }
    public string CurrentLine => runner.RawText;
    public int VisibleCharacters => runner.VisibleCharacters;
    public DialogueRunner Runner => runner;
    public bool IsDialogue => CurrentPhase == Phase.Typing || CurrentPhase == Phase.Waiting || CurrentPhase == Phase.Aftermath;

    [SerializeField] private float talkRadius = 3f;
    [SerializeField] private Sprite rewardPaper;

    private static MagicianEncounter active;
    private readonly DialogueRunner runner = new DialogueRunner();
    private Transform player;
    private float promptAlpha;
    private float roomTime;
    private bool showRoom;
    private bool castAfterLeaving;
    // The wand is taken once the room is reached, so leaving during the fade keeps it.
    private bool handInOnEntry;
    private InventorySystem inventory;
    private Canvas canvas;
    private RectTransform stage;
    private RectTransform room;
    private Image veil;
    private Image backdrop;
    private PixelText controls;
    private ShellMinigame game;

    // The door among the house's children, under either name.
    public static Transform FindDoor(Transform house)
    {
        if (house == null) return null;
        Transform door = house.Find(DoorName);
        return door != null ? door : house.Find(ModelDoorName);
    }

    public static bool TryGetGameBounds(out Bounds bounds)
    {
        bounds = default;
        WorldHatStage worldStage = active != null ? active.GetComponent<WorldHatStage>() : null;
        if (worldStage == null || !worldStage.IsShowing) return false;
        bounds = worldStage.FramingBounds;
        return true;
    }

    private void Awake() => player = Interactables.FindPlayer();
    private void OnEnable() => Interactables.Add(this);

    private void OnDisable()
    {
        Interactables.Remove(this);
        Cancel();
    }

    private void Update()
    {
        if (GameplayDebugMenu.BlocksInput) return;
        if (PauseMenu.IsPaused) return;
        if (CurrentPhase != Phase.Idle)
        {
            Tick(Time.unscaledDeltaTime, DialogueInput.Read());
            return;
        }

        Keyboard keyboard = Keyboard.current;
        bool canTalk = !Interactables.Busy && !InventoryInspection.IsOpen &&
            !TitleScreen.IsOpen && Interactables.IsNearest(this);
        promptAlpha = Mathf.MoveTowards(promptAlpha, canTalk ? 1f : 0f, Time.unscaledDeltaTime * 8f);
        if (canTalk && keyboard != null && keyboard.eKey.wasPressedThisFrame) Begin();
    }

    public float InteractDistanceSqr()
    {
        if (player == null || !isActiveAndEnabled) return float.MaxValue;
        Vector3 delta = player.position - transform.position;
        if (Mathf.Abs(delta.y) > 3f) return float.MaxValue;
        float distance = delta.x * delta.x + delta.z * delta.z;
        return distance <= talkRadius * talkRadius ? distance : float.MaxValue;
    }

    // First visit: who he is and the lost wand. Then he waits for it; with it back (now or on an
    // earlier visit) he offers the trick.
    public void Begin()
    {
        if (Interactables.Busy || InventoryInspection.IsOpen || PauseMenu.IsPaused || TitleScreen.IsOpen) return;
        // Not while the alchemist's potion hunt is on: he calls through the door instead.
        if (ClueMinigame.HuntUnderway)
        {
            BusyNotice.Show(BusyLine);
            return;
        }
        if (canvas == null) BuildView();
        inventory = InventorySystem.instance;
        active = this;
        IsOpen = true;
        HasVisited = true;
        promptAlpha = 0f;
        roomTime = 0f;
        showRoom = true;
        castAfterLeaving = false;
        handInOnEntry = false;
        if (HasWand) OfferTrick(StoryScript.TrickOffer);
        else if (!HasRequestedWand)
        {
            Play(StoryScript.MagicianMeeting, () =>
            {
                HasRequestedWand = true;
                if (AcceptWand()) OfferTrick(StoryScript.WandReturned);
                else Exit();
            });
        }
        else if (inventory != null && inventory.Contains(WandItemName))
        {
            handInOnEntry = true;
            OfferTrick(StoryScript.WandReturned);
        }
        else Play(StoryScript.MagicianWaiting, Exit);
        SetPhase(Phase.FadingToRoom);
        stage.gameObject.SetActive(false);
        canvas.gameObject.SetActive(true);
        UpdateVisuals();
    }

    public void Tick(float deltaTime, bool advance, bool escape = false)
    {
        DialogueInput input = advance ? DialogueInput.Continue : DialogueInput.None;
        input.Escape = escape;
        Tick(deltaTime, input);
    }

    // Enter finishes typing first; a separate press advances the conversation.
    public void Tick(float deltaTime, DialogueInput input)
    {
        if (CurrentPhase == Phase.Idle) return;
        promptAlpha = 0f;
        if (input.Escape && CurrentPhase != Phase.LeavingRoom && CurrentPhase != Phase.Returning &&
            CurrentPhase != Phase.Casting)
        {
            if (CurrentPhase == Phase.Aftermath) Finish();
            else Exit();
            UpdateVisuals();
            return;
        }

        PhaseTime += deltaTime;
        roomTime += deltaTime;
        switch (CurrentPhase)
        {
            case Phase.FadingToRoom:
                if (PhaseTime >= FadeDuration)
                {
                    stage.gameObject.SetActive(true);
                    SetPhase(Phase.EnteringRoom);
                }
                break;
            case Phase.EnteringRoom:
                if (PhaseTime >= RoomEnterDuration)
                {
                    if (handInOnEntry)
                    {
                        handInOnEntry = false;
                        if (!AcceptWand()) Play(StoryScript.MagicianWaiting, Exit);
                    }
                    if (runner.IsActive) SyncDialoguePhase();
                    else Exit();
                }
                break;
            case Phase.Typing:
            case Phase.Waiting:
            case Phase.Aftermath:
                runner.Tick(deltaTime, input);
                SyncDialoguePhase();
                break;
            case Phase.LeavingForTrick:
                if (PhaseTime >= FadeDuration)
                {
                    showRoom = false;
                    SetPhase(Phase.EnteringTrick);
                }
                break;
            case Phase.EnteringTrick:
                if (PhaseTime >= FadeDuration)
                {
                    game.Play((RectTransform)canvas.transform, rewardPaper);
                    veil.transform.SetAsLastSibling();
                    SetPhase(Phase.Playing);
                }
                break;
            case Phase.Playing:
                if (game.WinShown) TrickWon();
                else if (game.ShouldClose) Exit();
                break;
            case Phase.LeavingRoom:
                if (PhaseTime >= FadeDuration)
                {
                    stage.gameObject.SetActive(false);
                    game.Hide();
                    SetPhase(Phase.Returning);
                }
                break;
            case Phase.Returning:
                if (PhaseTime >= FadeDuration)
                {
                    if (castAfterLeaving) CastSpell();
                    else Cancel();
                }
                break;
        }
        UpdateVisuals();
    }

    private void Play(DialogueLine[] lines, System.Action then)
    {
        runner.Play(Speaker.Magician, lines, then);
        if (IsDialogue) SyncDialoguePhase();
    }

    private void OfferTrick(DialogueLine[] lines)
    {
        runner.Play(Speaker.Magician, lines, null, StoryScript.Ready, choice =>
        {
            if (choice == 0) SetPhase(Phase.LeavingForTrick);
            else Play(StoryScript.NotReady, Exit);
        });
        if (IsDialogue) SyncDialoguePhase();
    }

    private void SyncDialoguePhase()
    {
        if (!runner.IsActive || CurrentPhase == Phase.Aftermath) return;
        Phase next = runner.IsTyping ? Phase.Typing : Phase.Waiting;
        if (CurrentPhase != next) SetPhase(next);
    }

    // Back from the hats to the room, for his line before the reveal.
    private void TrickWon()
    {
        game.Hide();
        showRoom = true;
        veil.transform.SetAsLastSibling();
        SetPhase(Phase.Typing);
        Play(StoryScript.TrickWon, () =>
        {
            castAfterLeaving = true;
            Exit();
        });
    }

    // Outside again, the wand writes the secret in the air; then the reaction to it.
    private void CastSpell()
    {
        castAfterLeaving = false;
        canvas.gameObject.SetActive(false);
        SetPhase(Phase.Casting);
        if (InventorySystem.instance != null) InventorySystem.instance.RevealClueWords(WandSpell.Word);
        HasRevealedSecret = true;
        WandSpell.Cast(Camera.main, player, () =>
        {
            if (CurrentPhase != Phase.Casting) return;
            SetPhase(Phase.Aftermath);
            runner.Play(Speaker.Magician, StoryScript.SecretRevealed, Finish);
        });
    }

    private bool AcceptWand()
    {
        if (inventory == null || !inventory.TryRemove(WandItemName)) return false;
        HasWand = true;
        return true;
    }

    public bool DebugCompleteTrick()
    {
        if (InventorySystem.instance == null) return false;
        Cancel();
        if (game == null) game = GetComponent<ShellMinigame>();
        if (game == null) game = gameObject.AddComponent<ShellMinigame>();
        HasVisited = HasRequestedWand = true;
        if (!HasWand) InventorySystem.instance.TryRemove(WandItemName);
        HasWand = true;
        return game.DebugComplete();
    }

    public void Exit()
    {
        if (CurrentPhase == Phase.Idle || CurrentPhase == Phase.LeavingRoom ||
            CurrentPhase == Phase.Returning || CurrentPhase == Phase.Casting) return;
        runner.Stop();
        if (CurrentPhase == Phase.Aftermath)
        {
            Finish();
            return;
        }
        if (game.IsWorldGame && game.IsShowing)
        {
            Cancel();
        }
        else SetPhase(stage.gameObject.activeSelf ? Phase.LeavingRoom : Phase.Returning);
    }

    private void Finish()
    {
        runner.Stop();
        SetPhase(Phase.Idle);
        active = null;
        IsOpen = false;
    }

    // Disabling/unloading the entrance must release the keyboard and hide its canvas.
    public void Cancel()
    {
        if (CurrentPhase == Phase.Idle) return;
        runner.Stop();
        if (CurrentPhase == Phase.Casting && WandSpell.Active != null) WandSpell.Active.Finish();
        game.Hide();
        canvas.gameObject.SetActive(false);
        castAfterLeaving = false;
        handInOnEntry = false;
        Finish();
    }

    private void SetPhase(Phase phase)
    {
        CurrentPhase = phase;
        PhaseTime = 0f;
    }

    private void LateUpdate()
    {
        if (CurrentPhase == Phase.Idle || canvas == null) return;
        Rect bounds = ((RectTransform)canvas.transform).rect;
        stage.localScale = Vector3.one * Mathf.Min(bounds.width / 1280f, bounds.height / 720f);
        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        if (canvas == null || CurrentPhase == Phase.Idle || CurrentPhase == Phase.Casting ||
            CurrentPhase == Phase.Aftermath) return;
        backdrop.enabled = stage.gameObject.activeSelf;
        room.gameObject.SetActive(showRoom);
        float fade = CurrentPhase == Phase.FadingToRoom || CurrentPhase == Phase.LeavingRoom ||
            CurrentPhase == Phase.LeavingForTrick ?
            Mathf.Clamp01(PhaseTime / FadeDuration) :
            CurrentPhase == Phase.EnteringRoom ? 1f - Mathf.Clamp01(PhaseTime / RoomEnterDuration) :
            CurrentPhase == Phase.Returning || CurrentPhase == Phase.EnteringTrick ?
            1f - Mathf.Clamp01(PhaseTime / FadeDuration) : 0f;
        veil.color = new Color(0f, 0f, 0f, Mathf.SmoothStep(0f, 1f, fade));
        room.localScale = Vector3.one * (CurrentPhase == Phase.EnteringRoom ?
            Mathf.Lerp(1.04f, 1f, Mathf.SmoothStep(0f, 1f, PhaseTime / RoomEnterDuration)) : 1f);
        controls.text = CurrentPhase == Phase.Playing ? game.Controls : "[ENTER] CONTINUE    [ESC] LEAVE";
    }

    private void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;
        if (CurrentPhase == Phase.Idle)
        {
            InteractPrompt.Draw(transform.position + Vector3.up * 1.7f, 10f, "E", "Talk", 3, promptAlpha);
            return;
        }
        if (!IsDialogue) return;
        int depth = GUI.depth;
        GUI.depth = -900;
        runner.Draw();
        GUI.depth = depth;
    }

    // The room (Resources/Magician/Room) as the backdrop; his words are the shared dialogue box.
    private void BuildView()
    {
        var root = new GameObject("Magician room", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        root.transform.SetParent(transform, false);
        canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 150;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

        backdrop = Picture("Room backdrop", root.transform, null, Vector2.zero, Vector2.zero);
        backdrop.rectTransform.anchorMin = Vector2.zero;
        backdrop.rectTransform.anchorMax = Vector2.one;
        backdrop.rectTransform.offsetMin = backdrop.rectTransform.offsetMax = Vector2.zero;
        backdrop.color = Color.black;
        stage = Rect("Cinematic stage", root.transform, Vector2.zero, new Vector2(1280f, 720f));
        Image black = Picture("Black frame", stage, null, Vector2.zero, new Vector2(1280f, 720f));
        black.color = Color.black;
        room = Picture("Room", stage, Resources.Load<Sprite>("Magician/Room"),
            Vector2.zero, new Vector2(1125f, 720f)).rectTransform;
        Image top = Picture("Top letterbox", stage, null, new Vector2(0f, 344f), new Vector2(1280f, 32f));
        top.color = Color.black;
        Image bottom = Picture("Bottom letterbox", stage, null, new Vector2(0f, -344f), new Vector2(1280f, 32f));
        bottom.color = Color.black;
        controls = Label("Controls", stage, 12f, new Vector2(0f, 344f), new Vector2(1240f, 28f));
        controls.color = new Color32(233, 214, 168, 255);
        controls.highlightBrackets = true;
        game = GetComponent<ShellMinigame>();
        if (game == null) game = gameObject.AddComponent<ShellMinigame>();
        veil = Picture("Transition", root.transform, null, Vector2.zero, Vector2.zero);
        veil.rectTransform.anchorMin = Vector2.zero;
        veil.rectTransform.anchorMax = Vector2.one;
        veil.rectTransform.offsetMin = veil.rectTransform.offsetMax = Vector2.zero;
        veil.color = Color.clear;
        root.SetActive(false);
    }

    private static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    private static Image Picture(string name, Transform parent, Sprite sprite, Vector2 position, Vector2 size)
    {
        Image image = Rect(name, parent, position, size).gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.raycastTarget = false;
        return image;
    }

    private static PixelText Label(string name, Transform parent, float size, Vector2 position, Vector2 bounds)
    {
        PixelText label = Rect(name, parent, position, bounds).gameObject.AddComponent<PixelText>();
        label.fontSize = size;
        label.alignment = TextAnchor.MiddleCenter;
        return label;
    }
}
