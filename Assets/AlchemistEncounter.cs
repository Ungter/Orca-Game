using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Knocking on the alchemist's door (alchDoor) plays the magician's doorway cinematic: fade
// into the room, typed lines with [ENTER] to continue and [ESC] to leave, fade back out. After
// the introduction the alchemist hands over the Clue Letter, whose map leads the potion hunt
// (ClueMinigame). Until all five potions are found he sends the player back out; once they are,
// he writes the clue word on the letter. It sets itself up at the door on every scene load and
// keeps the letter out of the world until then. The lines are placeholders and the portrait is
// the magician's for now.
[DefaultExecutionOrder(-50)]
public class AlchemistEncounter : MonoBehaviour, IInteractable
{
    public enum Phase { Idle, FadingToRoom, EnteringRoom, Typing, Waiting, LeavingRoom, Returning }

    public const string DoorName = "alchDoor";
    public const string LetterItemName = "Clue Letter";
    public static readonly string[] Introduction =
    {
        "Ah, a visitor! Come in, come in. (Placeholder greeting.)",
        "Mind the bottles, some of them bite. (Placeholder line.)",
        "Someone left something here for you. (Placeholder line.)",
    };
    public const string LetterGift = "Here, take this letter. Hold it up with its number, then press R to start the hunt.";
    public const string LetterNoRoom = "I have a letter for you, but your bag is full! Come back for it.";
    public const string KeepLooking = "My five potions are still out there. Keep looking! (Placeholder line.)";
    public static readonly string[] PotionsFound =
    {
        "You found all five potions! (Placeholder line.)",
        "Hand me that letter for a moment... (Placeholder line.)",
    };
    public const string ClueGift = "There. I wrote a word on it for you. (Placeholder line.)";
    public const string Farewell = "Good luck with that letter. (Placeholder line.)";
    public const float FadeDuration = 0.35f;
    public const float RoomEnterDuration = 0.6f;

    public static bool IsOpen { get; private set; }
    public bool HasHeardIntroduction { get; private set; }
    public bool HasGivenLetter { get; private set; }
    public Phase CurrentPhase { get; private set; }
    public float PhaseTime { get; private set; }
    public string CurrentLine { get; private set; } = Introduction[0];
    public int VisibleCharacters { get; private set; }

    [SerializeField] private float talkRadius = 3f;
    [SerializeField] private float charactersPerSecond = 40f;
    // The world's Clue Letter, handed over instead of picked up.
    [SerializeField] private PickupItem letter;

    private Transform player;
    // Lines played in a row this visit, and what follows the last of them.
    private string[] script;
    private System.Action afterScript;
    private int lineIndex;
    private float promptAlpha;
    private float typed;
    private float roomTime;
    private string wrappedLine;
    private int wrapWidth;
    private Canvas canvas;
    private RectTransform stage;
    private RectTransform room;
    private RectTransform portrait;
    private Image veil;
    private Image backdrop;
    private PixelText speech;
    private PixelText enterHint;
    private PixelText controls;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => IsOpen = false;

    // Every load, including the pause menu's restart. Unsubscribing first keeps play mode
    // without domain reload to one handler.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Bootstrap();

    // Also runs after the first scene loads, in case sceneLoaded fired before Register.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindAnyObjectByType<AlchemistEncounter>() != null) return;
        GameObject door = GameObject.Find(DoorName);
        if (door != null) Install(door, FindLetter());
        else if (FindAnyObjectByType<CameraFollow>() != null)
            Debug.LogWarning("[AlchemistEncounter] No " + DoorName + " in the scene; the Clue Letter stays where it lies.");
    }

    // An entrance at the foot of the door that holds the letter until it is handed over.
    public static AlchemistEncounter Install(GameObject door, PickupItem letter)
    {
        var entrance = new GameObject("AlchemistEntrance");
        SceneManager.MoveGameObjectToScene(entrance, door.scene);
        entrance.transform.position = EntrancePoint(door);
        AlchemistEncounter encounter = entrance.AddComponent<AlchemistEncounter>();
        encounter.letter = letter;
        if (letter != null) letter.gameObject.SetActive(false);
        return encounter;
    }

    // The bottom centre of what the door renders (FBX nodes keep their pivot at the model's
    // origin), or its position when it draws nothing.
    public static Vector3 EntrancePoint(GameObject door)
    {
        Renderer[] renderers = door.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return door.transform.position;
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
    }

    private static PickupItem FindLetter()
    {
        foreach (PickupItem item in FindObjectsByType<PickupItem>(FindObjectsInactive.Include))
        {
            if (item.ItemName == LetterItemName) return item;
        }
        return null;
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
        if (player == null || !isActiveAndEnabled) return float.MaxValue;
        Vector3 delta = player.position - transform.position;
        if (Mathf.Abs(delta.y) > 3f) return float.MaxValue;
        float distance = delta.x * delta.x + delta.z * delta.z;
        return distance <= talkRadius * talkRadius ? distance : float.MaxValue;
    }

    // The introduction plays until it has been heard once; after that a visit goes straight to
    // the letter (when the bag was full last time). With the letter given: back out to the
    // potions until all five are found, then the clue is written on it, then a goodbye.
    public void Begin()
    {
        if (Interactables.Busy || InventoryInspection.IsOpen || PauseMenu.IsPaused || TitleScreen.IsOpen) return;
        if (canvas == null) BuildView();
        IsOpen = true;
        promptAlpha = 0f;
        roomTime = 0f;
        script = null;
        afterScript = null;
        ClueMinigame clue = ClueMinigame.Current;
        if (!HasGivenLetter && !HasHeardIntroduction)
        {
            Play(Introduction, () =>
            {
                HasHeardIntroduction = true;
                OfferLetter();
            });
        }
        else if (!HasGivenLetter) OfferLetter();
        else if (clue != null && clue.IsFound)
        {
            Play(PotionsFound, () =>
            {
                clue.Deliver();
                SetLine(ClueGift);
            });
        }
        else if (clue == null || clue.IsDelivered) SetLine(Farewell);
        else SetLine(KeepLooking);
        SetPhase(Phase.FadingToRoom);
        stage.gameObject.SetActive(false);
        canvas.gameObject.SetActive(true);
        UpdateVisuals();
    }

    // Enter finishes typing first; a separate press advances the conversation.
    public void Tick(float deltaTime, bool advance, bool escape = false)
    {
        if (CurrentPhase == Phase.Idle) return;
        promptAlpha = 0f;
        if (escape)
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
                if (PhaseTime >= RoomEnterDuration) SetPhase(Phase.Typing);
                break;
            case Phase.Typing:
                typed = advance ? wrappedLine.Length :
                    Mathf.Min(wrappedLine.Length, typed + deltaTime * charactersPerSecond);
                VisibleCharacters = Mathf.FloorToInt(typed);
                if (VisibleCharacters >= wrappedLine.Length) SetPhase(Phase.Waiting);
                break;
            case Phase.Waiting:
                if (advance) AdvanceLine();
                break;
            case Phase.LeavingRoom:
                if (PhaseTime >= FadeDuration)
                {
                    stage.gameObject.SetActive(false);
                    SetPhase(Phase.Returning);
                }
                break;
            case Phase.Returning:
                if (PhaseTime >= FadeDuration) Cancel();
                break;
        }
        UpdateVisuals();
    }

    private void Play(string[] lines, System.Action then)
    {
        script = lines;
        afterScript = then;
        lineIndex = 0;
        SetLine(lines[0]);
    }

    // The next line of the visit's script, then whatever follows it; a lone line just leaves.
    private void AdvanceLine()
    {
        if (script == null)
        {
            Exit();
            return;
        }
        if (lineIndex + 1 < script.Length)
        {
            SetLine(script[++lineIndex]);
        }
        else
        {
            System.Action then = afterScript;
            script = null;
            afterScript = null;
            then?.Invoke();
        }
        SetPhase(Phase.Typing);
    }

    private void OfferLetter()
    {
        HasGivenLetter = letter != null ? letter.Collect() :
            InventorySystem.instance != null && InventorySystem.instance.TryAdd(LetterItemName);
        SetLine(HasGivenLetter ? LetterGift : LetterNoRoom);
    }

    public bool DebugCompleteHunt()
    {
        ClueMinigame clue = ClueMinigame.Current;
        if (clue == null || !clue.DebugComplete()) return false;
        HasHeardIntroduction = true;
        InventorySystem inventory = InventorySystem.instance;
        if (!HasGivenLetter && inventory != null)
            HasGivenLetter = inventory.Contains(LetterItemName) ||
                (letter != null ? letter.Collect() : inventory.TryAdd(LetterItemName));
        Cancel();
        return true;
    }

    public void Exit()
    {
        if (CurrentPhase == Phase.Idle || CurrentPhase == Phase.LeavingRoom ||
            CurrentPhase == Phase.Returning) return;
        SetPhase(stage.gameObject.activeSelf ? Phase.LeavingRoom : Phase.Returning);
    }

    // Disabling/unloading the entrance must release the keyboard and hide its canvas.
    public void Cancel()
    {
        if (CurrentPhase == Phase.Idle) return;
        canvas.gameObject.SetActive(false);
        SetPhase(Phase.Idle);
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
        float fade = CurrentPhase == Phase.FadingToRoom || CurrentPhase == Phase.LeavingRoom ?
            Mathf.Clamp01(PhaseTime / FadeDuration) :
            CurrentPhase == Phase.EnteringRoom ? 1f - Mathf.Clamp01(PhaseTime / RoomEnterDuration) :
            CurrentPhase == Phase.Returning ? 1f - Mathf.Clamp01(PhaseTime / FadeDuration) : 0f;
        veil.color = new Color(0f, 0f, 0f, Mathf.SmoothStep(0f, 1f, fade));
        room.localScale = Vector3.one * (CurrentPhase == Phase.EnteringRoom ?
            Mathf.Lerp(1.04f, 1f, Mathf.SmoothStep(0f, 1f, PhaseTime / RoomEnterDuration)) : 1f);
        float bob = CurrentPhase == Phase.Typing ? Mathf.Sin(roomTime * 12f) * 7f : 0f;
        portrait.anchoredPosition = new Vector2(0f, -4f + bob);
        speech.text = wrappedLine.Substring(0, Mathf.Clamp(VisibleCharacters, 0, wrappedLine.Length));
        enterHint.gameObject.SetActive(CurrentPhase == Phase.Waiting);
        enterHint.rectTransform.anchoredPosition = new Vector2(410f, -48f + Mathf.Sin(roomTime * 4f) * 2f);
        controls.text = "[ENTER] CONTINUE    [ESC] LEAVE";
    }

    private void OnGUI()
    {
        if (CurrentPhase == Phase.Idle && Event.current.type == EventType.Repaint)
            InteractPrompt.Draw(transform.position + Vector3.up * 1.7f, 10f, "E", "Knock", 3, promptAlpha);
    }

    // The magician's room layout, with the alchemist's title. Swap the Resources paths when
    // the alchemist's own artwork exists.
    private void BuildView()
    {
        var root = new GameObject("Alchemist room", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
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
        RectTransform conversation = Rect("Conversation", stage, Vector2.zero, new Vector2(1280f, 720f));
        Sprite art = Resources.Load<Sprite>("Magician/Magician");
        portrait = Picture("Alchemist", conversation, art, new Vector2(0f, -4f),
            new Vector2(430f * art.rect.width / art.rect.height, 430f)).rectTransform;
        Image bubble = Picture("Speech bubble", conversation, Resources.Load<Sprite>("Magician/SpeechBubble"),
            new Vector2(0f, -237f), new Vector2(1120f, 168f));
        bubble.type = Image.Type.Sliced;
        bubble.pixelsPerUnitMultiplier = 2f;
        speech = Label("Dialogue", bubble.transform, 21f, new Vector2(0f, -6f), new Vector2(1030f, 96f));
        speech.alignment = TextAnchor.UpperLeft;
        speech.color = new Color32(30, 35, 58, 255);
        enterHint = Label("Enter hint", bubble.transform, 14f, new Vector2(410f, -48f), new Vector2(200f, 28f));
        enterHint.text = "[ENTER]";
        enterHint.color = new Color32(48, 62, 106, 255);
        PixelText title = Label("Alchemist title", conversation, 21f,
            new Vector2(0f, 303f), new Vector2(1000f, 32f));
        title.text = "THE ALCHEMIST";
        title.color = new Color32(242, 211, 107, 255);
        Image top = Picture("Top letterbox", stage, null, new Vector2(0f, 344f), new Vector2(1280f, 32f));
        top.color = Color.black;
        Image bottom = Picture("Bottom letterbox", stage, null, new Vector2(0f, -344f), new Vector2(1280f, 32f));
        bottom.color = Color.black;
        controls = Label("Controls", stage, 12f, new Vector2(0f, -344f), new Vector2(1240f, 28f));
        controls.color = new Color32(233, 214, 168, 255);
        controls.highlightBrackets = true;
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
