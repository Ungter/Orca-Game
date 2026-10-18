using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// Title screen shown when the game starts. The camera circles high above the island while a
// parchment "ORCA PLACEHOLDER" scroll hangs on chains between two wall torches, the orca leaps
// out from behind it and wooden PLAY and EXIT buttons (the pause menu's buttons) wait below.
// PLAY flies the camera down into the follow camera's view and hands control to the player;
// EXIT quits the game. It opens on every load of a gameplay scene, including the pause menu's HOME.
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

    private const float FadeInDuration = 1.2f;
    private const float UIFadeDuration = 0.4f;
    private const int MaxTitleScale = 3;
    private const int TorchScale = 2;
    private const int CornerScale = 2;
    private const int CornerMargin = 3;
    private const int EmbersPerTorch = 5;

    // The orca sheet is two 64x64 frames stacked vertically, drawn 4 texels per art pixel.
    // Content spans rows 8-56 of a frame and is centred 27 pixels from its left edge.
    private const int OrcaFrameSize = 64;
    private const int OrcaContentTop = 8;
    private const int OrcaContentBottom = 56;
    private const int OrcaContentCentre = 27;
    // The tail tucks this far behind the top of the scroll.
    private const int OrcaOverlap = 5;
    private const float OrcaFrameRate = 3f;

    // Layout in art pixels below the scroll.
    private const int SectionGap = 6;
    private const int DividerHalfWidth = 40;
    private const int ButtonGap = 3;

    private static readonly Color Ink = new Color32(59, 36, 22, 255);
    private static readonly Color InkShadow = new Color32(200, 168, 114, 140);
    private static readonly Color Gold = new Color32(212, 160, 42, 255);
    private static readonly Color Outline = new Color32(26, 15, 10, 220);
    private static readonly Color EmberHot = new Color32(255, 206, 84, 255);
    private static readonly Color EmberWarm = new Color32(234, 118, 36, 255);
    private static readonly Color EmberCool = new Color32(168, 44, 22, 255);

    private readonly List<Behaviour> frozen = new List<Behaviour>();
    private readonly List<Behaviour> cameraRigs = new List<Behaviour>();
    private Camera cam;
    private Texture2D orca;
    private Texture2D glow;
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
        glow = BuildGlow();

        FrameIsland();
        orbitYaw = cam.transform.eulerAngles.y;
        originalFarClip = cam.farClipPlane;
        cam.farClipPlane = Mathf.Max(originalFarClip, orbitDistance * 3f);
        cam.transform.SetPositionAndRotation(TitlePosition(), TitleRotation());
    }

    private void OnDestroy()
    {
        IsOpen = false;
        if (cam != null)
        {
            cam.farClipPlane = originalFarClip;
        }
        if (glow != null)
        {
            Destroy(glow);
        }
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
            if (flightTime >= flightDuration)
            {
                Finish();
            }
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
        if (!starting)
        {
            cam.transform.SetPositionAndRotation(TitlePosition(), TitleRotation());
            return;
        }

        // CameraFollow has already placed the camera for this frame; ease from the title view into it.
        float t = Mathf.Clamp01(flightTime / flightDuration);
        t = t * t * (3f - 2f * t);
        Transform view = cam.transform;
        view.SetPositionAndRotation(
            Vector3.Lerp(flightStartPosition, view.position, t),
            Quaternion.Slerp(flightStartRotation, view.rotation, t));
    }

    private void Begin()
    {
        starting = true;
        hoveredButton = -1;
        pressedButton = -1;
        flightTime = 0f;
        flightStartPosition = cam.transform.position;
        flightStartRotation = cam.transform.rotation;
        Thaw(cameraRigs);
    }

    private void Finish()
    {
        Thaw(cameraRigs);
        Thaw(frozen);
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

        // The scroll is as large a whole-number multiple as fits, leaving room for the torches.
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

            DrawShade(layout);
            DrawCorners(layout);
            DrawChains(layout);
            DrawTorch(layout, layout.BannerX - 6f - InventoryPixelArt.Torch[0].Length * TorchScale, time, 0);
            DrawTorch(layout, layout.BannerX + layout.BannerWidth + 6f, time, 1);
            DrawOrca(layout, time);
            DrawBanner(layout, artMatrix);
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

    // Iron chains from the top of the screen down behind the scroll's rolled ends.
    private void DrawChains(Layout layout)
    {
        Texture2D link = InventoryPixelArt.ChainLinkTexture;
        float rollCentre = 2.5f * layout.TitleScale;
        float bottom = layout.BannerTop + 3f * layout.TitleScale;
        int links = Mathf.CeilToInt(bottom / link.height);
        var full = new Rect(0f, 0f, 1f, 1f);
        foreach (float centre in new[] { layout.BannerX + rollCentre, layout.BannerX + layout.BannerWidth - rollCentre })
        {
            float x = Mathf.Floor(centre - link.width * 0.5f);
            for (int i = 0; i < links; i++)
            {
                DrawSprite(link, x, bottom - (i + 1) * link.height, 1, full);
            }
        }
    }

    // A wall torch level with the scroll: flickering glow, animated flame, rising embers.
    private void DrawTorch(Layout layout, float x, float time, int index)
    {
        Texture2D body = InventoryPixelArt.TorchTexture;
        Texture2D flame = InventoryPixelArt.TorchFlameTexture(Mathf.FloorToInt(time * 8f) + index);
        float flameHeight = flame.height * TorchScale;
        // The flame's bottom row sits behind the cresset rim.
        float totalHeight = flameHeight + body.height * TorchScale - TorchScale;
        float flameTop = Mathf.Round(layout.BannerTop + (layout.BannerHeight - totalHeight) * 0.5f);
        float bodyTop = flameTop + flameHeight - TorchScale;
        float centreX = x + body.width * TorchScale * 0.5f;
        var full = new Rect(0f, 0f, 1f, 1f);

        float flicker = 0.8f + 0.2f * Mathf.PerlinNoise(time * 3f, index * 7.3f);
        float glowSize = glow.width * TorchScale;
        DrawSprite(glow, Mathf.Round(centreX - glowSize * 0.5f), Mathf.Round(flameTop + flameHeight * 0.5f - glowSize * 0.5f), TorchScale, full, flicker);

        for (int i = 0; i < EmbersPerTorch; i++)
        {
            float seed = index * 0.37f + i * 1.91f;
            float life = Mathf.Repeat(time * 0.6f + i / (float)EmbersPerTorch + seed, 1f);
            float drift = Mathf.Round(Mathf.Sin(time * 2.1f + seed * 5f) * 2f + (i - EmbersPerTorch / 2));
            Color ember = life < 0.3f ? EmberHot : life < 0.65f ? EmberWarm : EmberCool;
            ember.a = 1f - life;
            Fill(new Rect(Mathf.Floor(centreX) + drift, flameTop + 4f - Mathf.Round(life * 28f), 1f, 1f), ember);
        }

        DrawSprite(flame, x, flameTop, TorchScale, full);
        DrawSprite(body, x, bodyTop, TorchScale, full);
    }

    // The orca leaps out from behind the scroll, bobbing a pixel and flicking between its two frames.
    private void DrawOrca(Layout layout, float time)
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
        GUI.DrawTextureWithTexCoords(new Rect(-OrcaContentCentre, y, OrcaFrameSize, OrcaFrameSize), orca, uv);
    }

    // The pause menu's parchment banner, blown up to a whole-number multiple for the title.
    private void DrawBanner(Layout layout, Matrix4x4 artMatrix)
    {
        if (bannerStyle == null || bannerStyle.normal.background == null)
        {
            bannerStyle = InventoryPixelArt.NineSlice(InventoryPixelArt.BannerTexture, InventoryPixelArt.BannerBorder);
        }

        int scale = layout.TitleScale;
        GUI.matrix = artMatrix * Matrix4x4.TRS(new Vector3(layout.BannerX, layout.BannerTop, 0f), Quaternion.identity, new Vector3(scale, scale, 1f));
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
            Fill(new Rect(end + 1f, ruleY + 1f, 1f, 1f), EmberHot);
        }
        DrawSprite(gem, gemX, layout.DividerTop, 1, new Rect(0f, 0f, 1f, 1f));
    }

    // Stepped rings of warm light behind each torch flame.
    private static Texture2D BuildGlow()
    {
        const int Size = 25;
        var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
        {
            name = "title_torch_glow",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave,
        };
        var pixels = new Color32[Size * Size];
        float centre = (Size - 1) * 0.5f;
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), new Vector2(centre, centre));
                byte alpha = distance <= 5f ? (byte)60 : distance <= 8.5f ? (byte)36 : distance <= 12f ? (byte)18 : (byte)0;
                pixels[y * Size + x] = new Color32(255, 170, 70, alpha);
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }
}
