using UnityEngine;

// The wooden button shared by the pause menu and the title screen: an
// inventory slot frame stretched to size, gold-studded while selected, its label sinking a
// pixel while held; DrawSelection adds the animated "> <" arrows. Draws in art pixels under the caller's GUI.matrix.
public static class PixelButton
{
    public const int Width = 52;
    public const int Height = 22;

    // Selection arrows sit ArrowGap pixels off the button plus the current step, so they ease
    // out to the far end, pause and come back in.
    private const int ArrowGap = 2;
    private const float ArrowStepTime = 0.12f;
    private static readonly int[] ArrowSteps = { 0, 0, 1, 2, 3, 3, 2, 1 };

    private static readonly Color Parchment = new Color32(233, 214, 168, 255);
    private static readonly Color Gold = new Color32(242, 211, 107, 255);
    private static readonly Color Outline = new Color32(26, 15, 10, 220);

    private static GUIStyle style;
    private static GUIStyle hoverStyle;

    public static void Draw(float x, float y, string label, bool hovered, bool pressed)
    {
        if (style == null || style.normal.background == null || hoverStyle.normal.background == null)
        {
            style = InventoryPixelArt.NineSlice(InventoryPixelArt.SlotTexture, InventoryPixelArt.SlotBorder);
            hoverStyle = InventoryPixelArt.NineSlice(InventoryPixelArt.SlotFilledTexture, InventoryPixelArt.SlotBorder);
        }

        (hovered ? hoverStyle : style).Draw(new Rect(x, y, Width, Height), false, false, false, false);

        RectOffset border = InventoryPixelArt.SlotBorder;
        float wellWidth = Width - border.horizontal;
        float wellHeight = Height - border.vertical;
        float labelX = x + border.left + Mathf.Floor((wellWidth - PixelFont.Measure(label)) * 0.5f);
        float labelY = y + border.top + Mathf.Floor((wellHeight - PixelFont.CapHeight) * 0.5f) + (pressed && hovered ? 1f : 0f);
        PixelFont.Draw(labelX, labelY, label, hovered ? Gold : Parchment, Outline);
    }

    // Gold "> <" arrows either side of the selected button, gliding in and out a pixel at a time.
    public static void DrawSelection(float x, float y)
    {
        int step = Mathf.FloorToInt(Time.unscaledTime / ArrowStepTime) % ArrowSteps.Length;
        float gap = ArrowGap + ArrowSteps[step];
        float arrowY = y + Mathf.Floor((Height - PixelFont.CapHeight) * 0.5f);
        PixelFont.Draw(x - gap - PixelFont.Measure(">"), arrowY, ">", Gold, Outline);
        PixelFont.Draw(x + Width + gap, arrowY, "<", Gold, Outline);
    }
}
