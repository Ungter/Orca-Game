using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// A doorway conversation followed by a live trick outside the entrance.
[DefaultExecutionOrder(-50)]
public class MagicianEncounter : MonoBehaviour, IInteractable
{
    public enum Phase
    {
        Idle, FadingToRoom, EnteringRoom, Typing, Waiting,
        LeavingForTrick, EnteringTrick, Playing, LeavingRoom, Returning
    }

    public const string WandItemName = "Magic Wand";
    public const string Greeting = "Want to see a magic trick?";
    public const string MissingWand = "Oh no! Im missing my magic wand!, Please find it for me, it should leak magic";
    public const string Thanks = "Oh Thanks for my wand!";
    public const float FadeDuration = 0.35f;
    public const float RoomEnterDuration = 0.6f;

    public static bool IsOpen { get; private set; }
    public static bool BlocksMovement => IsOpen && (active == null || active.CurrentPhase != Phase.Playing);
    public bool HasWand { get; private set; }
    public bool HasRequestedWand { get; private set; }
    public float MaxGameDistance => maxGameDistance;
    public Phase CurrentPhase { get; private set; }
    public float PhaseTime { get; private set; }
    public string CurrentLine { get; private set; } = Greeting;
    public int VisibleCharacters { get; private set; }

    [SerializeField] private float talkRadius = 3f;
    [SerializeField, Min(3f)] private float maxGameDistance = 25f;
    [SerializeField] private float charactersPerSecond = 40f;
    [SerializeField] private Sprite rewardPaper;

    private static MagicianEncounter active;
    private Transform player;
    private bool handInOnEntry;
    private float promptAlpha;
    private float typed;
    private float roomTime;
    private string wrappedLine;
    private int wrapWidth;
    private InventorySystem inventory;
    private Canvas canvas;
    private RectTransform stage;
    private RectTransform room;
    private RectTransform magician;
    private GameObject conversation;
    private Image veil;
    private Image backdrop;
    private PixelText speech;
    private PixelText enterHint;
    private PixelText controls;
    private ShellMinigame game;
    private WorldHatStage worldStage;
    private Transform house;

    private bool WaitingForWand => HasRequestedWand && !HasWand &&
        (InventorySystem.instance == null || !InventorySystem.instance.Contains(WandItemName));

    public static bool TryGetGameBounds(out Bounds bounds)
    {
        bounds = default;
        if (active == null || active.worldStage == null || !active.worldStage.IsShowing) return false;
        bounds = active.worldStage.FramingBounds;
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
        if (PauseMenu.IsPaused) return;
        Keyboard keyboard = Keyboard.current;
        if (CurrentPhase != Phase.Idle)
        {
            bool advance = keyboard != null &&
                (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame);
            bool escape = keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
            Tick(Time.unscaledDeltaTime, advance, escape);
            return;
        }

        bool active = !Interactables.Busy && !InventoryInspection.IsOpen &&
            !TitleScreen.IsOpen && Interactables.IsNearest(this);
        promptAlpha = Mathf.MoveTowards(promptAlpha, active ? 1f : 0f, Time.unscaledDeltaTime * 8f);
        if (active && keyboard != null && keyboard.eKey.wasPressedThisFrame) Begin();
    }

    public float InteractDistanceSqr()
    {
        if (player == null || !isActiveAndEnabled || WaitingForWand) return float.MaxValue;
        Vector3 delta = player.position - transform.position;
        if (Mathf.Abs(delta.y) > 3f) return float.MaxValue;
        float distance = delta.x * delta.x + delta.z * delta.z;
        return distance <= talkRadius * talkRadius ? distance : float.MaxValue;
    }

    public void Begin()
    {
        if (Interactables.Busy || InventoryInspection.IsOpen || PauseMenu.IsPaused || TitleScreen.IsOpen) return;
        if (WaitingForWand) return;
        if (canvas == null) BuildView();
        inventory = InventorySystem.instance;
        active = this;
        IsOpen = true;
        promptAlpha = 0f;
        roomTime = 0f;
        handInOnEntry = HasRequestedWand && !HasWand;
        SetLine(handInOnEntry ? Thanks : Greeting);
        SetPhase(Phase.FadingToRoom);
        stage.gameObject.SetActive(false);
        conversation.SetActive(true);
        canvas.gameObject.SetActive(true);
        UpdateVisuals();
    }

    // Enter finishes typing first; a separate press advances the conversation.
    public void Tick(float deltaTime, bool advance, bool escape = false)
    {
        if (CurrentPhase == Phase.Idle) return;
        promptAlpha = 0f;
        if (escape && CurrentPhase != Phase.LeavingRoom && CurrentPhase != Phase.Returning)
        {
            Exit();
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
                        if (!AcceptWand())
                        {
                            Cancel();
                            break;
                        }
                    }
                    SetPhase(Phase.Typing);
                }
                break;
            case Phase.Typing:
                typed = advance ? wrappedLine.Length :
                    Mathf.Min(wrappedLine.Length, typed + deltaTime * charactersPerSecond);
                VisibleCharacters = Mathf.FloorToInt(typed);
                if (VisibleCharacters >= wrappedLine.Length)
                {
                    if (CurrentLine == MissingWand) HasRequestedWand = true;
                    SetPhase(Phase.Waiting);
                }
                break;
            case Phase.Waiting:
                if (advance) AdvanceLine();
                break;
            case Phase.LeavingForTrick:
                if (PhaseTime >= FadeDuration)
                {
                    stage.gameObject.SetActive(false);
                    game.PlayInWorld(FindHouse(), transform.position, rewardPaper);
                    worldStage = GetComponent<WorldHatStage>();
                    PlacePlayerForTrick();
                    SetPhase(Phase.EnteringTrick);
                }
                break;
            case Phase.EnteringTrick:
                if (PhaseTime >= FadeDuration)
                {
                    game.StartWorldPlayback();
                    SetPhase(Phase.Playing);
                }
                break;
            case Phase.Playing:
                if (game.ShouldClose || (player != null &&
                    (player.position - transform.position).sqrMagnitude > maxGameDistance * maxGameDistance)) Exit();
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
                if (PhaseTime >= FadeDuration) Cancel();
                break;
        }
        UpdateVisuals();
    }

    private void AdvanceLine()
    {
        if (HasWand)
        {
            SetPhase(Phase.LeavingForTrick);
        }
        else if (CurrentLine == Greeting)
        {
            SetLine(MissingWand);
            SetPhase(Phase.Typing);
        }
        else if (AcceptWand())
        {
            SetLine(Thanks);
            SetPhase(Phase.Typing);
        }
        else
        {
            Exit();
        }
    }

    private bool AcceptWand()
    {
        if (inventory == null || !inventory.TryRemove(WandItemName)) return false;
        HasWand = true;
        return true;
    }

    private void PlacePlayerForTrick()
    {
        if (player == null) return;
        if (player.TryGetComponent(out PlayerController controller))
            controller.PlaceOnGround(worldStage.ViewingPoint);
        else
            player.position = worldStage.ViewingPoint + Vector3.up * Mathf.Max(0.02f, player.position.y - transform.position.y);
        Vector3 forward = worldStage.Centre - player.position;
        forward.y = 0f;
        player.rotation = Quaternion.LookRotation(forward);
        Camera view = Camera.main;
        if (view != null && view.TryGetComponent(out CameraFollow follow)) follow.LookTowards(worldStage.Centre);
        Physics.SyncTransforms();
    }

    public void Exit()
    {
        if (CurrentPhase == Phase.Idle || CurrentPhase == Phase.LeavingRoom ||
            CurrentPhase == Phase.Returning) return;
        if (game.IsWorldGame && game.IsShowing)
        {
            Cancel();
        }
        else SetPhase(stage.gameObject.activeSelf ? Phase.LeavingRoom : Phase.Returning);
    }

    private Transform FindHouse()
    {
        if (house != null) return house;
        foreach (GameObject root in gameObject.scene.GetRootGameObjects())
        {
            if (root.name != "IslanddemoV3") continue;
            house = root.transform.Find("world/building1");
            break;
        }
        return house;
    }

    // Disabling/unloading the entrance must release the keyboard and hide its canvas.
    public void Cancel()
    {
        if (CurrentPhase == Phase.Idle) return;
        game.Hide();
        canvas.gameObject.SetActive(false);
        handInOnEntry = false;
        SetPhase(Phase.Idle);
        active = null;
        IsOpen = false;
    }

    private void SetLine(string line)
    {
        CurrentLine = line;
        wrapWidth = 0;
        WrapLine();
        typed = 0f;
        VisibleCharacters = 0;
    }

    private void WrapLine()
    {
        int width = Mathf.Max(1, Mathf.FloorToInt(speech.rectTransform.rect.width / speech.PixelSize()));
        if (width == wrapWidth) return;
        wrapWidth = width;
        wrappedLine = string.Join("\n", PixelFont.Wrap(CurrentLine, width));
        if (CurrentPhase == Phase.Waiting) VisibleCharacters = wrappedLine.Length;
    }

    private void SetPhase(Phase phase)
    {
        CurrentPhase = phase;
        PhaseTime = 0f;
    }

    private void LateUpdate()
    {
        if (CurrentPhase == Phase.Idle) return;
        Rect bounds = ((RectTransform)canvas.transform).rect;
        stage.localScale = Vector3.one * Mathf.Min(bounds.width / 1280f, bounds.height / 720f);
        WrapLine();
        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        if (canvas == null || CurrentPhase == Phase.Idle) return;
        backdrop.enabled = stage.gameObject.activeSelf;
        float fade = CurrentPhase == Phase.FadingToRoom || CurrentPhase == Phase.LeavingRoom ||
            CurrentPhase == Phase.LeavingForTrick ?
            Mathf.Clamp01(PhaseTime / FadeDuration) :
            CurrentPhase == Phase.EnteringRoom ? 1f - Mathf.Clamp01(PhaseTime / RoomEnterDuration) :
            CurrentPhase == Phase.Returning || CurrentPhase == Phase.EnteringTrick ?
            1f - Mathf.Clamp01(PhaseTime / FadeDuration) : 0f;
        veil.color = new Color(0f, 0f, 0f, Mathf.SmoothStep(0f, 1f, fade));
        room.localScale = Vector3.one * (CurrentPhase == Phase.EnteringRoom ?
            Mathf.Lerp(1.04f, 1f, Mathf.SmoothStep(0f, 1f, PhaseTime / RoomEnterDuration)) : 1f);
        float bob = CurrentPhase == Phase.Typing ? Mathf.Sin(roomTime * 12f) * 7f : 0f;
        magician.anchoredPosition = new Vector2(0f, -4f + bob);
        speech.text = wrappedLine.Substring(0, Mathf.Clamp(VisibleCharacters, 0, wrappedLine.Length));
        enterHint.gameObject.SetActive(CurrentPhase == Phase.Waiting);
        enterHint.rectTransform.anchoredPosition = new Vector2(410f, -48f + Mathf.Sin(roomTime * 4f) * 2f);
        controls.text = CurrentPhase == Phase.Playing ? game.Controls : "[ENTER] CONTINUE    [ESC] LEAVE";
    }

    private void OnGUI()
    {
        if (CurrentPhase == Phase.Idle && Event.current.type == EventType.Repaint)
            InteractPrompt.Draw(transform.position + Vector3.up * 1.7f, 10f, "E", "Talk", 3, promptAlpha);
    }

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
        conversation = Rect("Conversation", stage, Vector2.zero, new Vector2(1280f, 720f)).gameObject;
        Sprite portrait = Resources.Load<Sprite>("Magician/Magician");
        magician = Picture("Magician", conversation.transform, portrait, new Vector2(0f, -4f),
            new Vector2(430f * portrait.rect.width / portrait.rect.height, 430f)).rectTransform;
        Image bubble = Picture("Speech bubble", conversation.transform, Resources.Load<Sprite>("Magician/SpeechBubble"),
            new Vector2(0f, -237f), new Vector2(1120f, 168f));
        bubble.type = Image.Type.Sliced;
        bubble.pixelsPerUnitMultiplier = 2f;
        speech = Label("Dialogue", bubble.transform, 21f, new Vector2(0f, -6f), new Vector2(1030f, 96f));
        speech.alignment = TextAnchor.UpperLeft;
        speech.color = new Color32(30, 35, 58, 255);
        enterHint = Label("Enter hint", bubble.transform, 14f, new Vector2(410f, -48f), new Vector2(200f, 28f));
        enterHint.text = "[ENTER]";
        enterHint.color = new Color32(48, 62, 106, 255);
        PixelText title = Label("Magician title", conversation.transform, 21f,
            new Vector2(0f, 303f), new Vector2(1000f, 32f));
        title.text = "THE MAGICIAN";
        title.color = new Color32(242, 211, 107, 255);
        Image top = Picture("Top letterbox", stage, null, new Vector2(0f, 344f), new Vector2(1280f, 32f));
        top.color = Color.black;
        Image bottom = Picture("Bottom letterbox", stage, null, new Vector2(0f, -344f), new Vector2(1280f, 32f));
        bottom.color = Color.black;
        controls = Label("Controls", stage, 12f, new Vector2(0f, -344f), new Vector2(1240f, 28f));
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
