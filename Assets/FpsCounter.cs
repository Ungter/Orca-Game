using UnityEngine;
using UnityEngine.InputSystem;

// A small frames-per-second readout in the top-right corner, toggled with the ] key.
// Averaged over half a second so the number is readable. Drawn in the pixel font on top
// of every other UI, including the pause menu. Installs itself when the game starts.
public class FpsCounter : MonoBehaviour
{
    private const float SampleTime = 0.5f;
    // Font pixel size at 1280x720, and the gap from the screen corner in font pixels.
    private const int PixelSize = 2;
    private const int Margin = 3;

    private static readonly Color Parchment = new Color32(233, 214, 168, 255);
    private static readonly Color Outline = new Color32(26, 15, 10, 220);

    private bool shown;
    private float elapsed;
    private int frames;
    private int fps = -1;
    private string label = "";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        var holder = new GameObject("FPS Counter");
        DontDestroyOnLoad(holder);
        holder.AddComponent<FpsCounter>();
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.rightBracketKey.wasPressedThisFrame)
        {
            shown = !shown;
            elapsed = 0f;
            frames = 0;
        }
        if (!shown)
        {
            return;
        }

        // Unscaled, so it keeps counting while the game is paused.
        elapsed += Time.unscaledDeltaTime;
        frames++;
        if (elapsed >= SampleTime)
        {
            int next = Mathf.RoundToInt(frames / elapsed);
            if (next != fps)
            {
                fps = next;
                label = fps + " FPS";
            }
            elapsed = 0f;
            frames = 0;
        }
    }

    private void OnGUI()
    {
        // Above the pause menu (-1000) and the title screen (-900).
        GUI.depth = -2000;
        if (!shown || fps < 0 || Event.current.type != EventType.Repaint)
        {
            return;
        }
        float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
        int pixel = Mathf.Max(1, Mathf.RoundToInt(PixelSize * scale));
        float x = Screen.width - (PixelFont.Measure(label) + Margin) * pixel;
        PixelFont.Draw(new Vector2(x, Margin * pixel), label, pixel, Parchment, Outline);
    }
}
