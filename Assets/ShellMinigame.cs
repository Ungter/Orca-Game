using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Three rounds of identity tracking, shared by the outdoor trick and legacy clue inspections.
[DefaultExecutionOrder(20)]
public class ShellMinigame : MonoBehaviour
{
    private const int CanvasWidth = 800;
    private const int CanvasHeight = 500;
    private const float HatY = 265f;
    private const float HatWidth = 130f;
    private const float HatHeight = 145f;
    private const int RoundCount = 3;
    private const float ResultSeconds = 0.8f;
    private const float KeywordHoldSeconds = 3f;
    private const float KeywordFadeSeconds = 0.65f;
    private const string ItemName = "Clue Letter 2";

    [SerializeField] private string victoryKeyword = "Jerk";

    private static readonly Color Ink = new Color32(60, 42, 30, 255);
    private static readonly Color Gold = new Color32(242, 211, 107, 255);
    private static readonly Color Green = new Color32(65, 190, 112, 255);
    private static readonly Color Red = new Color32(210, 65, 55, 255);

    private enum Phase { Reveal, Shuffle, Guess, Found, RoundWon, Won, Lost }

    private static readonly int[] RoundHats = { 2, 3, 5 };
    private static readonly int[] RoundPanthers = { 1, 1, 2 };
    private static readonly float[] RevealTimes = { 2.4f, 2.2f, 3.8f };
    private static readonly string[] PantherNames = { "Azure", "Amber", "Jade", "Rose", "Violet", "Pearl" };

    public struct HatPose
    {
        public float x, y, lift, tilt, scaleX, scaleY;
        public int panther;
        public bool revealed, showPanther, hovered;
    }

    private class Hat
    {
        public float x;
        public float startX;
        public float targetX;
        public float arc;
        public float tilt;
        public int pantherType = -1;
        public bool revealed;
        public float revealedAt;
        public RawImage image;
        public RawImage panther;
        public RawImage marker;
        public PixelText name;
    }

    // The final round uses the most hats.
    public static int MaxHatCount => RoundHats[RoundHats.Length - 1];

    private readonly Hat[] hats = CreateHats();
    private RectTransform surface;
    private RectTransform gameRoot;
    private PixelText roundLabel;
    private PixelText message;
    private PixelText footer;
    private Image roundAction;
    private PixelText roundActionLabel;
    private RectTransform victory;
    private Image victoryPaper;
    private RawImage keywordInk;
    private CanvasGroup victoryFade;
    [System.NonSerialized] private CursiveWriter keywordWriter;
    private Texture2D markerTexture;
    private Phase phase;
    private int roundIndex;
    private float phaseElapsed;
    private float animationElapsed;
    private float swapElapsed;
    private int movesLeft;
    private int swapA;
    private int swapB;
    private int previousA = -1;
    private int previousB = -1;
    private int hoveredHat = -1;
    private float keywordHoldElapsed;
    private int targetPanther;
    private bool started;
    private bool worldMode;
    private bool worldPlayback;
    private bool keywordRecorded;
    private WorldHatStage worldStage;

    private int ActiveHatCount => RoundHats[roundIndex];
    private int ShuffleMoves => 6 + roundIndex * 6;
    private float SwapSeconds => 0.78f - roundIndex * 0.13f;
    private float RevealSeconds => RevealTimes[roundIndex];

    public int HatCount => ActiveHatCount;
    public int PantherCount => RoundPanthers[roundIndex];
    public int RequestedPanther => targetPanther;
    public bool IsWorldGame => worldMode;
    public bool AcceptsGuess => phase == Phase.Guess && (!worldMode || worldPlayback);
    public bool CanContinue => (phase == Phase.RoundWon || phase == Phase.Lost) && phaseElapsed >= ResultSeconds;
    public string RoundTitle => $"ROUND {roundIndex + 1}/{RoundCount}  -  {ActiveHatCount} HATS  -  {PantherCount} " +
        (PantherCount == 1 ? "PANTHER" : "PANTHERS");
    public string Message => phase == Phase.Reveal ? PantherCount == 1 ? "Remember the panther's hat" : "Remember every numbered panther" :
        phase == Phase.Shuffle ? "Keep your eyes on the hats..." :
        phase == Phase.Guess ? $"Find {PantherName(targetPanther)} (#{targetPanther + 1})" :
        phase == Phase.Found ? $"{PantherName(targetPanther)} FOUND!" :
        phase == Phase.RoundWon ? "FOUND THEM! Ready for the next round?" :
        phase == Phase.Won ? "ALL THREE ROUNDS CLEARED!" : "Wrong hat! Watch each panther's identity.";
    public string Instructions => phase == Phase.Reveal ? PantherCount == 1 ? "Follow this hat through every swap." : "Match each name and number to its hat." :
        phase == Phase.Shuffle ? $"{ShuffleMoves - movesLeft}/{ShuffleMoves} swaps" :
        phase == Phase.Guess ? $"{targetPanther}/{PantherCount} found. " +
            (worldMode ? "Walk up to the requested panther's hat." : "Click the requested panther's hat.") :
        phase == Phase.Lost ? "Try this round again." :
        phase == Phase.RoundWon ? "More hats. More panthers. Faster swaps." : "";
    public string ActionLabel => phase == Phase.RoundWon ? "NEXT ROUND" : "RETRY ROUND";

    public static string PantherName(int type) => PantherNames[Mathf.Clamp(type, 0, PantherNames.Length - 1)].ToUpperInvariant();
    public static Texture2D PantherTexture(int type) =>
        Resources.Load<Texture2D>("ShellGame/Panthers/" + PantherNames[Mathf.Clamp(type, 0, PantherNames.Length - 1)]);

    public bool Handles(string itemName) => itemName == ItemName;

    public bool IsSolved => phase == Phase.Won;

    public bool ShouldClose { get; private set; }

    // Outdoors the player walks up to a hat and presses E; the letter inspection uses the mouse.
    public string Controls
    {
        get
        {
            string act = worldMode ? "[E]" : "[CLICK]";
            string close = worldMode ? "[ESC] END" : "[ESC] CLOSE";
            return phase == Phase.Guess ? $"{act} CHOOSE A HAT    {close}" :
                phase == Phase.RoundWon ? $"{act} NEXT ROUND    {close}" :
                phase == Phase.Lost ? $"{act} RETRY ROUND    {close}" : close;
        }
    }

    private static Hat[] CreateHats()
    {
        var created = new Hat[MaxHatCount];
        for (int i = 0; i < created.Length; i++) created[i] = new Hat();
        return created;
    }

    public bool IsShowing => worldMode ? worldStage != null && worldStage.IsShowing :
        surface != null && surface.gameObject.activeSelf;

    // Visits resume an unfinished trick, but a completed trick can be played again.
    public void Play(RectTransform parent, Sprite letter = null)
    {
        ResetSolvedGame();
        Show(parent, letter);
    }

    public void PlayInWorld(Transform house, Vector3 entrance, Sprite letter = null)
    {
        if (surface != null) surface.gameObject.SetActive(false);
        worldMode = true;
        worldPlayback = false;
        if (worldStage == null) worldStage = gameObject.AddComponent<WorldHatStage>();
        worldStage.Open(this, house, entrance);
        BuildVictory(worldStage.Hud);
        ResetSolvedGame();
        if (!started) ResetGame();
        victoryPaper.sprite = letter;
        victoryPaper.color = letter != null ? Color.white : new Color32(233, 214, 168, 255);
        ShouldClose = false;
        keywordHoldElapsed = 0f;
        UpdateVisuals();
    }

    public void StartWorldPlayback() => worldPlayback = true;
    public void PauseWorldPlayback() => worldPlayback = false;
    public void GuessHat(int index) { if (AcceptsGuess) ChooseHat(index); }
    public void HoverHat(int index) => hoveredHat = AcceptsGuess && index >= 0 && index < HatCount && !hats[index].revealed ? index : -1;

    public void ContinueRound()
    {
        if (CanContinue) BeginRound(phase == Phase.RoundWon ? roundIndex + 1 : roundIndex);
    }

    private void ResetSolvedGame()
    {
        if (!IsSolved) return;
        if (keywordWriter != null)
        {
            if (Application.isPlaying) keywordWriter.Dispose();
            else DestroyImmediate(keywordWriter.Texture);
            keywordWriter = null;
            keywordInk.texture = null;
        }
        ResetGame();
    }

    public void Show(RectTransform parent, Sprite letter = null)
    {
        if (worldStage != null) worldStage.Hide();
        worldMode = false;
        if (surface == null)
        {
            BuildView(parent);
        }
        BuildVictory(surface);
        if (!started) ResetGame();
        victoryPaper.sprite = letter;
        victoryPaper.color = letter != null ? Color.white : new Color32(233, 214, 168, 255);
        ShouldClose = false;
        keywordHoldElapsed = 0f;
        if (IsSolved)
        {
            // Solved letters only show their clue, never restart the game.
            EnsureKeywordWriter();
            keywordWriter.Complete();
            phaseElapsed = ResultSeconds;
            RememberKeyword();
        }
        surface.gameObject.SetActive(true);
        UpdateVisuals();
    }

    public void Hide()
    {
        if (keywordWriter != null && keywordWriter.IsPlaying) keywordWriter.Complete();
        RememberKeyword();
        if (surface != null) surface.gameObject.SetActive(false);
        if (worldStage != null) worldStage.Hide();
    }

    private void OnDestroy()
    {
        if (markerTexture != null)
        {
            if (Application.isPlaying) Destroy(markerTexture);
            else DestroyImmediate(markerTexture);
        }
        if (keywordWriter != null)
        {
            if (Application.isPlaying) keywordWriter.Dispose();
            else DestroyImmediate(keywordWriter.Texture);
        }
    }

    private void Update()
    {
        if (!IsShowing) return;
        if (worldMode && !worldPlayback) return;

        Tick(Time.unscaledDeltaTime);
        if (worldMode)
        {
            worldStage.HandleInput();
            UpdateVisuals();
            return;
        }
        hoveredHat = -1;
        if (Mouse.current != null &&
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                surface, Mouse.current.position.ReadValue(), null, out Vector2 point) &&
            surface.rect.Contains(point))
        {
            if (phase == Phase.Guess) hoveredHat = HatAt(point);
            if (Mouse.current.leftButton.wasPressedThisFrame) HandleClick(point);
        }
        UpdateVisuals();
    }

    private void Tick(float deltaTime)
    {
        phaseElapsed += deltaTime;
        animationElapsed += deltaTime;

        if (phase == Phase.Reveal)
        {
            if (phaseElapsed >= RevealSeconds)
            {
                SetPhase(Phase.Shuffle);
                movesLeft = ShuffleMoves;
                ScheduleSwap();
            }
        }
        else if (phase == Phase.Shuffle)
        {
            swapElapsed += deltaTime;
            float progress = Mathf.Clamp01(swapElapsed / SwapSeconds);
            float eased = Mathf.SmoothStep(0f, 1f, progress);
            float arc = Mathf.Sin(progress * Mathf.PI);
            for (int i = 0; i < ActiveHatCount; i++)
            {
                Hat hat = hats[i];
                hat.x = Mathf.Lerp(hat.startX, hat.targetX, eased);
                float direction = i == swapA ? 1f : i == swapB ? -1f : 0f;
                hat.arc = direction * arc * (26f + roundIndex * 7f);
                hat.tilt = direction * arc * 12f;
            }
            if (progress >= 1f)
            {
                if (movesLeft > 0) ScheduleSwap();
                else SetPhase(Phase.Guess);
            }
        }
        else if (phase == Phase.Found && phaseElapsed >= ResultSeconds)
        {
            targetPanther++;
            SetPhase(Phase.Guess);
        }
        else if (phase == Phase.Won && phaseElapsed >= ResultSeconds && !ShouldClose)
        {
            EnsureKeywordWriter();
            if (!keywordWriter.HasStarted) keywordWriter.Play();
            if (keywordWriter.IsPlaying)
            {
                keywordWriter.Tick(deltaTime);
            }
            else
            {
                keywordHoldElapsed += deltaTime;
                ShouldClose = keywordHoldElapsed >= KeywordHoldSeconds + KeywordFadeSeconds;
            }
            RememberKeyword();
        }
    }

    private int HatAt(Vector2 point)
    {
        for (int i = 0; i < ActiveHatCount; i++)
            if (Mathf.Abs(point.x - (hats[i].x - CanvasWidth * 0.5f)) <= HatWidth * 0.5f &&
                Mathf.Abs(point.y - (CanvasHeight * 0.5f - HatY)) <= HatHeight * 0.5f)
                return i;
        return -1;
    }

    private void HandleClick(Vector2 point)
    {
        if (phase == Phase.Guess)
        {
            ChooseHat(HatAt(point));
        }
        else if ((phase == Phase.RoundWon || phase == Phase.Lost) && phaseElapsed >= ResultSeconds &&
                 Contains(roundAction.rectTransform, point))
        {
            ContinueRound();
        }
    }

    private bool Contains(RectTransform rect, Vector2 surfacePoint)
    {
        Vector3 local = rect.InverseTransformPoint(surface.TransformPoint(surfacePoint));
        return rect.rect.Contains(new Vector2(local.x, local.y));
    }

    private void ChooseHat(int index)
    {
        if (phase != Phase.Guess || index < 0 || index >= ActiveHatCount || hats[index].revealed) return;
        hats[index].revealed = true;
        hats[index].revealedAt = animationElapsed;
        if (hats[index].pantherType == targetPanther)
        {
            SetPhase(targetPanther + 1 < PantherCount ? Phase.Found :
                roundIndex == RoundCount - 1 ? Phase.Won : Phase.RoundWon);
            if (phase == Phase.Won) EnsureKeywordWriter();
        }
        else
        {
            for (int i = 0; i < ActiveHatCount; i++)
            {
                if (hats[i].pantherType < 0 || hats[i].revealed) continue;
                hats[i].revealed = true;
                hats[i].revealedAt = animationElapsed;
            }
            SetPhase(Phase.Lost);
        }
    }

    private void SetPhase(Phase next)
    {
        phase = next;
        phaseElapsed = 0f;
        hoveredHat = -1;
    }

    private void EnsureKeywordWriter()
    {
        if (keywordWriter == null)
            keywordWriter = new CursiveWriter(victoryKeyword, 440f, 40f, 4);
        keywordInk.texture = keywordWriter.Texture;
        keywordInk.rectTransform.sizeDelta = keywordWriter.SizeInLetterPixels;
    }

    private void RememberKeyword()
    {
        if (keywordRecorded || !IsSolved || keywordWriter == null || !keywordWriter.HasStarted ||
            keywordWriter.IsPlaying || InventorySystem.instance == null) return;
        InventorySystem.instance.RevealClueWords(victoryKeyword);
        keywordRecorded = true;
    }

    private void LateUpdate()
    {
        if (!IsShowing) return;
        if (worldMode)
        {
            worldStage.Frame();
            return;
        }
        Rect bounds = ((RectTransform)surface.parent).rect;
        float scale = Mathf.Min(bounds.width * 0.84f / CanvasWidth, bounds.height * 0.76f / CanvasHeight);
        surface.localScale = Vector3.one * scale;
    }

    private void ResetGame()
    {
        BeginRound(0);
    }

    private void BeginRound(int index)
    {
        roundIndex = index;
        started = true;
        targetPanther = 0;
        float spacing = Mathf.Min(175f, 660f / (ActiveHatCount - 1));
        for (int i = 0; i < hats.Length; i++)
        {
            hats[i].x = CanvasWidth * 0.5f + (i - (ActiveHatCount - 1) * 0.5f) * spacing;
            hats[i].startX = hats[i].x;
            hats[i].targetX = hats[i].x;
            hats[i].arc = 0f;
            hats[i].tilt = 0f;
            hats[i].pantherType = -1;
            hats[i].revealed = false;
        }
        int[] slots = new int[ActiveHatCount];
        for (int i = 0; i < slots.Length; i++) slots[i] = i;
        for (int i = 0; i < PantherCount; i++)
        {
            int chosen = Random.Range(i, slots.Length);
            int temp = slots[i];
            slots[i] = slots[chosen];
            slots[chosen] = temp;
            hats[slots[i]].pantherType = i;
        }
        previousA = previousB = -1;
        movesLeft = 0;
        keywordHoldElapsed = 0f;
        ShouldClose = false;
        SetPhase(Phase.Reveal);
        UpdateVisuals();
    }

    private void ScheduleSwap()
    {
        for (int i = 0; i < ActiveHatCount; i++)
        {
            hats[i].startX = hats[i].x;
            hats[i].arc = hats[i].tilt = 0f;
        }
        swapA = Random.Range(0, ActiveHatCount);
        // Cycle through the identities so no panther can sit out the hard rounds.
        if ((ShuffleMoves - movesLeft) % 3 == 0)
            for (int i = 0; i < ActiveHatCount; i++)
                if (hats[i].pantherType == (ShuffleMoves - movesLeft) / 3 % PantherCount) swapA = i;
        swapB = Random.Range(0, ActiveHatCount - 1);
        if (swapB >= swapA) swapB++;
        if (ActiveHatCount > 2 &&
            (swapA == previousA && swapB == previousB || swapA == previousB && swapB == previousA))
        {
            // Avoid an immediate undo of the same pair once there are alternatives.
            do { swapB = (swapB + 1) % ActiveHatCount; }
            while (swapB == swapA || swapB == previousA || swapB == previousB);
        }
        float target = hats[swapA].targetX;
        hats[swapA].targetX = hats[swapB].targetX;
        hats[swapB].targetX = target;
        previousA = swapA;
        previousB = swapB;
        swapElapsed = -0.08f;
        movesLeft--;
    }

    private void UpdateVisuals()
    {
        if (worldMode)
        {
            worldStage.Refresh();
            UpdateVictory();
            return;
        }
        if (surface == null) return;
        roundLabel.text = RoundTitle;
        message.text = Message;
        footer.text = Instructions;
        roundAction.gameObject.SetActive(CanContinue);
        roundActionLabel.text = ActionLabel;

        for (int i = 0; i < hats.Length; i++)
        {
            Hat hat = hats[i];
            bool active = i < ActiveHatCount;
            hat.image.gameObject.SetActive(active);
            if (!active)
            {
                hat.panther.gameObject.SetActive(false);
                hat.marker.gameObject.SetActive(false);
                hat.name.gameObject.SetActive(false);
                continue;
            }
            HatPose pose = GetHatPose(i);
            float entrance = phase == Phase.Reveal ?
                (1f - Mathf.SmoothStep(0f, 1f, phaseElapsed / 0.4f)) * 35f : 0f;
            hat.image.rectTransform.anchoredPosition = new Vector2(pose.x, pose.y + pose.lift * 100f + entrance + (pose.hovered ? 8f : 0f));
            hat.image.rectTransform.localRotation = Quaternion.Euler(0f, 0f, pose.tilt);
            hat.image.rectTransform.localScale = new Vector3(pose.scaleX, pose.scaleY, 1f);
            if (hat.pantherType >= 0) hat.panther.texture = PantherTexture(hat.pantherType);
            hat.panther.rectTransform.anchoredPosition = new Vector2(pose.x, -45f + Mathf.Sin(animationElapsed * 4f) * 3f);
            hat.panther.gameObject.SetActive(pose.showPanther);
            hat.name.text = hat.pantherType >= 0 ? $"#{hat.pantherType + 1} {PantherName(hat.pantherType)}" : "";
            hat.name.rectTransform.anchoredPosition = new Vector2(pose.x, -106f);
            hat.name.gameObject.SetActive(pose.showPanther);
            hat.marker.rectTransform.anchoredPosition = new Vector2(pose.x, -60f);
            hat.marker.gameObject.SetActive(pose.revealed || pose.hovered);
            hat.marker.color = pose.revealed ? hat.pantherType >= 0 ? Green : Red : Gold;
            hat.marker.rectTransform.localScale = Vector3.one *
                (1f + Mathf.Sin(animationElapsed * 6f) * 0.025f);
        }
        UpdateVictory();
    }

    public HatPose GetHatPose(int index)
    {
        Hat hat = hats[index];
        float lift = phase == Phase.Reveal ?
            Mathf.SmoothStep(0f, 1f, phaseElapsed / 0.35f) *
            (1f - Mathf.SmoothStep(0f, 1f, (phaseElapsed - RevealSeconds + 0.45f) / 0.35f)) :
            hat.revealed ? Mathf.SmoothStep(0f, 1f, (animationElapsed - hat.revealedAt) / 0.4f) : 0f;
        float squash = phase == Phase.Shuffle ? Mathf.Abs(hat.arc) / 700f : 0f;
        return new HatPose
        {
            x = hat.x - CanvasWidth * 0.5f, y = CanvasHeight * 0.5f - HatY + hat.arc,
            lift = lift, tilt = hat.tilt, scaleX = 1f + squash, scaleY = 1f - squash,
            panther = hat.pantherType, revealed = hat.revealed,
            showPanther = hat.pantherType >= 0 && (phase == Phase.Reveal && lift > 0.1f || hat.revealed),
            hovered = AcceptsGuess && hoveredHat == index && !hat.revealed,
        };
    }

    private void BuildView(RectTransform parent)
    {
        surface = new GameObject("Shell game", typeof(RectTransform)).GetComponent<RectTransform>();
        surface.SetParent(parent, false);
        surface.anchorMin = surface.anchorMax = new Vector2(0.5f, 0.5f);
        surface.anchoredPosition = Vector2.zero;
        surface.sizeDelta = new Vector2(CanvasWidth, CanvasHeight);

        gameRoot = new GameObject("Hat board", typeof(RectTransform)).GetComponent<RectTransform>();
        gameRoot.SetParent(surface, false);
        gameRoot.anchorMin = Vector2.zero;
        gameRoot.anchorMax = Vector2.one;
        gameRoot.offsetMin = gameRoot.offsetMax = Vector2.zero;

        Texture2D background = Resources.Load<Texture2D>("ShellGame/background");
        Texture2D shell = Resources.Load<Texture2D>("ShellGame/shell");
        Texture2D panther = Resources.Load<Texture2D>("ShellGame/panther");
        if (background == null || shell == null || panther == null)
            Debug.LogError("Shell game artwork is missing from Resources/ShellGame.");
        if (background != null) background.filterMode = FilterMode.Point;
        if (shell != null) shell.filterMode = FilterMode.Point;
        if (panther != null) panther.filterMode = FilterMode.Point;

        CreateImage("Background", gameRoot, background, new Vector2(CanvasWidth, CanvasHeight));
        CreateLabel("Title", "THE PANTHER'S HATS", 26f, 35f, Ink);
        roundLabel = CreateLabel("Round", "", 12f, 72f, Ink);
        message = CreateLabel("Message", "", 16f, 108f, Ink);
        footer = CreateLabel("Instructions", "", 14f, 460f, Ink);

        // Panthers and reveal rings stay behind the hats while they cross.
        for (int i = 0; i < hats.Length; i++)
        {
            hats[i].panther = CreateImage("Panther", gameRoot, panther, new Vector2(85f, 85f));
            // The source is a two-frame vertical sheet; only draw the top panther.
            hats[i].panther.uvRect = new Rect(0f, 0.5f, 1f, 0.5f);
        }
        markerTexture = CreateMarkerTexture();
        for (int i = 0; i < hats.Length; i++)
            hats[i].marker = CreateImage("Reveal ring", gameRoot, markerTexture, new Vector2(148f, 153f));
        for (int i = 0; i < hats.Length; i++)
            hats[i].image = CreateImage("Hat", gameRoot, shell, new Vector2(HatWidth, HatHeight));
        for (int i = 0; i < hats.Length; i++)
            hats[i].name = CreateLabel("Panther name", gameRoot, "", 9f, Vector2.zero, new Vector2(130f, 24f), Ink);
        roundAction = CreateButton("Round action", gameRoot, "", new Vector2(0f, -155f), new Vector2(270f, 48f));
        roundActionLabel = roundAction.GetComponentInChildren<PixelText>();
    }

    private PixelText CreateLabel(string name, string text, float size, float y, Color color)
    {
        return CreateLabel(name, gameRoot, text, size, new Vector2(0f, CanvasHeight * 0.5f - y),
            new Vector2(CanvasWidth, 42f), color);
    }

    private static PixelText CreateLabel(string name, Transform parent, string text, float size,
        Vector2 position, Vector2 bounds, Color color)
    {
        PixelText label = new GameObject(name, typeof(RectTransform), typeof(PixelText)).GetComponent<PixelText>();
        label.transform.SetParent(parent, false);
        label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        label.rectTransform.anchoredPosition = position;
        label.rectTransform.sizeDelta = bounds;
        label.fontSize = size;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = color;
        label.text = text;
        return label;
    }

    private static Image CreatePanel(string name, Transform parent, Vector2 position, Vector2 size, Color color)
    {
        Image panel = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        panel.transform.SetParent(parent, false);
        panel.rectTransform.anchorMin = panel.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        panel.rectTransform.anchoredPosition = position;
        panel.rectTransform.sizeDelta = size;
        panel.color = color;
        panel.raycastTarget = false;
        return panel;
    }

    private static Image CreateButton(string name, Transform parent, string text, Vector2 position, Vector2 size)
    {
        Image button = CreatePanel(name, parent, position, size, Gold);
        CreateLabel("Label", button.transform, text, 14f, Vector2.zero, size, Ink);
        return button;
    }

    private void BuildVictory(RectTransform parent)
    {
        if (victory != null)
        {
            victory.SetParent(parent, false);
            victory.anchoredPosition = Vector2.zero;
            victory.localScale = Vector3.one;
            return;
        }
        victoryPaper = CreatePanel("Clue letter", parent, Vector2.zero,
            new Vector2(650f, 406.25f), new Color32(233, 214, 168, 255));
        victoryPaper.preserveAspect = true;
        victory = victoryPaper.rectTransform;
        victoryFade = victory.gameObject.AddComponent<CanvasGroup>();
        keywordInk = CreateImage("Keyword", victory, null, Vector2.zero);
        victory.gameObject.SetActive(false);
    }

    private void UpdateVictory()
    {
        bool show = phase == Phase.Won && phaseElapsed >= ResultSeconds;
        if (worldMode) worldStage.SetRewardVisible(show);
        else gameRoot.gameObject.SetActive(!show);
        victory.gameObject.SetActive(show);
        if (!show) return;
        victoryFade.alpha = 1f - Mathf.SmoothStep(0f, 1f,
            (keywordHoldElapsed - KeywordHoldSeconds) / KeywordFadeSeconds);
    }

    private static RawImage CreateImage(string name, Transform parent, Texture texture, Vector2 size)
    {
        RawImage image = new GameObject(name, typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        image.transform.SetParent(parent, false);
        image.rectTransform.anchorMin = image.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        image.rectTransform.sizeDelta = size;
        image.texture = texture;
        image.raycastTarget = false;
        return image;
    }

    internal static Texture2D CreateMarkerTexture()
    {
        const int width = 148;
        const int height = 153;
        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float dx = (x + 0.5f - width * 0.5f) / (width * 0.5f);
                float dy = (y + 0.5f - height * 0.5f) / (height * 0.5f);
                float distance = dx * dx + dy * dy;
                if (distance <= 1f && distance >= 0.88f)
                    pixels[y * width + x] = new Color32(255, 255, 255, 255);
            }
        }
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        texture.SetPixels32(pixels);
        texture.Apply();
        return texture;
    }
}
