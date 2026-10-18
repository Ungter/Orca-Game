using UnityEngine;
using UnityEngine.InputSystem;

// A world object the player can talk to with E (e.g. a gravestone). Talking slides a
// portrait into the bottom-right corner, pops a pixel-art speech bubble from it and
// types out the line with an Enter keycap hint underneath. Enter or a click sends the
// portrait back out of frame while the bubble fades. The player cannot move until
// the portrait has left.
// Runs before the player and pickups so the press that starts a talk moves and picks up nothing.
[DefaultExecutionOrder(-50)]
public class TalkInteractable : MonoBehaviour, IInteractable
{
    public enum Phase { Idle, Entering, Popping, Typing, Waiting, Leaving }

    public const float EnterDuration = 0.45f;
    public const float PopDuration = 0.18f;
    public const float LeaveDuration = 0.4f;
    private const float HintFadeDuration = 0.15f;
    private const float PortraitFrameTime = 0.5f;
    private const float HintBobTime = 0.4f;

    // Layout in UI art pixels (one art pixel = uiPixelSize screen pixels at 720p).
    private const int PortraitSize = 64;
    private const int PortraitRightInset = 2;
    private const int PortraitBottomInset = 6;
    private const int MaxTextWidth = 150;
    private const int PadX = 7;
    private const int PadTop = 6;
    private const int PadBottom = 5;
    private const int HintGap = 3;
    private const int BubbleReachRight = 12;
    private const int ScreenMargin = 2;

    private static readonly Color Ink = new Color32(59, 36, 22, 255);

    public static bool IsTalking { get; private set; }

    // True once a conversation has played to the end at least once.
    public bool HasSpoken { get; private set; }

    [SerializeField]
    private float talkRadius = 2.5f;

    [SerializeField]
    private float hintOffsetY = 10f;

    [SerializeField]
    private string hintText = "E";

    [SerializeField]
    private string actionText = "Talk";

    [SerializeField]
    private int promptPixelSize = 3;

    [Header("Conversation")]
    [SerializeField, TextArea]
    private string line = "Something is in the forest";

    // Two square frames stacked vertically under Resources; they alternate as an idle.
    [SerializeField]
    private string portraitResource = "ShellGame/panther";

    // The panther faces right in the sheet; mirrored it looks into the frame.
    [SerializeField]
    private bool mirrorPortrait = true;

    // Where the bubble's tail points, in art pixels from the drawn portrait's top-left.
    [SerializeField]
    private Vector2Int speechPoint = new Vector2Int(20, 15);

    [SerializeField]
    private float charactersPerSecond = 40f;

    [SerializeField]
    private int uiPixelSize = 3;

    private Transform player;
    private Vector3 anchorOffset;
    private float promptAlpha;
    private bool inputGuard;
    private Texture2D portrait;
    private GUIStyle bubbleStyle;
    private string wrappedLine;

    public Phase CurrentPhase { get; private set; }
    public float PhaseTime { get; private set; }
    public int VisibleCharacters { get; private set; }
    private float typed;

    public string Text => wrappedLine ?? (wrappedLine = string.Join("\n", PixelFont.Wrap(line ?? "", MaxTextWidth)));

    private void Awake()
    {
        player = Interactables.FindPlayer();
        anchorOffset = Interactables.TopCentre(gameObject) - transform.position;
    }

    private void OnEnable()
    {
        Interactables.Add(this);
    }

    private void OnDisable()
    {
        Interactables.Remove(this);
        Cancel();
    }

    private void OnValidate()
    {
        wrappedLine = null;
    }

    private void Update()
    {
        if (PauseMenu.IsPaused)
        {
            // The click that resumes the game must not also dismiss the bubble.
            inputGuard = true;
            return;
        }

        if (CurrentPhase != Phase.Idle)
        {
            bool advance = !inputGuard && AdvancePressed();
            inputGuard = false;
            promptAlpha = 0f;
            Tick(Time.unscaledDeltaTime, advance);
            return;
        }
        inputGuard = false;

        bool blocked = Interactables.Busy || InventoryInspection.IsOpen || TitleScreen.IsOpen;
        bool active = !blocked && Interactables.IsNearest(this);
        promptAlpha = Mathf.MoveTowards(promptAlpha, active ? 1f : 0f, Time.unscaledDeltaTime * 8f);

        Keyboard keyboard = Keyboard.current;
        if (active && keyboard != null && keyboard.eKey.wasPressedThisFrame)
        {
            Begin();
        }
    }

    public void Begin()
    {
        if (IsTalking)
        {
            return;
        }
        IsTalking = true;
        typed = 0f;
        VisibleCharacters = 0;
        SetPhase(Phase.Entering);
    }

    // Advances the conversation. advance = Enter or a click this frame.
    public void Tick(float deltaTime, bool advance)
    {
        if (CurrentPhase == Phase.Idle)
        {
            return;
        }

        PhaseTime += deltaTime;
        switch (CurrentPhase)
        {
            case Phase.Entering:
                if (PhaseTime >= EnterDuration) SetPhase(Phase.Popping);
                break;
            case Phase.Popping:
                if (PhaseTime >= PopDuration) SetPhase(Phase.Typing);
                break;
            case Phase.Typing:
                // The first press finishes the line; it takes another to close it.
                typed = advance ? Text.Length : Mathf.Min(Text.Length, typed + deltaTime * charactersPerSecond);
                VisibleCharacters = Mathf.FloorToInt(typed);
                if (VisibleCharacters >= Text.Length) SetPhase(Phase.Waiting);
                break;
            case Phase.Waiting:
                if (advance) SetPhase(Phase.Leaving);
                break;
            case Phase.Leaving:
                if (PhaseTime >= LeaveDuration)
                {
                    HasSpoken = true;
                    Cancel();
                }
                break;
        }
    }

    // Ends the conversation immediately and frees the player.
    public void Cancel()
    {
        if (CurrentPhase == Phase.Idle)
        {
            return;
        }
        SetPhase(Phase.Idle);
        IsTalking = false;
    }

    private void SetPhase(Phase next)
    {
        CurrentPhase = next;
        PhaseTime = 0f;
    }

    private static bool AdvancePressed()
    {
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        return (keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame))
            || (mouse != null && mouse.leftButton.wasPressedThisFrame);
    }

    public float InteractDistanceSqr()
    {
        if (player == null || !isActiveAndEnabled)
        {
            return float.MaxValue;
        }
        Vector3 delta = player.position - (transform.position + anchorOffset);
        float distance = delta.x * delta.x + delta.z * delta.z;
        return distance <= talkRadius * talkRadius ? distance : float.MaxValue;
    }

    private void OnGUI()
    {
        if (Event.current.type != EventType.Repaint)
        {
            return;
        }

        if (CurrentPhase == Phase.Idle)
        {
            InteractPrompt.Draw(transform.position + anchorOffset, hintOffsetY, hintText, actionText, promptPixelSize, promptAlpha);
            return;
        }

        DrawConversation();
    }

    private void DrawConversation()
    {
        if (portrait == null)
        {
            portrait = Resources.Load<Texture2D>(portraitResource);
            if (portrait == null)
            {
                return;
            }
            portrait.filterMode = FilterMode.Point;
        }
        if (bubbleStyle == null || bubbleStyle.normal.background == null)
        {
            bubbleStyle = InventoryPixelArt.NineSlice(InventoryPixelArt.SpeechBubbleTexture, InventoryPixelArt.SpeechBubbleBorder);
        }

        // Work in whole art pixels so every edge lands on the UI pixel grid.
        int p = Mathf.Max(1, Mathf.RoundToInt(uiPixelSize * InteractPrompt.UIScale()));
        int width = Mathf.CeilToInt(Screen.width / (float)p);
        int height = Mathf.CeilToInt(Screen.height / (float)p);
        Matrix4x4 previousMatrix = GUI.matrix;
        Color previousColor = GUI.color;
        GUI.matrix = Matrix4x4.Scale(new Vector3(p, p, 1f));

        int restX = width - PortraitSize + PortraitRightInset;
        int outX = width + 2;
        int portraitY = height - PortraitSize + PortraitBottomInset;
        float slide = CurrentPhase == Phase.Entering ? 1f - EaseOut(PhaseTime / EnterDuration)
            : CurrentPhase == Phase.Leaving ? EaseIn(PhaseTime / LeaveDuration)
            : 0f;
        int portraitX = Mathf.RoundToInt(Mathf.Lerp(restX, outX, slide));

        int frame = Mathf.FloorToInt(Time.unscaledTime / PortraitFrameTime) % 2;
        var uv = new Rect(mirrorPortrait ? 1f : 0f, frame == 0 ? 0.5f : 0f, mirrorPortrait ? -1f : 1f, 0.5f);
        GUI.DrawTextureWithTexCoords(new Rect(portraitX, portraitY, PortraitSize, PortraitSize), portrait, uv);

        if (CurrentPhase != Phase.Entering)
        {
            DrawBubble(restX + speechPoint.x, portraitY + speechPoint.y, width);
        }

        GUI.color = previousColor;
        GUI.matrix = previousMatrix;
    }

    // The bubble stays where it popped (anchored on the resting portrait) and fades
    // in place while the portrait leaves.
    private void DrawBubble(int tipX, int tipY, int screenWidth)
    {
        float alpha = 1f;
        int rise = 0;
        if (CurrentPhase == Phase.Popping)
        {
            alpha = Mathf.Clamp01(PhaseTime / PopDuration);
            rise = Mathf.RoundToInt((1f - alpha) * 3f);
        }
        else if (CurrentPhase == Phase.Leaving)
        {
            alpha = 1f - Mathf.Clamp01(PhaseTime / LeaveDuration);
        }
        alpha = alpha * alpha * (3f - 2f * alpha);
        if (alpha <= 0f)
        {
            return;
        }

        Texture2D tail = InventoryPixelArt.SpeechTailTexture;
        Texture2D key = InventoryPixelArt.EnterKeyTexture;
        string text = Text;
        int textWidth = PixelFont.Measure(text);
        int bubbleWidth = Mathf.Max(textWidth, key.width) + PadX * 2;
        int bubbleHeight = PadTop + PixelFont.MeasureHeight(text) + HintGap + key.height + PadBottom;

        int right = Mathf.Min(tipX + BubbleReachRight, screenWidth - ScreenMargin);
        int x = Mathf.Max(ScreenMargin, right - bubbleWidth);
        // The tail's first row overlaps the bubble's bottom outline; its tip sits on tipY.
        int y = tipY - tail.height + 2 - bubbleHeight + rise;
        var bubble = new Rect(x, y, bubbleWidth, bubbleHeight);
        var tailRect = new Rect(tipX - 6, y + bubbleHeight - 1, tail.width, tail.height);

        GUI.color = new Color(0f, 0f, 0f, 0.35f * alpha);
        bubbleStyle.Draw(new Rect(bubble.x + 1, bubble.y + 1, bubble.width, bubble.height), false, false, false, false);
        GUI.DrawTexture(new Rect(tailRect.x + 1, tailRect.y + 1, tailRect.width, tailRect.height), tail);

        GUI.color = new Color(1f, 1f, 1f, alpha);
        bubbleStyle.Draw(bubble, false, false, false, false);
        GUI.DrawTexture(tailRect, tail);
        PixelFont.Draw(x + PadX, y + PadTop, text.Substring(0, Mathf.Clamp(VisibleCharacters, 0, text.Length)), Ink);

        float hintAlpha = CurrentPhase == Phase.Waiting ? Mathf.Clamp01(PhaseTime / HintFadeDuration)
            : CurrentPhase == Phase.Leaving ? 1f
            : 0f;
        if (hintAlpha > 0f)
        {
            int bob = Mathf.FloorToInt(Time.unscaledTime / HintBobTime) % 2;
            GUI.color = new Color(1f, 1f, 1f, alpha * hintAlpha);
            GUI.DrawTexture(new Rect(x + bubbleWidth - PadX - key.width, y + bubbleHeight - PadBottom - key.height + bob, key.width, key.height), key);
        }
    }

    private static float EaseOut(float t)
    {
        t = 1f - Mathf.Clamp01(t);
        return 1f - t * t * t;
    }

    private static float EaseIn(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * t;
    }
}
