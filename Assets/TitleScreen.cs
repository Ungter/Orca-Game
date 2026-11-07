using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// Title screen shown when the game starts. The camera circles high above the island while a
// parchment "ORCA PLACEHOLDER" scroll floats over the island, the orca leaps
// out from behind it and wooden PLAY and EXIT buttons (the pause menu's buttons) wait below.
// PLAY flies out to the player's boat and rides with it in to the dock (OpeningVoyage); SPACE
// jumps onto the dock, then the camera eases into gameplay. EXIT quits the game. It opens on
// every load of a gameplay scene, including the pause menu's HOME.
// Runs after CameraFollow so LateUpdate can blend from the title view into the follow view.
[DefaultExecutionOrder(100)]
public class TitleScreen : MonoBehaviour
{
    public static bool IsOpen { get; private set; }

    // Screen pixels per art pixel at 1280x720; rounded to a whole number at any resolution.
    [SerializeField]
    private int pixelSize = 4;

    [SerializeField, Range(0f, 1f)]
    private float dimAlpha = 0.25f;

    [Header("Camera")]
    [SerializeField, Tooltip("Degrees the camera looks down at the island.")]
    private float pitch = 35f;
    [SerializeField, Tooltip("Degrees per second the camera circles the island.")]
    private float orbitSpeed = 3f;
    [SerializeField, Tooltip("Distance multiplier; above 1 shows more sea around the island.")]
    private float framing = 1f;
    [SerializeField]
    private float flightDuration = 2.4f;

    private const string Title = "ORCA PLACEHOLDER";
    private const string Label = "PLAY";
    private const string ExitLabel = "EXIT";
    private const string OrcaPath = "TitleScreen/Orca Sprite";
    private const string MapName = "IslanddemoV3";
    private const string OpeningMessage = "SOMEONE'S BEEN MURDERED!";

    private const float FadeInDuration = 1.2f;
    private const float UIFadeDuration = 0.4f;
    private const float HandoffDuration = 1.2f;
    // The opening message shows this long after the boat sets off, for MessageDuration.
    private const float MessageStart = 1.2f;
    private const float MessageDuration = 2.8f;
    private const float MessageFadeDuration = 0.2f;
    private const string JumpKey = "SPACE";
    private const int MaxTitleScale = 3;
    private const int CornerScale = 2;
    private const int CornerMargin = 3;

    // The orca sheet is two 64x64 frames stacked vertically, drawn 4 texels per art pixel.
    // Content spans rows 8-56 of a frame and is centred 27 pixels from its left edge.
    private const int OrcaFrameSize = 64;
    private const int OrcaContentTop = 8;
    private const int OrcaContentBottom = 56;
    private const int OrcaContentCentre = 27;
    // The tail tucks this far behind the top of the scroll.
    private const int OrcaOverlap = 5;
    private const float OrcaFrameRate = 3f;

    // The scroll drifts at most this many art pixels either side of centre, once
    // there and back every SwayPeriod seconds. It moves in whole screen pixels, not art pixels,
    // so it glides instead of hopping between a handful of positions and resting at the ends.
    private const float SwayAmplitude = 2f;
    private const float SwayPeriod = 5f;

    // Layout in art pixels below the scroll.
    private const int SectionGap = 6;
    private const int DividerHalfWidth = 40;
    private const int ButtonGap = 3;

    // From high over the island the scene's fog hid it completely and the moon left it too dark
    // to read. While the title is up, and through the flight and the crossing, the fog thins so
    // the island stays about 80% clear at the camera's distance (never thicker than the scene's),
    // and the moon is brighter. Both ease back to the scene's own over the hand-off to gameplay.
    public const float TitleFogReach = 0.45f;
    public const float TitleMoonBoost = 1.8f;

    private static readonly Color Ink = new Color32(59, 36, 22, 255);
    private static readonly Color InkShadow = new Color32(200, 168, 114, 140);
    private static readonly Color Gold = new Color32(212, 160, 42, 255);
    private static readonly Color Outline = new Color32(26, 15, 10, 220);
    private static readonly Color GoldHighlight = new Color32(255, 206, 84, 255);

    private readonly List<Behaviour> frozen = new List<Behaviour>();
    private readonly List<Behaviour> cameraRigs = new List<Behaviour>();
    private Camera cam;
    private Texture2D orca;
    private GUIStyle bannerStyle;

    private Vector3 islandCentre;
    private float orbitDistance;
    private float orbitYaw;
    private float originalFarClip;

    private float uiOpacity;
    private float veil = 1f;
    // 0 = PLAY, 1 = EXIT. Moved by arrows/W/S or the mouse; Enter presses it.
    private int selectedButton;
    // Button under the mouse: 0 = PLAY, 1 = EXIT, -1 = none.
    private int hoveredButton = -1;
    // The button the mouse went down on and has not been released yet, or -1.
    private int pressedButton = -1;
    private bool starting;
    private float flightTime;
    private Vector3 flightStartPosition;
    private Quaternion flightStartRotation;
    private OpeningVoyage opening;
    // Seconds since the boat set off, and since the hand-off to gameplay began.
    private float voyageTime;
    private float handoffTime;
    private bool handingOff;
    private Vector3 handoffPosition;
    private Quaternion handoffRotation;
    // The scene's own fog and moon, put back when the title is done.
    private bool atmosphereHeld;
    private float sceneFogDensity;
    private Light moon;
    private float moonIntensity;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        IsOpen = false;
    }

    // Gameplay scenes (the ones with a follow camera) open on the title screen, on the first load
    // and on every reload. Unsubscribing first keeps play mode without domain reload to one handler.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Single)
        {
            Bootstrap();
        }
    }

    // Also runs after the first scene loads, in case sceneLoaded fired before Register.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindAnyObjectByType<CameraFollow>() != null && FindAnyObjectByType<TitleScreen>() == null)
        {
            new GameObject("Title screen", typeof(TitleScreen));
        }
    }

    public static void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void Awake()
    {
        cam = Camera.main;
        if (cam == null)
        {
            Destroy(gameObject);
            return;
        }

        IsOpen = true;
        Freeze<CameraFollow>(cameraRigs);
        Freeze<PlayerController>(frozen);
        Freeze<InventoryHUD>(frozen);
        Freeze<InventoryInspection>(frozen);
        Freeze<PickupItem>(frozen);

        orca = Resources.Load<Texture2D>(OrcaPath);
        if (orca != null)
        {
            orca.filterMode = FilterMode.Point;
        }
        FrameIsland();
        orbitYaw = cam.transform.eulerAngles.y;
        originalFarClip = cam.farClipPlane;
        cam.farClipPlane = Mathf.Max(originalFarClip, orbitDistance * 3f);
        cam.transform.SetPositionAndRotation(TitlePosition(), TitleRotation());
        HoldAtmosphere();
        ApplyAtmosphere(0f);
    }

    private void OnDestroy()
    {
        opening?.Dispose();
        Thaw(cameraRigs);
        Thaw(frozen);
        IsOpen = false;
        RestoreAtmosphere();
        if (cam != null)
        {
            cam.farClipPlane = originalFarClip;
        }
    }

    // Fog density that keeps the island about 80% clear (exp2 fog: e^-(density x distance)^2)
    // from cameraDistance away, never thicker than the scene's own.
    public static float TitleFogDensity(float sceneDensity, float cameraDistance) =>
        Mathf.Min(sceneDensity, TitleFogReach / Mathf.Max(cameraDistance, 1f));

    private void HoldAtmosphere()
    {
        sceneFogDensity = RenderSettings.fogDensity;
        moon = RenderSettings.sun;
        if (moon == null)
        {
            foreach (Light light in FindObjectsByType<Light>())
            {
                if (light.type == LightType.Directional)
                {
                    moon = light;
                    break;
                }
            }
        }
        moonIntensity = moon != null ? moon.intensity : 0f;
        atmosphereHeld = true;
    }

    // toScene: 0 for the title's thin fog and bright moon, 1 for the scene's own.
    private void ApplyAtmosphere(float toScene)
    {
        if (!atmosphereHeld) return;
        toScene = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(toScene));
        float distance = Vector3.Distance(cam.transform.position, islandCentre);
        RenderSettings.fogDensity = Mathf.Lerp(TitleFogDensity(sceneFogDensity, distance), sceneFogDensity, toScene);
        if (moon != null) moon.intensity = moonIntensity * Mathf.Lerp(TitleMoonBoost, 1f, toScene);
    }

    private void RestoreAtmosphere()
    {
        if (!atmosphereHeld) return;
        atmosphereHeld = false;
        RenderSettings.fogDensity = sceneFogDensity;
        if (moon != null) moon.intensity = moonIntensity;
    }

    private static void Freeze<T>(List<Behaviour> into) where T : Behaviour
    {
        foreach (T behaviour in FindObjectsByType<T>())
        {
            if (behaviour.enabled)
            {
                behaviour.enabled = false;
                into.Add(behaviour);
            }
        }
    }

    private static void Thaw(List<Behaviour> behaviours)
    {
        foreach (Behaviour behaviour in behaviours)
        {
            if (behaviour != null)
            {
                behaviour.enabled = true;
            }
        }
        behaviours.Clear();
    }

    // Centres the orbit on the island's land, ignoring the ocean plane that surrounds it.
    private void FrameIsland()
    {
        GameObject map = GameObject.Find(MapName);
        Renderer[] renderers = map != null
            ? map.GetComponentsInChildren<Renderer>()
            : FindObjectsByType<MeshRenderer>();

        Bounds bounds = default;
        bool found = false;
        foreach (Renderer meshRenderer in renderers)
        {
            if (!meshRenderer.enabled || meshRenderer.name.ToLowerInvariant().Contains("ocean"))
            {
                continue;
            }
            if (found)
            {
                bounds.Encapsulate(meshRenderer.bounds);
            }
            else
            {
                bounds = meshRenderer.bounds;
                found = true;
            }
        }
        if (!found)
        {
            bounds = new Bounds(cam.transform.position, Vector3.one * 40f);
        }

        islandCentre = new Vector3(bounds.center.x, bounds.min.y + bounds.size.y * 0.2f, bounds.center.z);
        float radius = Mathf.Max(bounds.extents.x, bounds.extents.z);
        orbitDistance = framing * radius / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
    }

    private Vector3 TitlePosition()
    {
        return islandCentre + TitleRotation() * Vector3.back * orbitDistance;
    }

    private Quaternion TitleRotation()
    {
        return Quaternion.Euler(pitch, orbitYaw, 0f);
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        veil = Mathf.MoveTowards(veil, 0f, dt / FadeInDuration);
        uiOpacity = Mathf.MoveTowards(uiOpacity, starting ? 0f : 1f, dt / UIFadeDuration);

        if (starting)
        {
            flightTime += dt;
            float arrival = Mathf.Max(0.01f, flightDuration);
            if (opening == null)
            {
                if (flightTime >= arrival) Finish();
                return;
            }
            if (flightTime < arrival) return;
            if (!handingOff)
            {
                // The boat sets off once the camera has reached it; SPACE jumps ashore once it is in.
                voyageTime += dt;
                Keyboard jumpKeys = Keyboard.current;
                opening.Tick(dt, jumpKeys != null && jumpKeys.spaceKey.wasPressedThisFrame);
                if (opening.HasLanded)
                {
                    opening.Dispose();
                    foreach (Behaviour rig in cameraRigs)
                        if (rig is CameraFollow follow) follow.LookTowards(opening.Inland);
                    handoffPosition = cam.transform.position;
                    handoffRotation = cam.transform.rotation;
                    handingOff = true;
                    handoffTime = 0f;
                    Thaw(cameraRigs);
                }
                return;
            }
            handoffTime += dt;
            if (handoffTime >= HandoffDuration) Finish();
            return;
        }

        orbitYaw = Mathf.Repeat(orbitYaw + orbitSpeed * dt, 360f);

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.upArrowKey.wasPressedThisFrame || keyboard.wKey.wasPressedThisFrame)
            {
                selectedButton = 0;
            }
            if (keyboard.downArrowKey.wasPressedThisFrame || keyboard.sKey.wasPressedThisFrame)
            {
                selectedButton = 1;
            }
            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
            {
                Press(selectedButton);
                return;
            }
        }

        Mouse mouse = Mouse.current;
        if (mouse == null)
        {
            hoveredButton = -1;
            pressedButton = -1;
            return;
        }

        Vector2 position = mouse.position.ReadValue();
        var pointer = new Vector2(position.x, Screen.height - position.y);
        Layout layout = ComputeLayout();
        hoveredButton = -1;
        for (int i = 0; i < 2; i++)
        {
            if (ArtToScreen(layout, new Rect(ButtonX(), ButtonTop(layout, i), PixelButton.Width, PixelButton.Height)).Contains(pointer))
            {
                hoveredButton = i;
            }
        }
        // Only a moving or clicking mouse takes the selection, so a parked cursor never fights the keys.
        if (hoveredButton >= 0 && (mouse.delta.ReadValue().sqrMagnitude > 0f || mouse.leftButton.wasPressedThisFrame))
        {
            selectedButton = hoveredButton;
        }
        if (mouse.leftButton.wasPressedThisFrame)
        {
            pressedButton = hoveredButton;
        }
        if (mouse.leftButton.wasReleasedThisFrame)
        {
            // Like any button, it fires on release and only if the press started on it.
            int clicked = pressedButton >= 0 && pressedButton == hoveredButton ? pressedButton : -1;
            pressedButton = -1;
            if (clicked >= 0)
            {
                Press(clicked);
            }
        }
    }

    private void Press(int button)
    {
        if (button == 0)
        {
            Begin();
        }
        else
        {
            QuitGame();
        }
    }

    private void LateUpdate()
    {
        PlaceCamera();
        // Back to the scene's fog and moon over the hand-off, or over the flight with no crossing.
        float toScene = handingOff ? handoffTime / HandoffDuration
            : starting && opening == null ? flightTime / Mathf.Max(0.01f, flightDuration) : 0f;
        ApplyAtmosphere(toScene);
    }

    private void PlaceCamera()
    {
        if (!starting)
        {
            cam.transform.SetPositionAndRotation(TitlePosition(), TitleRotation());
            return;
        }

        Transform view = cam.transform;
        float arrival = Mathf.Max(0.01f, flightDuration);
        if (opening != null && !handingOff)
        {
            opening.GetCameraPose(out Vector3 position, out Quaternion rotation);
            float approach = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(flightTime / arrival));
            view.SetPositionAndRotation(Vector3.Lerp(flightStartPosition, position, approach),
                Quaternion.Slerp(flightStartRotation, rotation, approach));
            opening.AnimatePlayer(Time.unscaledDeltaTime);
            return;
        }

        // CameraFollow has placed this frame's gameplay view; blend into it without a cut.
        float blend = handingOff ? handoffTime / HandoffDuration : flightTime / arrival;
        blend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(blend));
        view.SetPositionAndRotation(
            Vector3.Lerp(handingOff ? handoffPosition : flightStartPosition, view.position, blend),
            Quaternion.Slerp(handingOff ? handoffRotation : flightStartRotation, view.rotation, blend));
        opening?.AnimatePlayer(Time.unscaledDeltaTime);
    }

    private void Begin()
    {
        if (starting) return;
        starting = true;
        hoveredButton = -1;
        pressedButton = -1;
        flightTime = 0f;
        flightStartPosition = cam.transform.position;
        flightStartRotation = cam.transform.rotation;
        voyageTime = 0f;
        GameObject map = GameObject.Find(MapName);
        PlayerController player = FindAnyObjectByType<PlayerController>();
        // Without a dock to sail to, PLAY just flies down to the player where they stand.
        if (!OpeningVoyage.TryCreate(map != null ? map.transform : null, player, out opening))
            Thaw(cameraRigs);
    }

    private void Finish()
    {
        opening?.Dispose();
        Thaw(cameraRigs);
        Thaw(frozen);
        RestoreAtmosphere();
        enabled = false;
        Destroy(gameObject);
    }

    // Everything in art pixels, measured from the top-centre of the screen.
    private struct Layout
    {
        public int Pixel;
        public Vector2 Origin;
        public float Left;
        public float Right;
        public float Bottom;
        public int TitleScale;
        public int BannerArtWidth;
        public float BannerX;
        public float BannerTop;
        public float BannerWidth;
        public float BannerHeight;
        public float DividerTop;
        public float ButtonTop;
    }

    private Layout ComputeLayout()
    {
        var layout = new Layout();
        float uiScale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
        int p = Mathf.Max(1, Mathf.RoundToInt(pixelSize * uiScale));
        layout.Pixel = p;
        layout.Origin = new Vector2(Mathf.Round(Screen.width * 0.5f), 0f);
        layout.Left = Mathf.Ceil(-layout.Origin.x / p);
        layout.Right = Mathf.Floor((Screen.width - layout.Origin.x) / p);
        layout.Bottom = Mathf.Floor(Screen.height / (float)p);

        // The scroll is as large a whole-number multiple as fits within the side margins.
        layout.BannerArtWidth = InventoryPixelArt.BannerBorder.horizontal + PixelFont.Measure(Title) + 6;
        float screenArtWidth = layout.Right - layout.Left;
        layout.TitleScale = Mathf.Clamp(Mathf.FloorToInt(screenArtWidth * 0.8f / layout.BannerArtWidth), 1, MaxTitleScale);
        layout.BannerWidth = layout.BannerArtWidth * layout.TitleScale;
        layout.BannerHeight = InventoryPixelArt.Banner.Length * layout.TitleScale;
        layout.BannerX = Mathf.Floor(-layout.BannerWidth * 0.5f);

        // Orca, scroll, divider and buttons are centred as one block, so the scroll sits mid-screen.
        float orcaRise = OrcaContentBottom - OrcaContentTop - OrcaOverlap;
        float below = SectionGap + InventoryPixelArt.Gem.Length + SectionGap + PixelButton.Height * 2 + ButtonGap;
        float blockHeight = orcaRise + layout.BannerHeight + below;
        layout.BannerTop = Mathf.Round((layout.Bottom - blockHeight) * 0.5f) + orcaRise;
        layout.DividerTop = layout.BannerTop + layout.BannerHeight + SectionGap;
        layout.ButtonTop = layout.DividerTop + InventoryPixelArt.Gem.Length + SectionGap;
        return layout;
    }

    private static float ButtonTop(Layout layout, int button)
    {
        return layout.ButtonTop + button * (PixelButton.Height + ButtonGap);
    }

    private static float ButtonX()
    {
        return Mathf.Floor(-PixelButton.Width * 0.5f);
    }

    private static Rect ArtToScreen(Layout layout, Rect art)
    {
        int p = layout.Pixel;
        return new Rect(layout.Origin.x + art.x * p, layout.Origin.y + art.y * p, art.width * p, art.height * p);
    }

    private void OnGUI()
    {
        // Lower depth draws on top: above the (frozen) HUD, below the pause menu.
        GUI.depth = -900;
        if (Event.current.type != EventType.Repaint)
        {
            return;
        }

        Color previousColor = GUI.color;
        Matrix4x4 previousMatrix = GUI.matrix;

        if (uiOpacity > 0f)
        {
            Layout layout = ComputeLayout();
            int p = layout.Pixel;
            // Draw in art pixels: one GUI unit is one art pixel, an exact p x p block on screen.
            Matrix4x4 artMatrix = Matrix4x4.TRS(new Vector3(layout.Origin.x, layout.Origin.y, 0f), Quaternion.identity, new Vector3(p, p, 1f));
            GUI.matrix = artMatrix;
            float time = Time.unscaledTime;
            float sway = Sway(time, p);

            DrawShade(layout);
            DrawCorners(layout);
            DrawOrca(layout, time, sway);
            DrawBanner(layout, artMatrix, sway);
            GUI.matrix = artMatrix;
            DrawDivider(layout);

            GUI.color = new Color(1f, 1f, 1f, uiOpacity);
            for (int i = 0; i < 2; i++)
            {
                bool held = pressedButton == i && hoveredButton == i;
                PixelButton.Draw(ButtonX(), ButtonTop(layout, i), i == 0 ? Label : ExitLabel, selectedButton == i, held);
            }
            PixelButton.DrawSelection(ButtonX(), ButtonTop(layout, selectedButton));
        }

        if (starting && opening != null)
        {
            GUI.matrix = previousMatrix;
            float bars = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(flightTime / UIFadeDuration)) *
                CinematicBarsAmount(handingOff ? handoffTime : -1f);
            CinematicOverlay.DrawBars(bars);
            CinematicOverlay.DrawMessage(OpeningMessage, OpeningMessageOpacity(voyageTime), pixelSize);
            if (!handingOff)
                InteractPrompt.Draw(opening.JumpPromptAnchor, 10f, JumpKey, null, 3, opening.PromptAlpha);
        }

        if (veil > 0f)
        {
            GUI.matrix = previousMatrix;
            GUI.color = new Color(0f, 0f, 0f, veil);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
        }

        GUI.color = previousColor;
        GUI.matrix = previousMatrix;
    }

    private void Fill(Rect art, Color color)
    {
        color.a *= uiOpacity;
        GUI.color = color;
        GUI.DrawTexture(art, Texture2D.whiteTexture);
    }

    // The message shows while the boat crosses, MessageStart seconds after it sets off (time).
    private static float OpeningMessageOpacity(float time)
    {
        float elapsed = time - MessageStart;
        float fadeIn = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / MessageFadeDuration));
        float fadeOut = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((MessageDuration - elapsed) / MessageFadeDuration));
        return fadeIn * fadeOut;
    }

    // The letterbox stays for the whole opening and opens up over the hand-off to gameplay
    // (handoff: seconds into it, negative before it starts).
    private static float CinematicBarsAmount(float handoff)
    {
        return 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(handoff / HandoffDuration));
    }

    private void DrawSprite(Texture2D texture, float x, float y, int scale, Rect uv, float alpha = 1f)
    {
        GUI.color = new Color(1f, 1f, 1f, uiOpacity * alpha);
        float width = texture.width * Mathf.Abs(uv.width) * scale;
        float height = texture.height * Mathf.Abs(uv.height) * scale;
        GUI.DrawTextureWithTexCoords(new Rect(x, y, width, height), texture, uv);
    }

    // A light dim over the island, darkened in stepped bands along the top and bottom edges.
    private void DrawShade(Layout layout)
    {
        float width = layout.Right - layout.Left + 2f;
        Fill(new Rect(layout.Left - 1f, 0f, width, layout.Bottom + 1f), new Color(0f, 0f, 0f, dimAlpha));
        const int Bands = 4;
        const float BandHeight = 5f;
        for (int i = 0; i < Bands; i++)
        {
            var shade = new Color(0f, 0f, 0f, 0.3f * (1f - i / (float)Bands));
            Fill(new Rect(layout.Left - 1f, i * BandHeight, width, BandHeight), shade);
            Fill(new Rect(layout.Left - 1f, layout.Bottom - (i + 1) * BandHeight, width, BandHeight + (i == 0 ? 1f : 0f)), shade);
        }
    }

    // One gilded corner piece, mirrored into all four corners by flipping its texture coordinates.
    private void DrawCorners(Layout layout)
    {
        Texture2D corner = InventoryPixelArt.CornerTexture;
        float size = corner.width * CornerScale;
        float left = layout.Left + CornerMargin;
        float right = layout.Right - CornerMargin - size;
        float top = CornerMargin;
        float bottom = layout.Bottom - CornerMargin - size;
        DrawSprite(corner, left, top, CornerScale, new Rect(0f, 0f, 1f, 1f));
        DrawSprite(corner, right, top, CornerScale, new Rect(1f, 0f, -1f, 1f));
        DrawSprite(corner, left, bottom, CornerScale, new Rect(0f, 1f, 1f, -1f));
        DrawSprite(corner, right, bottom, CornerScale, new Rect(1f, 1f, -1f, -1f));
    }

    // Sideways offset in art pixels, snapped to whole screen pixels. Half sine, half triangle wave:
    // the sine alone lingers at each end once snapped, the triangle alone turns too sharply.
    private static float Sway(float time, int pixel)
    {
        float phase = time * 2f * Mathf.PI / SwayPeriod;
        float sine = Mathf.Sin(phase);
        float triangle = Mathf.Asin(sine) * 2f / Mathf.PI;
        return Mathf.Round(SwayAmplitude * pixel * (sine + triangle) * 0.5f) / pixel;
    }

    // The orca leaps out from behind the scroll, bobbing a pixel and flicking between its two frames.
    // It sways with the scroll so its tail stays tucked behind it.
    private void DrawOrca(Layout layout, float time, float sway)
    {
        if (orca == null)
        {
            return;
        }

        int frame = Mathf.FloorToInt(time * OrcaFrameRate) % 2;
        // Texture rows count from the bottom, so the sheet's top frame is the upper half.
        var uv = new Rect(0f, frame == 0 ? 0.5f : 0f, 1f, 0.5f);
        float bob = Mathf.Round(Mathf.Sin(time * 2f));
        float y = layout.BannerTop - OrcaContentBottom + OrcaOverlap + bob;
        GUI.color = new Color(1f, 1f, 1f, uiOpacity);
        GUI.DrawTextureWithTexCoords(new Rect(sway - OrcaContentCentre, y, OrcaFrameSize, OrcaFrameSize), orca, uv);
    }

    // The pause menu's parchment banner, blown up to a whole-number multiple for the title.
    private void DrawBanner(Layout layout, Matrix4x4 artMatrix, float sway)
    {
        if (bannerStyle == null || bannerStyle.normal.background == null)
        {
            bannerStyle = InventoryPixelArt.NineSlice(InventoryPixelArt.BannerTexture, InventoryPixelArt.BannerBorder);
        }

        int scale = layout.TitleScale;
        GUI.matrix = artMatrix * Matrix4x4.TRS(new Vector3(layout.BannerX + sway, layout.BannerTop, 0f), Quaternion.identity, new Vector3(scale, scale, 1f));
        GUI.color = new Color(1f, 1f, 1f, uiOpacity);
        bannerStyle.Draw(new Rect(0f, 0f, layout.BannerArtWidth, InventoryPixelArt.Banner.Length), false, false, false, false);
        PixelFont.Draw(InventoryPixelArt.BannerBorder.left + 3, 4f, Title, Ink, InkShadow);
    }

    // A gold rule with a gem at its centre, between the scroll and the button.
    private void DrawDivider(Layout layout)
    {
        Texture2D gem = InventoryPixelArt.GemTexture;
        float gemX = Mathf.Floor(-gem.width * 0.5f);
        float ruleY = layout.DividerTop + Mathf.Floor(gem.height * 0.5f) - 1f;
        float ruleLeft = -DividerHalfWidth;
        float ruleWidth = DividerHalfWidth * 2f;
        Fill(new Rect(ruleLeft - 1f, ruleY, ruleWidth + 2f, 3f), Outline);
        Fill(new Rect(ruleLeft, ruleY + 1f, ruleWidth, 1f), Gold);
        foreach (float end in new[] { ruleLeft - 3f, ruleLeft + ruleWidth })
        {
            Fill(new Rect(end, ruleY - 1f, 3f, 5f), Outline);
            Fill(new Rect(end + 1f, ruleY + 1f, 1f, 1f), GoldHighlight);
        }
        DrawSprite(gem, gemX, layout.DividerTop, 1, new Rect(0f, 0f, 1f, 1f));
    }

}
