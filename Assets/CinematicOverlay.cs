using UnityEngine;

// Shared by the opening boat ride and the nearby character's call-out.
public static class CinematicOverlay
{
    private static readonly Color Ink = new Color32(59, 36, 22, 255);
    private const int Padding = 8;
    private static GUIStyle messageStyle;

    public static void DrawBars(float amount)
    {
        Matrix4x4 previousMatrix = GUI.matrix;
        Color previousColor = GUI.color;
        GUI.matrix = Matrix4x4.identity;
        GUI.color = Color.black;
        float height = Mathf.Round(Screen.height * 0.08f * Mathf.Clamp01(amount));
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, height), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(0f, Screen.height - height, Screen.width, height), Texture2D.whiteTexture);
        GUI.color = previousColor;
        GUI.matrix = previousMatrix;
    }

    public static void DrawMessage(string message, float opacity, int pixelSize = 4)
    {
        if (opacity <= 0f || string.IsNullOrEmpty(message)) return;
        int p = Mathf.Max(1, Mathf.RoundToInt(pixelSize * Mathf.Min(Screen.width / 1280f, Screen.height / 720f)));
        int width = Mathf.FloorToInt(Screen.width / (float)p);
        string text = string.Join("\n", PixelFont.Wrap(message, Mathf.Max(1, width - Padding * 2 - 8)));
        float boxWidth = PixelFont.Measure(text) + Padding * 2;
        float boxHeight = PixelFont.MeasureHeight(text) + Padding * 2;
        float x = Mathf.Round((width - boxWidth) * 0.5f);
        float y = Mathf.Floor(Screen.height * 0.92f / p) - boxHeight - 6;
        Matrix4x4 previousMatrix = GUI.matrix;
        Color previousColor = GUI.color;
        GUI.matrix = Matrix4x4.Scale(new Vector3(p, p, 1f));
        DrawPanel(new Rect(x, y, boxWidth, boxHeight), opacity);
        PixelFont.Draw(x + Padding, y + Padding, text, Ink);
        GUI.color = previousColor;
        GUI.matrix = previousMatrix;
    }

    // Returns the question's screen-space hit area. The question list has no implicit
    // "continue" click: only the listed option, Enter or its number selects it.
    public static Rect DrawDialogue(string message, int visibleCharacters, bool waiting, string question)
    {
        int p = Mathf.Max(1, Mathf.RoundToInt(3f * Mathf.Min(Screen.width / 1280f, Screen.height / 720f)));
        string option = question == null ? "" : "> [1] " + question;
        string hint = question != null ? "[ENTER] ASK   [ESC] LEAVE" :
            waiting ? "[ENTER] CONTINUE   [ESC] LEAVE" : "[ESC] LEAVE";
        float bodyHeight = PixelFont.MeasureHeight(message);
        float optionHeight = question == null ? 0f : PixelFont.MeasureHeight(option) + 10f;
        float width = Mathf.Max(PixelFont.Measure(message),
            Mathf.Max(PixelFont.Measure(option), PixelFont.Measure(hint))) + Padding * 2;
        float height = Padding * 2 + bodyHeight + optionHeight + 6f + PixelFont.MeasureHeight(hint);
        float x = Mathf.Round((Screen.width / (float)p - width) * 0.5f);
        float y = Mathf.Floor(Screen.height * 0.92f / p) - height - 6f;
        Matrix4x4 previousMatrix = GUI.matrix;
        Color previousColor = GUI.color;
        GUI.matrix = Matrix4x4.Scale(new Vector3(p, p, 1f));
        DrawPanel(new Rect(x, y, width, height), 1f);
        PixelFont.Draw(x + Padding, y + Padding,
            message.Substring(0, Mathf.Clamp(visibleCharacters, 0, message.Length)), Ink);
        Rect choice = default;
        float footerY = y + Padding + bodyHeight + 6f;
        if (question != null)
        {
            choice = new Rect(x + Padding - 2f, footerY - 2f, width - Padding * 2 + 4f,
                PixelFont.MeasureHeight(option) + 4f);
            GUI.color = new Color(Ink.r, Ink.g, Ink.b, 0.14f);
            GUI.DrawTexture(choice, Texture2D.whiteTexture);
            GUI.color = Color.white;
            PixelFont.Draw(x + Padding, footerY, option, Ink);
            footerY += optionHeight;
        }
        PixelFont.Draw(x + Padding, footerY, hint, new Color(Ink.r, Ink.g, Ink.b, 0.7f));
        GUI.color = previousColor;
        GUI.matrix = previousMatrix;
        return new Rect(choice.x * p, choice.y * p, choice.width * p, choice.height * p);
    }

    private static void DrawPanel(Rect rect, float opacity)
    {
        if (messageStyle == null || messageStyle.normal.background == null)
            messageStyle = InventoryPixelArt.NineSlice(InventoryPixelArt.SpeechBubbleTexture,
                InventoryPixelArt.SpeechBubbleBorder);
        GUI.color = new Color(0f, 0f, 0f, opacity * 0.35f);
        messageStyle.Draw(new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height), false, false, false, false);
        GUI.color = new Color(1f, 1f, 1f, opacity);
        messageStyle.Draw(rect, false, false, false, false);
    }
}
