using UnityEngine;
using UnityEngine.Experimental.Rendering;

// Draws a minimal inventory bar in the bottom-left corner of the game canvas:
// one white-bordered box per slot, filled with item names in pickup order.
[RequireComponent(typeof(InventorySystem))]
public class InventoryHUD : MonoBehaviour
{
    [SerializeField]
    private float slotWidth = 120f;

    [SerializeField]
    private float slotHeight = 44f;

    [SerializeField]
    private float borderSize = 2f;

    [SerializeField]
    private float gap = 4f;

    [SerializeField]
    private float margin = 10f;

    [SerializeField]
    private int fontSize = 14;

    private InventorySystem inventory;

    private Texture2D whiteTexture;
    private GUIStyle whiteStyle;
    private GUIStyle glassStyle;
    private GUIStyle itemStyle;
    private GUIStyle emptyStyle;

    private void Awake()
    {
        inventory = GetComponent<InventorySystem>();
    }

    private void OnGUI()
    {
        if (inventory == null)
        {
            return;
        }

        BuildStyles();

        float x = margin;
        float y = margin;

        // Small caption above the bar so the panel reads as an inventory.
        GUI.Label(new Rect(x, y + slotHeight + 4f, 140f, 18f), "Inventory", itemStyle);

        for (int i = 0; i < inventory.SlotCount(); i++)
        {
            Rect cell = new Rect(x, y, slotWidth, slotHeight);

            // White box behind a smaller translucent box leaves a white border.
            GUI.Box(cell, "", whiteStyle);
            float inset = borderSize;
            Rect inner = new Rect(x + inset, y + inset, slotWidth - inset * 2f, slotHeight - inset * 2f);
            GUI.Box(inner, "", glassStyle);

            string item = inventory.GetItem(i);
            if (item.Length == 0)
            {
                GUI.Label(inner, "-", emptyStyle);
            }
            else
            {
                GUI.Label(inner, item, itemStyle);
            }

            x += slotWidth + gap;
        }
    }

    private void BuildStyles()
    {
        if (whiteStyle != null)
        {
            return;
        }

        whiteTexture = new Texture2D(1, 1, GraphicsFormat.R8G8B8A8_SRGB, TextureCreationFlags.None) { name = "hud_white" };
        whiteTexture.SetPixel(0, 0, Color.white);
        whiteTexture.Apply();

        whiteStyle = new GUIStyle(GUI.skin.box);
        whiteStyle.normal.background = whiteTexture;
        whiteStyle.border = new RectOffset(0, 0, 0, 0);

        glassStyle = new GUIStyle(GUI.skin.box);
        glassStyle.border = new RectOffset(1, 1, 1, 1);

        itemStyle = new GUIStyle(GUI.skin.label);
        itemStyle.normal.textColor = Color.white;
        itemStyle.alignment = TextAnchor.MiddleLeft;
        itemStyle.fontSize = fontSize;

        emptyStyle = new GUIStyle(GUI.skin.label);
        emptyStyle.normal.textColor = new Color(1f, 1f, 1f, 0.4f);
        emptyStyle.alignment = TextAnchor.MiddleLeft;
        emptyStyle.fontSize = fontSize;
    }
}
