using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// The three-hat game from the supplied p5.js sketch, shown while inspecting Clue Letter 2.
public class ShellMinigame : MonoBehaviour
{
    private const int CanvasWidth = 800;
    private const int CanvasHeight = 500;
    private const float HatY = 225f;
    private const float HatWidth = 130f;
    private const float HatHeight = 145f;
    private const float RevealSeconds = 2.2f;
    private const int ShuffleMoves = 12;
    private const float ShuffleSpeed = 0.16f;
    private const string ItemName = "Clue Letter 2";

    private enum Phase { Reveal, Shuffle, Guess, Won, Lost }

    private class Hat
    {
        public float x;
        public float targetX;
        public bool hasPanther;
        public bool revealed;
        public RawImage image;
        public RawImage panther;
        public RawImage marker;
    }

    private readonly Hat[] hats = new Hat[3];
    private RectTransform surface;
    private PixelText message;
    private PixelText footer;
    private Texture2D markerTexture;
    private Phase phase;
    private float revealElapsed;
    private int movesLeft;

    public bool Handles(string itemName) => itemName == ItemName;

    public string Controls => phase == Phase.Guess ? "[CLICK] CHOOSE A HAT    [ESC] CLOSE" :
        phase == Phase.Won || phase == Phase.Lost ? "[CLICK] PLAY AGAIN    [ESC] CLOSE" :
        "[MOUSE] WATCH THE HATS    [ESC] CLOSE";

    public bool IsShowing => surface != null && surface.gameObject.activeSelf;

    public void Show(RectTransform parent)
    {
        if (surface == null)
        {
            BuildView(parent);
            ResetGame();
        }
        surface.gameObject.SetActive(true);
        UpdateVisuals();
    }

    public void Hide()
    {
        if (surface != null) surface.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (markerTexture != null) Destroy(markerTexture);
    }

    private void Update()
    {
        if (!IsShowing) return;

        if (phase == Phase.Reveal)
        {
            revealElapsed += Time.unscaledDeltaTime;
            if (revealElapsed >= RevealSeconds)
            {
                phase = Phase.Shuffle;
                movesLeft = ShuffleMoves;
                ScheduleSwap();
            }
        }
        else if (phase == Phase.Shuffle)
        {
            // Match p5's lerp(..., 0.16) at 60 fps without tying speed to frame rate.
            float amount = 1f - Mathf.Pow(1f - ShuffleSpeed, Time.unscaledDeltaTime * 60f);
            bool settled = true;
            foreach (Hat hat in hats)
            {
                hat.x = Mathf.Lerp(hat.x, hat.targetX, amount);
                if (Mathf.Abs(hat.x - hat.targetX) < 0.5f) hat.x = hat.targetX;
                else settled = false;
            }
            if (settled)
            {
                if (movesLeft > 0) ScheduleSwap();
                else phase = Phase.Guess;
            }
        }
        else if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame &&
                 RectTransformUtility.ScreenPointToLocalPointInRectangle(
                     surface, Mouse.current.position.ReadValue(), null, out Vector2 point) &&
                 surface.rect.Contains(point))
        {
            if (phase == Phase.Guess)
            {
                foreach (Hat hat in hats)
                {
                    if (Mathf.Abs(point.x - (hat.x - CanvasWidth * 0.5f)) > HatWidth * 0.5f ||
                        Mathf.Abs(point.y - (CanvasHeight * 0.5f - HatY)) > HatHeight * 0.5f)
                        continue;

                    hat.revealed = true;
                    phase = hat.hasPanther ? Phase.Won : Phase.Lost;
                    if (phase == Phase.Lost)
                    {
                        foreach (Hat other in hats)
                            if (other.hasPanther) other.revealed = true;
                    }
                    break;
                }
            }
            else if (phase == Phase.Won || phase == Phase.Lost)
            {
                ResetGame();
            }
        }

        UpdateVisuals();
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
        int winner = Random.Range(0, hats.Length);
        for (int i = 0; i < hats.Length; i++)
        {
            hats[i].x = 180f + i * 220f;
            hats[i].targetX = hats[i].x;
            hats[i].hasPanther = i == winner;
            hats[i].revealed = false;
        }
        revealElapsed = 0f;
        movesLeft = 0;
        phase = Phase.Reveal;
        UpdateVisuals();
    }

    private void ScheduleSwap()
    {
        int a = Random.Range(0, hats.Length);
        int b = Random.Range(0, hats.Length - 1);
        if (b >= a) b++;
        float target = hats[a].targetX;
        hats[a].targetX = hats[b].targetX;
        hats[b].targetX = target;
        movesLeft--;
    }

    private void UpdateVisuals()
    {
        if (surface == null) return;
        message.text = phase == Phase.Reveal ? "Remember where the panther is" :
            phase == Phase.Shuffle ? "Shuffling..." :
            phase == Phase.Guess ? "Which hat is hiding the panther?" :
            phase == Phase.Won ? "YOU WIN! You found the panther!" : "You have failed...";
        footer.text = phase == Phase.Reveal ? "Find the Panther." :
            phase == Phase.Shuffle ? "Watch the hats carefully" :
            phase == Phase.Guess ? "Click a hat to find your cat." : "Click to play again.";

        foreach (Hat hat in hats)
        {
            float x = hat.x - CanvasWidth * 0.5f;
            hat.image.rectTransform.anchoredPosition = new Vector2(x, CanvasHeight * 0.5f - HatY);
            hat.panther.rectTransform.anchoredPosition = new Vector2(x, CanvasHeight * 0.5f - (HatY + 55f));
            hat.panther.gameObject.SetActive(hat.hasPanther &&
                (phase == Phase.Reveal || hat.revealed && (phase == Phase.Won || phase == Phase.Lost)));
            hat.marker.rectTransform.anchoredPosition = new Vector2(x, CanvasHeight * 0.5f - (HatY + 45f));
            hat.marker.gameObject.SetActive(hat.revealed);
            hat.marker.color = hat.hasPanther ? new Color32(30, 160, 65, 255) : new Color32(210, 45, 45, 255);
        }
    }

    private void BuildView(RectTransform parent)
    {
        surface = new GameObject("Shell game", typeof(RectTransform)).GetComponent<RectTransform>();
        surface.SetParent(parent, false);
        surface.anchorMin = surface.anchorMax = new Vector2(0.5f, 0.5f);
        surface.anchoredPosition = Vector2.zero;
        surface.sizeDelta = new Vector2(CanvasWidth, CanvasHeight);

        Texture2D background = Resources.Load<Texture2D>("ShellGame/background");
        Texture2D shell = Resources.Load<Texture2D>("ShellGame/shell");
        Texture2D panther = Resources.Load<Texture2D>("ShellGame/panther");
        if (background == null || shell == null || panther == null)
            Debug.LogError("Shell game artwork is missing from Resources/ShellGame.");
        if (background != null) background.filterMode = FilterMode.Point;
        if (shell != null) shell.filterMode = FilterMode.Point;
        if (panther != null) panther.filterMode = FilterMode.Point;

        CreateImage("Background", surface, background, new Vector2(CanvasWidth, CanvasHeight));
        CreateLabel("Title", "Shell Game", 26f, 42f, new Color32(60, 42, 30, 255));
        message = CreateLabel("Message", "", 16f, 82f, new Color32(60, 42, 30, 255));
        footer = CreateLabel("Instructions", "", 22f, 455f, new Color32(70, 55, 42, 255));

        // The panthers and reveal rings sit behind all three hats while they cross.
        for (int i = 0; i < hats.Length; i++)
        {
            hats[i] = new Hat();
            hats[i].panther = CreateImage("Panther", surface, panther, new Vector2(125f, 250f));
        }
        markerTexture = CreateMarkerTexture();
        for (int i = 0; i < hats.Length; i++)
            hats[i].marker = CreateImage("Reveal ring", surface, markerTexture, new Vector2(148f, 153f));
        for (int i = 0; i < hats.Length; i++)
            hats[i].image = CreateImage("Hat", surface, shell, new Vector2(HatWidth, HatHeight));
    }

    private PixelText CreateLabel(string name, string text, float size, float y, Color color)
    {
        PixelText label = new GameObject(name, typeof(RectTransform), typeof(PixelText)).GetComponent<PixelText>();
        label.transform.SetParent(surface, false);
        label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        label.rectTransform.anchoredPosition = new Vector2(0f, CanvasHeight * 0.5f - y);
        label.rectTransform.sizeDelta = new Vector2(CanvasWidth, 42f);
        label.fontSize = size;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = color;
        label.text = text;
        return label;
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
