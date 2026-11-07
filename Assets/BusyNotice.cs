using UnityEngine;

// What an NPC says instead of talking while the player is busy with the potion hunt: one line in
// the speech panel at the bottom of the screen (the opening's message panel) for a few seconds,
// fading in and out. It doesn't stop the player or move the camera; pressing E again, or at
// another NPC, just replaces the line.
public class BusyNotice : MonoBehaviour
{
    public const float Duration = 3f;
    private const float FadeTime = 0.25f;
    private const int PixelSize = 3;

    private static BusyNotice instance;
    private string message;
    private float shown;

    // The line on screen, or null.
    public static string Showing => instance != null && instance.shown < Duration ? instance.message : null;

    public static void Show(string line)
    {
        if (instance == null)
        {
            instance = new GameObject("Busy notice").AddComponent<BusyNotice>();
        }
        instance.message = line;
        instance.shown = 0f;
    }

    // Takes the line off the screen at once.
    public static void Clear()
    {
        if (instance == null) return;
        if (Application.isPlaying) Destroy(instance.gameObject);
        else DestroyImmediate(instance.gameObject);
        instance = null;
    }

    private void Update()
    {
        shown += Time.unscaledDeltaTime;
    }

    private void OnGUI()
    {
        if (Event.current.type != EventType.Repaint || shown >= Duration || TitleScreen.IsOpen || PauseMenu.IsPaused) return;
        float opacity = Mathf.Clamp01(shown / FadeTime) * Mathf.Clamp01((Duration - shown) / FadeTime);
        CinematicOverlay.DrawMessage(message, opacity, PixelSize);
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }
}
