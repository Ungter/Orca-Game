using UnityEngine;
using UnityEngine.InputSystem;

// A white, bottom-left inventory strip that disappears when it is not in use.
[RequireComponent(typeof(InventorySystem), typeof(InventoryInspection))]
public class InventoryHUD : MonoBehaviour
{
    [SerializeField]
    private float slotWidth = 104f;

    [SerializeField]
    private float slotHeight = 52f;

    [SerializeField]
    private float gap = 6f;

    [SerializeField]
    private float margin = 24f;

    [SerializeField]
    private int fontSize = 13;

    private const float IdleDelay = 7f;
    private const float FadeDuration = 0.4f;
    private const float HeaderHeight = 26f;
    private InventorySystem inventory;
    private float lastInteractionTime;
    private float opacity;
    private GUIStyle captionStyle;
    private GUIStyle hintStyle;
    private GUIStyle itemStyle;
    private GUIStyle emptyStyle;

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
            position = new Vector2(position.x, Screen.height - position.y) / UIScale();
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

    private Rect PanelRect()
    {
        float scale = UIScale();
        Rect safeArea = Screen.safeArea;
        float width = inventory.SlotCount() * slotWidth + (inventory.SlotCount() - 1) * gap;
        // IMGUI uses a top-left origin; Screen.safeArea uses a bottom-left origin.
        return new Rect(safeArea.xMin / scale + margin,
            (Screen.height - safeArea.yMin) / scale - margin - slotHeight - HeaderHeight,
            width, slotHeight + HeaderHeight);
    }

    private void OnGUI()
    {
        if (inventory == null || opacity <= 0f || InventoryInspection.IsOpen)
        {
            return;
        }

        BuildStyles();
        Color previousColor = GUI.color;
        Matrix4x4 previousMatrix = GUI.matrix;
        float scale = UIScale();
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        GUI.color = new Color(1f, 1f, 1f, previousColor.a * opacity);

        Rect panel = PanelRect();
        string capacity = inventory.ItemCount() + " / " + inventory.SlotCount();
        if (inventory.IsFull())
        {
            capacity += "  FULL";
        }
        GUI.Label(new Rect(panel.x, panel.y, 96f, 18f), "INVENTORY", captionStyle);
        GUI.Label(new Rect(panel.x + 96f, panel.y, 100f, 18f), capacity, captionStyle);
        GUI.Label(new Rect(panel.xMax - 250f, panel.y, 250f, 18f), "HOLD 1\u20135  INSPECT     [I]  VIEW", hintStyle);

        for (int i = 0; i < inventory.SlotCount(); i++)
        {
            Rect cell = new Rect(panel.x + i * (slotWidth + gap), panel.y + HeaderHeight,
                slotWidth, slotHeight);
            bool occupied = i < inventory.ItemCount();
            DrawRect(cell, new Color(0f, 0f, 0f, occupied ? 0.32f : 0.16f));
            Color border = new Color(1f, 1f, 1f, occupied ? 0.8f : 0.25f);
            DrawRect(new Rect(cell.x, cell.y, cell.width, 1f), border);
            DrawRect(new Rect(cell.x, cell.yMax - 1f, cell.width, 1f), border);
            DrawRect(new Rect(cell.x, cell.y, 1f, cell.height), border);
            DrawRect(new Rect(cell.xMax - 1f, cell.y, 1f, cell.height), border);
            GUI.Label(new Rect(cell.x + 10f, cell.y + 3f, 20f, 12f), (i + 1).ToString(), captionStyle);
            Rect label = new Rect(cell.x + 10f, cell.y + 18f, cell.width - 20f, cell.height - 22f);
            GUI.Label(label, occupied ? inventory.GetItem(i) : "\u2014", occupied ? itemStyle : emptyStyle);
        }

        GUI.color = previousColor;
        GUI.matrix = previousMatrix;
    }

    private static void DrawRect(Rect rect, Color color)
    {
        Color previousColor = GUI.color;
        GUI.color = previousColor * color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previousColor;
    }

    private void BuildStyles()
    {
        if (itemStyle != null)
        {
            return;
        }

        // Start from plain styles rather than the editor-dependent GUI skin.
        itemStyle = new GUIStyle { fontSize = fontSize, alignment = TextAnchor.MiddleLeft, wordWrap = true };
        itemStyle.normal.textColor = Color.white;
        emptyStyle = new GUIStyle(itemStyle);
        emptyStyle.normal.textColor = new Color(1f, 1f, 1f, 0.3f);
        emptyStyle.alignment = TextAnchor.MiddleCenter;
        captionStyle = new GUIStyle(itemStyle) { fontSize = 11, fontStyle = FontStyle.Bold, wordWrap = false };
        captionStyle.normal.textColor = new Color(1f, 1f, 1f, 0.8f);
        hintStyle = new GUIStyle(captionStyle);
        hintStyle.normal.textColor = new Color(1f, 1f, 1f, 0.5f);
        hintStyle.alignment = TextAnchor.MiddleRight;
    }
}
