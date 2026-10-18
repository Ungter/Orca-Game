using UnityEngine;

// The "[E] ACTION" prompt floating above a world interactable (pickups, talkable
// objects). Drawn in screen space at the anchor's projected position, so it always
// faces the camera and keeps a constant on-screen gap above the object.
public static class InteractPrompt
{
    private static readonly Color KeyColor = new Color32(242, 211, 107, 255);
    private static readonly Color TextColor = new Color32(233, 214, 168, 255);
    private static readonly Color ShadowColor = new Color32(26, 15, 10, 230);

    // Same reference resolution as the inventory HUD, so prompts scale with it.
    public static float UIScale()
    {
        return Mathf.Max(0.5f, Mathf.Min(Screen.width / 1280f, Screen.height / 720f));
    }

    // anchor: world point the prompt sits above. offsetY: gap in reference pixels at 720p.
    // pixelSize: screen pixels per font pixel at 720p. alpha: linear 0-1 fade, eased here.
    public static void Draw(Vector3 anchor, float offsetY, string key, string action, int pixelSize, float alpha)
    {
        if (alpha <= 0f || Camera.main == null)
        {
            return;
        }

        Vector3 screenPos = Camera.main.WorldToScreenPoint(anchor);
        if (screenPos.z <= 0f)
        {
            return; // Behind the camera.
        }

        string keyText = "[" + key.ToUpperInvariant() + "]";
        string actionText = string.IsNullOrEmpty(action) ? "" : " " + action.ToUpperInvariant();
        int width = PixelFont.Measure(keyText + actionText);

        float uiScale = UIScale();
        int p = Mathf.Max(1, Mathf.RoundToInt(pixelSize * uiScale));
        // WorldToScreenPoint counts y from the bottom; IMGUI counts from the top.
        float bottom = Screen.height - screenPos.y - offsetY * uiScale;
        var topLeft = new Vector2(screenPos.x - width * p * 0.5f, bottom - PixelFont.CapHeight * p);

        float a = alpha * alpha * (3f - 2f * alpha);
        Color shadow = Fade(ShadowColor, a);
        PixelFont.Draw(topLeft, keyText, p, Fade(KeyColor, a), shadow);
        if (actionText.Length > 0)
        {
            // Measure(key) excludes trailing spacing; add it back so the two runs join seamlessly.
            float offset = (PixelFont.Measure(keyText) + PixelFont.Spacing) * p;
            PixelFont.Draw(topLeft + new Vector2(offset, 0f), actionText, p, Fade(TextColor, a), shadow);
        }
    }

    private static Color Fade(Color color, float alpha)
    {
        return new Color(color.r, color.g, color.b, color.a * alpha);
    }
}
