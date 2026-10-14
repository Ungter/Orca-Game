using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// A three-round hat-tracking challenge, shown while inspecting Clue Letter 2.
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

    private enum Phase { Reveal, Shuffle, Guess, RoundWon, Won, Lost }

    private class Hat
    {
        public float x;
        public float startX;
        public float targetX;
        public float arc;
        public float tilt;
        public bool hasPanther;
        public bool revealed;
        public RawImage image;
        public RawImage panther;
        public RawImage marker;
    }

    private readonly Hat[] hats = new Hat[4];
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

    private int ActiveHatCount => roundIndex + 2;
    private int ShuffleMoves => 6 + roundIndex * 6;
    private float SwapSeconds => 0.78f - roundIndex * 0.18f;
    private float RevealSeconds => 2.4f - roundIndex * 0.25f;

    public bool Handles(string itemName) => itemName == ItemName;

    public bool IsSolved => phase == Phase.Won;

    public bool ShouldClose { get; private set; }

    public string Controls => phase == Phase.Guess ? "[CLICK] CHOOSE A HAT    [ESC] CLOSE" :
        phase == Phase.RoundWon ? "[CLICK] NEXT ROUND    [ESC] CLOSE" :
        phase == Phase.Lost ? "[CLICK] RETRY ROUND    [ESC] CLOSE" :
        phase == Phase.Won ? "[ESC] CLOSE" :
        "[MOUSE] WATCH THE HATS    [ESC] CLOSE";

    public bool IsShowing => surface != null && surface.gameObject.activeSelf;

    public void Show(RectTransform parent, Sprite letter = null)
    {
        if (surface == null)
        {
            BuildView(parent);
            ResetGame();
        }
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
        }
        surface.gameObject.SetActive(true);
        UpdateVisuals();
    }

    public void Hide()
    {
        if (keywordWriter != null && keywordWriter.IsPlaying) keywordWriter.Complete();
        if (surface != null) surface.gameObject.SetActive(false);
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

        Tick(Time.unscaledDeltaTime);
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
            BeginRound(phase == Phase.RoundWon ? roundIndex + 1 : roundIndex);
        }
    }

    private bool Contains(RectTransform rect, Vector2 surfacePoint)
    {
        Vector3 local = rect.InverseTransformPoint(surface.TransformPoint(surfacePoint));
        return rect.rect.Contains(new Vector2(local.x, local.y));
    }

    private void ChooseHat(int index)
    {
        if (phase != Phase.Guess || index < 0 || index >= ActiveHatCount) return;
        hats[index].revealed = true;
        if (hats[index].hasPanther)
        {
            // Let the final hat lift before the game gives way to the clue letter.
            SetPhase(roundIndex == RoundCount - 1 ? Phase.Won : Phase.RoundWon);
            if (phase == Phase.Won) EnsureKeywordWriter();
        }
        else
        {
            for (int i = 0; i < ActiveHatCount; i++)
                if (hats[i].hasPanther) hats[i].revealed = true;
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

    private void LateUpdate()
    {
        if (!IsShowing) return;
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
        int winner = Random.Range(0, ActiveHatCount);
        for (int i = 0; i < hats.Length; i++)
        {
            hats[i].x = CanvasWidth * 0.5f + (i - (ActiveHatCount - 1) * 0.5f) * 175f;
            hats[i].startX = hats[i].x;
            hats[i].targetX = hats[i].x;
            hats[i].arc = 0f;
            hats[i].tilt = 0f;
            hats[i].hasPanther = i == winner;
            hats[i].revealed = false;
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
        // Move the panther at least every third swap; it cannot sit out a hard round.
        if ((ShuffleMoves - movesLeft) % 3 == 0)
            for (int i = 0; i < ActiveHatCount; i++)
                if (hats[i].hasPanther) swapA = i;
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
        if (surface == null) return;
        roundLabel.text = $"ROUND {roundIndex + 1}/{RoundCount}  -  {ActiveHatCount} HATS";
        message.text = phase == Phase.Reveal ? "Remember the panther's hat" :
            phase == Phase.Shuffle ? "Keep your eyes on it..." :
            phase == Phase.Guess ? "Which hat is hiding the panther?" :
            phase == Phase.RoundWon ? "FOUND IT! Ready for another hat?" :
            phase == Phase.Won ? "ALL THREE ROUNDS CLEARED!" : "That hat was empty!";
        footer.text = phase == Phase.Reveal ? "Follow this hat through every swap." :
            phase == Phase.Shuffle ? $"{ShuffleMoves - movesLeft}/{ShuffleMoves} swaps" :
            phase == Phase.Guess ? "Click once. Make it count." :
            phase == Phase.Lost ? "Watch the panther, then try this round again." :
            phase == Phase.RoundWon ? "More hats. Faster swaps." : "";
        bool result = phase == Phase.RoundWon || phase == Phase.Lost;
        roundAction.gameObject.SetActive(result && phaseElapsed >= ResultSeconds);
        roundActionLabel.text = phase == Phase.RoundWon ? "NEXT ROUND" : "RETRY ROUND";

        for (int i = 0; i < hats.Length; i++)
        {
            Hat hat = hats[i];
            bool active = i < ActiveHatCount;
            hat.image.gameObject.SetActive(active);
            if (!active)
            {
                hat.panther.gameObject.SetActive(false);
                hat.marker.gameObject.SetActive(false);
                continue;
            }
            float x = hat.x - CanvasWidth * 0.5f;
            float y = CanvasHeight * 0.5f - HatY;
            float lift = phase == Phase.Reveal ?
                Mathf.SmoothStep(0f, 1f, phaseElapsed / 0.35f) *
                (1f - Mathf.SmoothStep(0f, 1f, (phaseElapsed - RevealSeconds + 0.45f) / 0.35f)) :
                hat.revealed ? Mathf.SmoothStep(0f, 1f, phaseElapsed / 0.4f) : 0f;
            float entrance = phase == Phase.Reveal ?
                (1f - Mathf.SmoothStep(0f, 1f, phaseElapsed / 0.4f)) * 35f : 0f;
            float hover = phase == Phase.Guess && hoveredHat == i ? 8f : 0f;
            hat.image.rectTransform.anchoredPosition = new Vector2(x, y + hat.arc + lift * 75f + entrance + hover);
            hat.image.rectTransform.localRotation = Quaternion.Euler(0f, 0f, hat.tilt);
            float squash = phase == Phase.Shuffle ? Mathf.Abs(hat.arc) / 700f : 0f;
            hat.image.rectTransform.localScale = new Vector3(1f + squash, 1f - squash, 1f);
            hat.panther.rectTransform.anchoredPosition = new Vector2(x, y - 30f + Mathf.Sin(animationElapsed * 4f) * 3f);
            hat.panther.gameObject.SetActive(hat.hasPanther &&
                (phase == Phase.Reveal && lift > 0.1f || hat.revealed));
            hat.marker.rectTransform.anchoredPosition = new Vector2(x, y - 45f);
            hat.marker.gameObject.SetActive(hat.revealed || hoveredHat == i);
            hat.marker.color = hat.revealed ? hat.hasPanther ? Green : Red : Gold;
            hat.marker.rectTransform.localScale = Vector3.one *
                (1f + Mathf.Sin(animationElapsed * 6f) * 0.025f);
        }
        UpdateVictory();
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
            hats[i] = new Hat();
            hats[i].panther = CreateImage("Panther", gameRoot, panther, new Vector2(85f, 85f));
            // The source is a two-frame vertical sheet; only draw the top panther.
            hats[i].panther.uvRect = new Rect(0f, 0.5f, 1f, 0.5f);
        }
        markerTexture = CreateMarkerTexture();
        for (int i = 0; i < hats.Length; i++)
            hats[i].marker = CreateImage("Reveal ring", gameRoot, markerTexture, new Vector2(148f, 153f));
        for (int i = 0; i < hats.Length; i++)
            hats[i].image = CreateImage("Hat", gameRoot, shell, new Vector2(HatWidth, HatHeight));
        roundAction = CreateButton("Round action", gameRoot, "", new Vector2(0f, -155f), new Vector2(270f, 48f));
        roundActionLabel = roundAction.GetComponentInChildren<PixelText>();
        BuildVictory();
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

    private void BuildVictory()
    {
        victoryPaper = CreatePanel("Clue letter", surface, Vector2.zero,
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
        gameRoot.gameObject.SetActive(!show);
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

    private static Texture2D CreateMarkerTexture()
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
