using UnityEngine;
using UnityEngine.InputSystem;

// A bottom-left, pixel-art inventory strip (wooden slots under a parchment
// banner) that disappears when it is not in use. Art lives in InventoryPixelArt; text uses PixelFont.
[RequireComponent(typeof(InventorySystem), typeof(InventoryInspection))]
public class InventoryHUD : MonoBehaviour
{
    // Gap to the screen edge, in reference pixels at 1280x720.
    [SerializeField]
    private float margin = 24f;

    // Screen pixels per art pixel at 1280x720; rounded to a whole number at any resolution.
    [SerializeField]
    private int pixelSize = 3;

    private const float IdleDelay = 7f;
    private const float FadeDuration = 0.4f;

    // Layout in art pixels.
    private const int SlotWidth = 32;
    private const int SlotHeight = 26;
    private const int SlotGap = 2;
    private const int BannerHeight = 14;
    private const int RowGap = 3;
    private const int NameGap = 2;
    private const int LineHeight = PixelFont.CapHeight + 1;
    private const int PanelHeight = BannerHeight + RowGap + SlotHeight + NameGap + LineHeight * 2;
    private const string Hint = "[1-5] INSPECT [I] VIEW";

    private static readonly Color Ink = new Color32(59, 36, 22, 255);
    private static readonly Color InkShadow = new Color32(200, 168, 114, 140);
    private static readonly Color Parchment = new Color32(233, 214, 168, 255);
    private static readonly Color ParchmentDim = new Color32(200, 168, 114, 255);
    private static readonly Color Gold = new Color32(242, 211, 107, 255);
    private static readonly Color Blood = new Color32(196, 52, 40, 255);
    private static readonly Color Outline = new Color32(26, 15, 10, 220);

    private InventorySystem inventory;
    private float lastInteractionTime;
    private float opacity;
    private GUIStyle slotStyle;
    private GUIStyle slotFilledStyle;
    private GUIStyle bannerStyle;

    private void Awake()
    {
        inventory = GetComponent<InventorySystem>();
    }

    private void OnEnable()
    {
        inventory.Interacted += Show;
        Show();
    }

    private void OnDisable()
    {
        inventory.Interacted -= Show;
    }

    private void Show()
    {
        lastInteractionTime = Time.unscaledTime;
        opacity = 1f;
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.iKey.isPressed)
        {
            Show();
        }

        Mouse mouse = Mouse.current;
        if (mouse != null)
        {
            Vector2 position = mouse.position.ReadValue();
            position = new Vector2(position.x, Screen.height - position.y);
            bool pointerActivity = mouse.delta.ReadValue().sqrMagnitude > 0f
                || mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame
                || mouse.scroll.ReadValue().sqrMagnitude > 0f;
            // A parked cursor should not prevent the inventory from fading.
            if (pointerActivity && PanelRect().Contains(position))
            {
                Show();
            }
        }

        float fadeProgress = (Time.unscaledTime - lastInteractionTime - IdleDelay) / FadeDuration;
        opacity = 1f - Mathf.SmoothStep(0f, 1f, fadeProgress);
    }

    private static float UIScale()
    {
        return Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
    }

private int PixelScale()
    {
        return Mathf.Max(1, Mathf.RoundToInt(pixelSize * UIScale()));
    }

    private int PanelWidth()
    {
        int slots = inventory.SlotCount();
        return slots * SlotWidth + Mathf.Max(0, slots - 1) * SlotGap;
    }

    // Panel bounds in screen pixels (IMGUI top-left origin), snapped to whole pixels.
    private Rect PanelRect()
    {
        float scale = UIScale();
        int p = PixelScale();
        Rect safeArea = Screen.safeArea;
        // IMGUI uses a top-left origin; Screen.safeArea uses a bottom-left origin.
        float x = Mathf.Round(safeArea.xMin + margin * scale);
        float bottom = Mathf.Round(Screen.height - safeArea.yMin - margin * scale);
        return new Rect(x, bottom - PanelHeight * p, PanelWidth() * p, PanelHeight * p);
    }

private void OnGUI()
    {
        if (inventory == null || opacity <= 0f || InventoryInspection.IsOpen || Event.current.type != EventType.Repaint)
        {
            return;
        }

        BuildStyles();
        Color previousColor = GUI.color;
        Matrix4x4 previousMatrix = GUI.matrix;
        Rect panel = PanelRect();
        int p = PixelScale();
        // Draw in art pixels: one GUI unit is one art pixel, an exact p x p block on screen.
        GUI.matrix = Matrix4x4.TRS(new Vector3(panel.x, panel.y, 0f), Quaternion.identity, new Vector3(p, p, 1f));
        GUI.color = new Color(1f, 1f, 1f, previousColor.a * opacity);

        DrawHeader();

        int top = BannerHeight + RowGap;
        for (int i = 0; i < inventory.SlotCount(); i++)
        {
            int x = i * (SlotWidth + SlotGap);
            bool occupied = i < inventory.ItemCount();
            (occupied ? slotFilledStyle : slotStyle).Draw(new Rect(x, top, SlotWidth, SlotHeight), false, false, false, false);

            // The well inside the frame, one pixel in from its shadowed edge.
            var well = new Rect(x + InventoryPixelArt.SlotBorder.left + 1, top + InventoryPixelArt.SlotBorder.top + 1,
                SlotWidth - InventoryPixelArt.SlotBorder.horizontal - 2, SlotHeight - InventoryPixelArt.SlotBorder.vertical - 2);
            if (occupied)
            {
                DrawIcon(well, inventory.GetSprite(i));
                DrawName(x, top + SlotHeight + NameGap, inventory.GetItem(i));
            }

            Color number = occupied ? Gold : new Color(ParchmentDim.r, ParchmentDim.g, ParchmentDim.b, 0.55f);
            PixelFont.Draw(well.x, well.y, (i + 1).ToString(), number, Outline);
        }

        GUI.color = previousColor;
        GUI.matrix = previousMatrix;
    }

    private void DrawHeader()
    {
        string title = "INVENTORY " + inventory.ItemCount() + "/" + inventory.SlotCount();
        int titleWidth = PixelFont.Measure(title);
        int bannerWidth = InventoryPixelArt.BannerBorder.horizontal + titleWidth + 6;
        bannerStyle.Draw(new Rect(0f, 0f, bannerWidth, BannerHeight), false, false, false, false);
        PixelFont.Draw(InventoryPixelArt.BannerBorder.left + 3, 4f, title, Ink, InkShadow);

        if (inventory.IsFull())
        {
            PixelFont.Draw(bannerWidth + 3, 5f, "FULL", Blood, Outline);
        }

        int hintWidth = PixelFont.Measure(Hint);
        PixelFont.Draw(PanelWidth() - hintWidth, 5f, Hint, ParchmentDim, Outline);
    }

    private static void DrawIcon(Rect well, Sprite sprite)
    {
        if (sprite == null || sprite.texture == null)
        {
            return;
        }

        Rect source = sprite.textureRect;
        float fit = Mathf.Min(well.width / source.width, well.height / source.height);
        float width = source.width * fit;
        float height = source.height * fit;
        var target = new Rect(well.x + (well.width - width) * 0.5f, well.y + (well.height - height) * 0.5f, width, height);
        var uv = new Rect(source.x / sprite.texture.width, source.y / sprite.texture.height,
            source.width / sprite.texture.width, source.height / sprite.texture.height);
        GUI.DrawTextureWithTexCoords(target, sprite.texture, uv);
    }

    // Item name under its slot: word-wrapped to the slot width, at most two centred lines.
    private static void DrawName(int slotX, int y, string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        string[] words = name.ToUpperInvariant().Split(' ');
        string line = "";
        int lines = 0;
        for (int w = 0; w <= words.Length && lines < 2; w++)
        {
            string candidate = w < words.Length ? (line.Length == 0 ? words[w] : line + " " + words[w]) : null;
            bool flush = candidate == null || (line.Length > 0 && PixelFont.Measure(candidate) > SlotWidth);
            if (flush)
            {
                if (lines == 1 && w < words.Length)
                {
                    line += "..";
                }
                int width = PixelFont.Measure(line);
                PixelFont.Draw(slotX + Mathf.Floor((SlotWidth - width) * 0.5f), y + lines * LineHeight, line, Parchment, Outline);
                lines++;
                line = w < words.Length ? words[w] : "";
            }
            else
            {
                line = candidate;
            }
        }
    }



private void BuildStyles()
    {
        if (slotStyle != null && slotStyle.normal.background != null)
        {
            return;
        }

        slotStyle = NineSlice(InventoryPixelArt.SlotTexture, InventoryPixelArt.SlotBorder);
        slotFilledStyle = NineSlice(InventoryPixelArt.SlotFilledTexture, InventoryPixelArt.SlotBorder);
        bannerStyle = NineSlice(InventoryPixelArt.BannerTexture, InventoryPixelArt.BannerBorder);
    }

    private static GUIStyle NineSlice(Texture2D texture, RectOffset border)
    {
        var style = new GUIStyle { border = new RectOffset(border.left, border.right, border.top, border.bottom) };
        style.normal.background = texture;
        return style;
    }
}
