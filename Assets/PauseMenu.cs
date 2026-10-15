using UnityEngine;
using UnityEngine.InputSystem;

// Esc pauses the game: the scene dims behind a parchment "PAUSED" banner, a max FPS
// slider and a wooden Quit button, drawn with the inventory HUD's pixel art and PixelFont.
// Runs before InventoryInspection so Esc closes an open inspection instead of pausing.
[DefaultExecutionOrder(-200)]
public class PauseMenu : MonoBehaviour
{
    public static bool IsPaused { get; private set; }

    // Screen pixels per art pixel at 1280x720; rounded to a whole number at any resolution.
    [SerializeField]
    private int pixelSize = 4;

    [SerializeField, Range(0f, 1f)]
    private float dimAlpha = 0.65f;

    private const float FadeDuration = 0.15f;
    private const string Title = "PAUSED";
    private const string Label = "QUIT";
    private const string Hint = "[ESC] RESUME";
    private const string FrameRateTitle = "MAX FPS";
    private const string FrameRateKey = "PauseMenu.MaxFps";
    private const int Unlimited = -1;

    // Slider stops, lowest first. Unlimited matches Application.targetFrameRate's "no cap".
    private static readonly int[] FrameRateStops = { 30, 45, 60, 75, 90, 120, 144, 165, 240, Unlimited };

    // Layout in art pixels, measured down from the menu origin. Track and knob sizes
    // match InventoryPixelArt.SliderTrack and SliderKnob.
    private const int BannerHeight = 14;
    private const int SectionGap = 6;
    private const int SliderWidth = 79;
    private const int TrackHeight = 6;
    private const int KnobWidth = 7;
    private const int KnobHeight = 10;
    private const int ButtonWidth = 52;
    private const int ButtonHeight = 22;
    private const int HintGap = 4;
    private const int SliderTop = BannerHeight + SectionGap;
    private const int KnobTop = SliderTop + PixelFont.CapHeight + 3;
    private const int TrackTop = KnobTop + (KnobHeight - TrackHeight) / 2;
    private const int ButtonTop = KnobTop + KnobHeight + SectionGap;
    private const int HintTop = ButtonTop + ButtonHeight + HintGap;
    private const int MenuHeight = HintTop + PixelFont.CapHeight + 1;

    private static readonly Color Ink = new Color32(59, 36, 22, 255);
    private static readonly Color InkShadow = new Color32(200, 168, 114, 140);
    private static readonly Color Parchment = new Color32(233, 214, 168, 255);
    private static readonly Color ParchmentDim = new Color32(200, 168, 114, 255);
    private static readonly Color Gold = new Color32(242, 211, 107, 255);
    private static readonly Color Outline = new Color32(26, 15, 10, 220);

    private float previousTimeScale = 1f;
    private float opacity;
    private bool hovered;
    // The mouse went down on the button and has not been released yet.
    private bool pressed;
    private bool sliderHovered;
    private bool dragging;
    private int frameRateStop;
    private GUIStyle buttonStyle;
    private GUIStyle buttonHoverStyle;
    private GUIStyle bannerStyle;
    private GUIStyle trackStyle;
    private GUIStyle fillStyle;

    // Every scene gets a pause menu without having to place one by hand.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindAnyObjectByType<PauseMenu>() == null)
        {
            DontDestroyOnLoad(new GameObject("Pause menu", typeof(PauseMenu)));
        }
    }

    private void Awake()
    {
        frameRateStop = NearestStop(PlayerPrefs.GetInt(FrameRateKey, Unlimited));
        ApplyFrameRate();
    }

    private void OnDisable()
    {
        Resume();
        opacity = 0f;
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            if (IsPaused)
            {
                Resume();
            }
            else if (!InventoryInspection.IsOpen)
            {
                Pause();
            }
        }

        opacity = Mathf.MoveTowards(opacity, IsPaused ? 1f : 0f, Time.unscaledDeltaTime / FadeDuration);

        Mouse mouse = Mouse.current;
        if (!IsPaused || mouse == null)
        {
            hovered = false;
            pressed = false;
            sliderHovered = false;
            dragging = false;
            return;
        }

        if (keyboard != null)
        {
            if (keyboard.leftArrowKey.wasPressedThisFrame || keyboard.aKey.wasPressedThisFrame)
            {
                SetFrameRateStop(frameRateStop - 1);
            }
            if (keyboard.rightArrowKey.wasPressedThisFrame || keyboard.dKey.wasPressedThisFrame)
            {
                SetFrameRateStop(frameRateStop + 1);
            }
        }

        Vector2 position = mouse.position.ReadValue();
        var pointer = new Vector2(position.x, Screen.height - position.y);
        hovered = ButtonRect().Contains(pointer);
        sliderHovered = SliderRect().Contains(pointer);
        if (mouse.leftButton.wasPressedThisFrame)
        {
            pressed = hovered;
            dragging = sliderHovered;
        }

        if (dragging)
        {
            // Clicking the track jumps to the nearest stop; holding drags the knob between stops.
            SetFrameRateStop(StopAt(pointer.x));
            dragging = mouse.leftButton.isPressed;
        }

        if (mouse.leftButton.wasReleasedThisFrame)
        {
            // Like any button, it fires on release and only if the press started on it.
            bool clicked = pressed && hovered;
            pressed = false;
            if (clicked)
            {
                Quit();
            }
        }
    }

    private void Pause()
    {
        IsPaused = true;
        previousTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        AudioListener.pause = true;
    }

    private void Resume()
    {
        if (!IsPaused)
        {
            return;
        }
        IsPaused = false;
        Time.timeScale = previousTimeScale;
        AudioListener.pause = false;
        PlayerPrefs.Save();
    }

    private static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void SetFrameRateStop(int stop)
    {
        stop = Mathf.Clamp(stop, 0, FrameRateStops.Length - 1);
        if (stop == frameRateStop)
        {
            return;
        }
        frameRateStop = stop;
        PlayerPrefs.SetInt(FrameRateKey, FrameRateStops[stop]);
        ApplyFrameRate();
    }

    private void ApplyFrameRate()
    {
        // Any vsync count overrides targetFrameRate, so the cap only holds with vsync off.
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = FrameRateStops[frameRateStop];
    }

    // Saved rates that are no longer a stop snap to the closest one.
    private static int NearestStop(int frameRate)
    {
        if (frameRate <= 0)
        {
            return FrameRateStops.Length - 1;
        }

        int nearest = 0;
        for (int i = 1; i < FrameRateStops.Length; i++)
        {
            int stop = FrameRateStops[i];
            if (stop > 0 && Mathf.Abs(stop - frameRate) < Mathf.Abs(FrameRateStops[nearest] - frameRate))
            {
                nearest = i;
            }
        }
        return nearest;
    }

    private static string FrameRateText(int frameRate)
    {
        return frameRate > 0 ? frameRate.ToString() : "UNLIMITED";
    }

    private static float UIScale()
    {
        return Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
    }

    private int PixelScale()
    {
        return Mathf.Max(1, Mathf.RoundToInt(pixelSize * UIScale()));
    }

    // Top-centre of the menu in screen pixels (IMGUI top-left origin), snapped to whole pixels.
    private Vector2 MenuOrigin()
    {
        Rect safeArea = Screen.safeArea;
        // Screen.safeArea uses a bottom-left origin.
        float centreY = Screen.height - safeArea.center.y;
        return new Vector2(Mathf.Round(safeArea.center.x), Mathf.Round(centreY - MenuHeight * PixelScale() * 0.5f));
    }

    private static float ButtonX()
    {
        return Mathf.Floor(-ButtonWidth * 0.5f);
    }

    private static float SliderX()
    {
        return Mathf.Floor(-SliderWidth * 0.5f);
    }

    // Left edge of the knob at a stop, in art pixels.
    private static float KnobX(int stop)
    {
        return SliderX() + Mathf.Round(stop * (SliderWidth - KnobWidth) / (float)(FrameRateStops.Length - 1));
    }

    // Nearest stop to a screen x position.
    private int StopAt(float screenX)
    {
        float artX = (screenX - MenuOrigin().x) / PixelScale();
        float t = (artX - SliderX() - KnobWidth * 0.5f) / (SliderWidth - KnobWidth);
        return Mathf.RoundToInt(Mathf.Clamp01(t) * (FrameRateStops.Length - 1));
    }

    // Hit areas in screen pixels, matching where OnGUI draws them.
    private Rect ButtonRect()
    {
        return ArtToScreen(new Rect(ButtonX(), ButtonTop, ButtonWidth, ButtonHeight));
    }

    private Rect SliderRect()
    {
        return ArtToScreen(new Rect(SliderX(), KnobTop, SliderWidth, KnobHeight));
    }

    private Rect ArtToScreen(Rect art)
    {
        int p = PixelScale();
        Vector2 origin = MenuOrigin();
        return new Rect(origin.x + art.x * p, origin.y + art.y * p, art.width * p, art.height * p);
    }

    private void OnGUI()
    {
        // Lower depth draws on top: stay above the inventory HUD and pickup prompts.
        GUI.depth = -1000;
        if (opacity <= 0f || Event.current.type != EventType.Repaint)
        {
            return;
        }

        BuildStyles();
        Color previousColor = GUI.color;
        Matrix4x4 previousMatrix = GUI.matrix;

        GUI.color = new Color(0f, 0f, 0f, dimAlpha * opacity);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);

        int p = PixelScale();
        Vector2 origin = MenuOrigin();
        // Draw in art pixels: one GUI unit is one art pixel, an exact p x p block on screen.
        GUI.matrix = Matrix4x4.TRS(new Vector3(origin.x, origin.y, 0f), Quaternion.identity, new Vector3(p, p, 1f));
        GUI.color = new Color(1f, 1f, 1f, previousColor.a * opacity);

        DrawBanner();
        DrawSlider();
        DrawButton();

        int hintWidth = PixelFont.Measure(Hint);
        PixelFont.Draw(Mathf.Floor(-hintWidth * 0.5f), HintTop, Hint, ParchmentDim, Outline);

        GUI.color = previousColor;
        GUI.matrix = previousMatrix;
    }

    private void DrawBanner()
    {
        int titleWidth = PixelFont.Measure(Title);
        int bannerWidth = InventoryPixelArt.BannerBorder.horizontal + titleWidth + 6;
        float x = Mathf.Floor(-bannerWidth * 0.5f);
        bannerStyle.Draw(new Rect(x, 0f, bannerWidth, BannerHeight), false, false, false, false);
        PixelFont.Draw(x + InventoryPixelArt.BannerBorder.left + 3, 4f, Title, Ink, InkShadow);
    }

    // "MAX FPS" and the current cap over a groove; gold fills it up to the knob.
    private void DrawSlider()
    {
        float x = SliderX();
        string value = FrameRateText(FrameRateStops[frameRateStop]);
        PixelFont.Draw(x, SliderTop, FrameRateTitle, Parchment, Outline);
        PixelFont.Draw(x + SliderWidth - PixelFont.Measure(value), SliderTop, value, Gold, Outline);

        trackStyle.Draw(new Rect(x, TrackTop, SliderWidth, TrackHeight), false, false, false, false);
        float knobX = KnobX(frameRateStop);
        float fillWidth = knobX - x + Mathf.Ceil(KnobWidth * 0.5f);
        // At the first stop the fill is narrower than its own end caps and hides under the knob.
        if (fillWidth >= InventoryPixelArt.SliderBorder.horizontal)
        {
            fillStyle.Draw(new Rect(x, TrackTop, fillWidth, TrackHeight), false, false, false, false);
        }

        Texture2D knob = sliderHovered || dragging ? InventoryPixelArt.SliderKnobLitTexture : InventoryPixelArt.SliderKnobTexture;
        GUI.DrawTexture(new Rect(knobX, KnobTop, KnobWidth, KnobHeight), knob);
    }

    // An inventory slot frame stretched into a button; hover swaps in the gold-studded frame.
    private void DrawButton()
    {
        float x = ButtonX();
        (hovered ? buttonHoverStyle : buttonStyle).Draw(new Rect(x, ButtonTop, ButtonWidth, ButtonHeight), false, false, false, false);

        RectOffset border = InventoryPixelArt.SlotBorder;
        float wellWidth = ButtonWidth - border.horizontal;
        float wellHeight = ButtonHeight - border.vertical;
        float labelX = x + border.left + Mathf.Floor((wellWidth - PixelFont.Measure(Label)) * 0.5f);
        // The label sinks a pixel while the button is held down.
        float labelY = ButtonTop + border.top + Mathf.Floor((wellHeight - PixelFont.CapHeight) * 0.5f) + (pressed && hovered ? 1f : 0f);
        PixelFont.Draw(labelX, labelY, Label, hovered ? Gold : Parchment, Outline);
    }

    private void BuildStyles()
    {
        if (buttonStyle != null && buttonStyle.normal.background != null)
        {
            return;
        }

        buttonStyle = NineSlice(InventoryPixelArt.SlotTexture, InventoryPixelArt.SlotBorder);
        buttonHoverStyle = NineSlice(InventoryPixelArt.SlotFilledTexture, InventoryPixelArt.SlotBorder);
        bannerStyle = NineSlice(InventoryPixelArt.BannerTexture, InventoryPixelArt.BannerBorder);
        trackStyle = NineSlice(InventoryPixelArt.SliderTrackTexture, InventoryPixelArt.SliderBorder);
        fillStyle = NineSlice(InventoryPixelArt.SliderFillTexture, InventoryPixelArt.SliderBorder);
    }

    private static GUIStyle NineSlice(Texture2D texture, RectOffset border)
    {
        var style = new GUIStyle { border = new RectOffset(border.left, border.right, border.top, border.bottom) };
        style.normal.background = texture;
        return style;
    }
}
