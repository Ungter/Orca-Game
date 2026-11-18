using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Knocking on the alchemist's door (alchDoor) plays the magician's doorway cinematic: fade
// into the room, where Iguana's spirit talks in the shared dialogue box (DialogueRunner, Enter to
// continue, Esc to leave), then fade back out. After the introduction Iguana hands over the Clue
// Letter, whose map leads the potion hunt (ClueMinigame). Until all five potions are found she
// sends the player back out; once they are, the letter comes up over the room and FELL is written
// on it, under its map. It sets itself up at the door on every scene load and keeps the letter out
// of the world until then.
[DefaultExecutionOrder(-50)]
public class AlchemistEncounter : MonoBehaviour, IInteractable
{
    public enum Phase { Idle, FadingToRoom, EnteringRoom, Typing, Waiting, Writing, LeavingRoom, Returning }

    public const string DoorName = "alchDoor";
    public const string LetterItemName = "Clue Letter";
    public const float FadeDuration = 0.35f;
    public const float RoomEnterDuration = 0.6f;
    // How long the written word stays up before Iguana reads it.
    public const float WrittenHold = 0.9f;

    public static bool IsOpen { get; private set; }
    public bool HasHeardIntroduction { get; private set; }
    public bool HasGivenLetter { get; private set; }
    public Phase CurrentPhase { get; private set; }
    public float PhaseTime { get; private set; }
    public string CurrentLine => runner.RawText;
    public int VisibleCharacters => runner.VisibleCharacters;
    public DialogueRunner Runner => runner;

    [SerializeField] private float talkRadius = 3f;
    // The world's Clue Letter, handed over instead of picked up.
    [SerializeField] private PickupItem letter;

    private readonly DialogueRunner runner = new DialogueRunner();
    private Transform player;
    private float promptAlpha;
    private float roomTime;
    private Canvas canvas;
    private RectTransform stage;
    private RectTransform room;
    private Image veil;
    private Image backdrop;
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
            Tick(Time.unscaledDeltaTime, DialogueInput.Read());
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
        ClueMinigame clue = ClueMinigame.Current;
        if (!HasGivenLetter && !HasHeardIntroduction)
        {
            Play(StoryScript.IguanaMeeting, () =>
            {
                HasHeardIntroduction = true;
                OfferLetter();
            });
        }
        else if (!HasGivenLetter) OfferLetter();
        else if (clue != null && clue.IsFound)
        {
            Play(StoryScript.PotionsFound, () =>
            {
                clue.Deliver();
                if (clue.PresentClue()) SetPhase(Phase.Writing);
                else ReadClue();
            });
        }
        else if (clue == null || clue.IsDelivered) Play(StoryScript.IguanaFarewell, Exit);
        else Play(StoryScript.KeepLooking, Exit);
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
        if (input.Escape && CurrentPhase != Phase.Writing)
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
                    if (runner.IsActive) SyncDialoguePhase();
                    else Exit();
                }
                break;
            case Phase.Typing:
            case Phase.Waiting:
                runner.Tick(deltaTime, input);
                SyncDialoguePhase();
                break;
            case Phase.Writing:
                ClueMinigame clue = ClueMinigame.Current;
                if (clue == null || !clue.IsPresenting) ReadClue();
                else if (clue.ClueWritten && PhaseTime >= WrittenHold && (input.Advance || PhaseTime >= WrittenHold * 2f))
                {
                    clue.EndPresentation();
                    ReadClue();
                }
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

    private void Play(DialogueLine[] lines, System.Action then)
    {
        runner.Play(Speaker.Iguana, lines, then);
        if (CurrentPhase == Phase.Typing || CurrentPhase == Phase.Waiting || CurrentPhase == Phase.Writing)
            SyncDialoguePhase();
    }

    private void SyncDialoguePhase()
    {
        if (runner.IsActive) SetDialoguePhase(runner.IsTyping ? Phase.Typing : Phase.Waiting);
    }

    private void SetDialoguePhase(Phase phase)
    {
        if (CurrentPhase != phase) SetPhase(phase);
    }

    private void ReadClue()
    {
        SetPhase(Phase.Typing);
        Play(StoryScript.ClueRead, Exit);
    }

    private void OfferLetter()
    {
        HasGivenLetter = letter != null ? letter.Collect() :
            InventorySystem.instance != null && InventorySystem.instance.TryAdd(LetterItemName);
        Play(HasGivenLetter ? StoryScript.LetterGiven : StoryScript.LetterNoRoom, Exit);
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
        runner.Stop();
        ClueMinigame.Current?.EndPresentation();
        SetPhase(stage.gameObject.activeSelf ? Phase.LeavingRoom : Phase.Returning);
    }

    // Disabling/unloading the entrance must release the keyboard and hide its canvas.
    public void Cancel()
    {
        if (CurrentPhase == Phase.Idle) return;
        runner.Stop();
        ClueMinigame.Current?.EndPresentation();
        canvas.gameObject.SetActive(false);
        SetPhase(Phase.Idle);
        IsOpen = false;
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
        controls.text = CurrentPhase == Phase.Writing ? "" : "[ENTER] CONTINUE    [ESC] LEAVE";
    }

    private void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;
        if (CurrentPhase == Phase.Idle)
        {
            InteractPrompt.Draw(transform.position + Vector3.up * 1.7f, 10f, "E", "Knock", 3, promptAlpha);
            return;
        }
        if (CurrentPhase != Phase.Typing && CurrentPhase != Phase.Waiting) return;
        int depth = GUI.depth;
        GUI.depth = -900;
        runner.Draw();
        GUI.depth = depth;
    }

    // The magician's room layout as the backdrop; the conversation itself is the shared dialogue box.
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
        Image top = Picture("Top letterbox", stage, null, new Vector2(0f, 344f), new Vector2(1280f, 32f));
        top.color = Color.black;
        Image bottom = Picture("Bottom letterbox", stage, null, new Vector2(0f, -344f), new Vector2(1280f, 32f));
        bottom.color = Color.black;
        controls = Label("Controls", stage, 12f, new Vector2(0f, 344f), new Vector2(1240f, 28f));
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
