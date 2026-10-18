using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// A patch of ground the player digs with E. Digging runs Dead by Daylight style skill checks
// on a bar near the bottom centre of the screen: a needle sweeps left to right and Space must
// land while it is inside the gold zone, which stays open 0.5, then 0.3, then 0.2 seconds.
// A miss shatters the bar and the dig starts over from the first check; clearing all of
// them raises the buried reward out of the ground.
// Runs before the player and pickups so the press that starts a dig moves and picks up nothing.
[DefaultExecutionOrder(-50)]
public class DigSite : MonoBehaviour, IInteractable
{
    public enum Phase { Idle, Appearing, Sweep, Hit, Failed, Shatter, Complete, Dug }

    public const float AppearDuration = 0.25f;
    // The needle rests at the left end this long before it starts to move.
    public const float LeadIn = 0.35f;
    // Time for the needle to cross the whole bar.
    public const float SweepDuration = 1.3f;
    public const float HitDuration = 0.4f;
    public const float FailDuration = 0.15f;
    public const float ShatterDuration = 0.9f;
    public const float CompleteDuration = 0.6f;
    public const float RiseDuration = 1.2f;
    // The zone never opens before this share of the sweep, nor closer than ZoneEndMargin to the end.
    private const float ZoneEarliest = 0.3f;
    private const float ZoneEndMargin = 0.08f;

    // Layout in UI art pixels (one art pixel = uiPixelSize screen pixels at 720p).
    private const int BarWidth = 130;
    private const int BarHeight = 13;
    private const int BarBottomInset = 30;
    private const int TrackLeft = 4;
    private const int TrackTop = 4;
    private const int TrackWidth = BarWidth - 7;
    private const int TrackHeight = BarHeight - 7;
    private const float ShardGravity = 320f;

    private static readonly Color ZoneFill = new Color32(212, 160, 42, 255);
    private static readonly Color ZoneLight = new Color32(242, 211, 107, 255);
    private static readonly Color ZoneFlash = new Color32(255, 244, 200, 255);
    private static readonly Color Needle = new Color32(196, 52, 40, 255);
    private static readonly Color Outline = new Color32(26, 15, 10, 230);
    private static readonly Color PipEmpty = new Color32(59, 36, 22, 255);
    private static readonly Color Parchment = new Color32(233, 214, 168, 255);
    private static readonly Color FailTint = new Color(1f, 0.55f, 0.5f, 1f);
    private static readonly Color GoldTint = new Color(1f, 0.9f, 0.45f, 1f);
    private const string SkillHint = "[SPACE]";

    public static bool IsDigging { get; private set; }

    // 30% under the 2.5 of the pickups and the gravestone.
    [SerializeField]
    private float digRadius = 1.75f;

    [SerializeField]
    private float hintOffsetY = 10f;

    [SerializeField]
    private string hintText = "E";

    [SerializeField]
    private string actionText = "Dig";

    [SerializeField]
    private int promptPixelSize = 3;

    [Header("Skill checks")]
    // Seconds the needle spends inside the zone, one entry per check.
    [SerializeField]
    private float[] windows = { 0.5f, 0.3f, 0.2f };

    [SerializeField]
    private int uiPixelSize = 3;

    // Buried until every check is cleared, then rises out of the ground to be picked up.
    [Header("Reward")]
    [SerializeField]
    private PickupItem reward;

    // Faint lit glints over the site, shown once the player has finished talking to this
    // object (the gravestone). Left empty, the scene's talkable object is used.
    [Header("Sparkles")]
    [SerializeField]
    private TalkInteractable revealedBy;

    [SerializeField]
    private Color sparkleColor = new Color(1f, 0.86f, 0.5f);

    [SerializeField]
    private float sparkleLightIntensity = 1.5f;

    [SerializeField]
    private float sparkleLightRange = 3f;

    private struct Shard
    {
        public RectInt source;
        public Vector2 velocity;
    }

    private readonly List<Shard> shards = new List<Shard>();
    private Transform player;
    private Vector3 anchor;
    private float promptAlpha;
    private bool inputGuard;
    private float stoppedNeedle;
    private GUIStyle barStyle;
    private DigSparkles sparkles;

    public Phase CurrentPhase { get; private set; }
    public float PhaseTime { get; private set; }
    // Index of the current check, from 0.
    public int Round { get; private set; }
    public int RoundCount => windows != null ? windows.Length : 0;
    public float Window => RoundCount > 0 ? windows[Mathf.Clamp(Round, 0, RoundCount - 1)] : 0f;
    // Needle time (seconds since it started moving) at which the zone opens.
    public float ZoneStart { get; private set; }
    public float NeedleTime => CurrentPhase == Phase.Sweep ? Mathf.Max(0f, PhaseTime - LeadIn)
        : CurrentPhase == Phase.Appearing ? 0f
        : stoppedNeedle;
    public bool IsDug => CurrentPhase == Phase.Dug;

    private void Awake()
    {
        player = Interactables.FindPlayer();
        anchor = Interactables.TopCentre(gameObject);
    }

    private void Start()
    {
        // After every Awake, so the reward has already recorded its resting place.
        if (reward != null)
        {
            reward.Bury();
        }

        if (revealedBy == null)
        {
            revealedBy = FindAnyObjectByType<TalkInteractable>();
        }
        // Not parented to the site: its uneven scale would stretch the glints.
        var sparkleObject = new GameObject(name + " Sparkles");
        sparkles = sparkleObject.AddComponent<DigSparkles>();
        float sparkleRadius = Mathf.Min(digRadius * 0.5f, SiteHalfWidth());
        sparkles.Configure(anchor, sparkleRadius, sparkleColor, sparkleLightIntensity, sparkleLightRange);
    }

    private void OnDestroy()
    {
        if (sparkles != null)
        {
            Destroy(sparkles.gameObject);
        }
    }

    // Half the smaller horizontal size of what the site renders.
    private float SiteHalfWidth()
    {
        Renderer siteRenderer = GetComponentInChildren<Renderer>();
        if (siteRenderer == null)
        {
            return 0.5f;
        }
        Vector3 extents = siteRenderer.bounds.extents;
        return Mathf.Max(0.2f, Mathf.Min(extents.x, extents.z));
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

    // Drops an unfinished dig without breaking the bar and frees the player.
    public void Cancel()
    {
        if (CurrentPhase == Phase.Idle || CurrentPhase == Phase.Dug)
        {
            return;
        }
        if (CurrentPhase == Phase.Complete)
        {
            // The reward is already on its way up; the site stays dug.
            SetPhase(Phase.Dug);
            return;
        }
        if (CurrentPhase == Phase.Appearing || CurrentPhase == Phase.Sweep || CurrentPhase == Phase.Hit)
        {
            IsDigging = false;
        }
        shards.Clear();
        Round = 0;
        SetPhase(Phase.Idle);
    }

    private void Update()
    {
        if (sparkles != null)
        {
            // They fade away as the reward comes up.
            bool finished = CurrentPhase == Phase.Complete || CurrentPhase == Phase.Dug;
            sparkles.Visible = !finished && revealedBy != null && revealedBy.HasSpoken;
        }

        if (PauseMenu.IsPaused)
        {
            // The press that resumes the game must not also count as a skill check.
            inputGuard = true;
            return;
        }

        if (CurrentPhase != Phase.Idle && CurrentPhase != Phase.Dug)
        {
            bool pressed = !inputGuard && SkillPressed();
            inputGuard = false;
            promptAlpha = 0f;
            Tick(Time.unscaledDeltaTime, pressed);
            return;
        }
        inputGuard = false;

        bool blocked = IsDug || Interactables.Busy || InventoryInspection.IsOpen || TitleScreen.IsOpen;
        bool active = !blocked && Interactables.IsNearest(this);
        promptAlpha = Mathf.MoveTowards(promptAlpha, active ? 1f : 0f, Time.unscaledDeltaTime * 8f);

        Keyboard keyboard = Keyboard.current;
        if (active && keyboard != null && keyboard.eKey.wasPressedThisFrame)
        {
            Begin();
        }
    }

    // Starts the dig from the first check.
    public void Begin()
    {
        if (CurrentPhase != Phase.Idle || IsDigging || RoundCount == 0)
        {
            return;
        }
        Round = 0;
        stoppedNeedle = 0f;
        shards.Clear();
        IsDigging = true;
        SetPhase(Phase.Appearing);
    }

    // Advances the dig. pressed = the skill check key went down this frame.
    public void Tick(float deltaTime, bool pressed)
    {
        if (CurrentPhase == Phase.Idle || CurrentPhase == Phase.Dug)
        {
            return;
        }

        PhaseTime += deltaTime;
        switch (CurrentPhase)
        {
            case Phase.Appearing:
                if (PhaseTime >= AppearDuration) StartRound();
                break;
            case Phase.Sweep:
                float needle = PhaseTime - LeadIn;
                if (pressed)
                {
                    stoppedNeedle = Mathf.Max(0f, needle);
                    if (needle >= ZoneStart && needle <= ZoneStart + Window) Succeed();
                    else Fail();
                }
                else if (needle > ZoneStart + Window)
                {
                    // Letting the needle run past the zone is a miss too.
                    stoppedNeedle = ZoneStart + Window;
                    Fail();
                }
                break;
            case Phase.Hit:
                if (PhaseTime >= HitDuration)
                {
                    Round += 1;
                    StartRound();
                }
                break;
            case Phase.Failed:
                if (PhaseTime >= FailDuration)
                {
                    SpawnShards();
                    SetPhase(Phase.Shatter);
                }
                break;
            case Phase.Shatter:
                if (PhaseTime >= ShatterDuration)
                {
                    shards.Clear();
                    Round = 0;
                    SetPhase(Phase.Idle);
                }
                break;
            case Phase.Complete:
                if (PhaseTime >= CompleteDuration) SetPhase(Phase.Dug);
                break;
        }
    }

    private void StartRound()
    {
        float latest = Mathf.Max(0f, SweepDuration - Window - ZoneEndMargin);
        ZoneStart = Random.Range(Mathf.Min(SweepDuration * ZoneEarliest, latest), latest);
        SetPhase(Phase.Sweep);
    }

    private void Succeed()
    {
        if (Round + 1 < RoundCount)
        {
            SetPhase(Phase.Hit);
            return;
        }
        IsDigging = false;
        SetPhase(Phase.Complete);
        if (reward != null)
        {
            reward.Unearth(RiseDuration);
        }
    }

    // The player is free the moment the check is missed; the dig site answers E again once
    // the shards are gone.
    private void Fail()
    {
        IsDigging = false;
        SetPhase(Phase.Failed);
    }

    private void SetPhase(Phase next)
    {
        CurrentPhase = next;
        PhaseTime = 0f;
    }

    private static bool SkillPressed()
    {
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard.spaceKey.wasPressedThisFrame;
    }

    public float InteractDistanceSqr()
    {
        if (CurrentPhase != Phase.Idle || player == null || !isActiveAndEnabled)
        {
            return float.MaxValue;
        }
        Vector3 delta = player.position - anchor;
        float distance = delta.x * delta.x + delta.z * delta.z;
        return distance <= digRadius * digRadius ? distance : float.MaxValue;
    }

    // Breaks the bar into jagged two-row columns that burst outward from the middle and fall.
    private void SpawnShards()
    {
        shards.Clear();
        int x = 0;
        while (x < BarWidth)
        {
            int width = Mathf.Min(BarWidth - x, Random.Range(9, 17));
            if (BarWidth - x - width < 6)
            {
                width = BarWidth - x;
            }
            // Shards reach 2 px past the bar so the needle's overhang breaks with it.
            int split = Random.Range(4, BarHeight - 3);
            AddShard(new RectInt(x - 1, -2, width + 1, split + 2), true);
            AddShard(new RectInt(x - 1, split, width + 1, BarHeight + 2 - split), false);
            x += width;
        }
    }

    private void AddShard(RectInt source, bool top)
    {
        float side = (source.center.x - BarWidth * 0.5f) / (BarWidth * 0.5f);
        var velocity = new Vector2(
            side * Random.Range(30f, 70f) + Random.Range(-12f, 12f),
            -Random.Range(40f, 90f) * (top ? 1.2f : 0.7f));
        shards.Add(new Shard { source = source, velocity = velocity });
    }

    private void OnGUI()
    {
        if (Event.current.type != EventType.Repaint)
        {
            return;
        }

        if (CurrentPhase == Phase.Idle)
        {
            InteractPrompt.Draw(anchor, hintOffsetY, hintText, actionText, promptPixelSize, promptAlpha);
            return;
        }
        if (CurrentPhase == Phase.Dug)
        {
            return;
        }

        if (barStyle == null || barStyle.normal.background == null)
        {
            barStyle = InventoryPixelArt.NineSlice(InventoryPixelArt.SkillBarTexture, InventoryPixelArt.SkillBarBorder);
        }

        // Work in whole art pixels so every edge lands on the UI pixel grid.
        int p = Mathf.Max(1, Mathf.RoundToInt(uiPixelSize * InteractPrompt.UIScale()));
        int width = Mathf.CeilToInt(Screen.width / (float)p);
        int height = Mathf.CeilToInt(Screen.height / (float)p);
        Matrix4x4 previousMatrix = GUI.matrix;
        Color previousColor = GUI.color;
        GUI.matrix = Matrix4x4.Scale(new Vector3(p, p, 1f));

        int x = (width - BarWidth) / 2;
        int y = height - BarBottomInset - BarHeight;
        if (CurrentPhase == Phase.Shatter)
        {
            DrawShards(x, y);
        }
        else
        {
            DrawCheck(x, y);
        }

        GUI.color = previousColor;
        GUI.matrix = previousMatrix;
    }

    private void DrawCheck(int x, int y)
    {
        float alpha = 1f;
        Color tint = Color.white;
        bool showHint = true;
        switch (CurrentPhase)
        {
            case Phase.Appearing:
                float t = Mathf.Clamp01(PhaseTime / AppearDuration);
                alpha = t * t * (3f - 2f * t);
                y += Mathf.RoundToInt((1f - t) * 4f);
                break;
            case Phase.Failed:
                tint = FailTint;
                x += Mathf.FloorToInt(PhaseTime / 0.03f) % 2 == 0 ? 1 : -1;
                showHint = false;
                break;
            case Phase.Complete:
                float fade = 1f - Mathf.Clamp01(PhaseTime / CompleteDuration);
                alpha = fade * fade * (3f - 2f * fade);
                tint = Color.Lerp(GoldTint, Color.white, PhaseTime / 0.2f);
                showHint = false;
                break;
        }

        DrawBar(x, y, alpha, tint);
        DrawPips(x, y, alpha);
        if (showHint)
        {
            int hintX = x + (BarWidth - PixelFont.Measure(SkillHint)) / 2;
            PixelFont.Draw(hintX, y + BarHeight + 5, SkillHint, Fade(ZoneLight, alpha), Fade(Outline, alpha));
        }
    }

    // Frame, zone and needle with the bar's top-left at (x, y).
    private void DrawBar(int x, int y, float alpha, Color tint)
    {
        GUI.color = Fade(tint, alpha);
        barStyle.Draw(new Rect(x, y, BarWidth, BarHeight), false, false, false, false);

        int trackX = x + TrackLeft;
        int trackY = y + TrackTop;
        int zoneX = trackX + Mathf.RoundToInt(ZoneStart / SweepDuration * TrackWidth);
        int zoneWidth = Mathf.Clamp(Mathf.RoundToInt(Window / SweepDuration * TrackWidth), 2, trackX + TrackWidth - zoneX);
        bool flash = (CurrentPhase == Phase.Hit || CurrentPhase == Phase.Complete) && Mathf.FloorToInt(PhaseTime / 0.08f) % 2 == 0;
        Fill(zoneX, trackY, zoneWidth, TrackHeight, (flash ? ZoneFlash : ZoneFill) * tint, alpha);
        Fill(zoneX, trackY, zoneWidth, 1, ZoneLight * tint, alpha);
        Fill(zoneX, trackY, 1, TrackHeight, ZoneLight * tint, alpha);
        Fill(zoneX + zoneWidth - 1, trackY, 1, TrackHeight, ZoneLight * tint, alpha);

        float progress = Mathf.Clamp01(NeedleTime / SweepDuration);
        int needleX = trackX + Mathf.RoundToInt(progress * (TrackWidth - 1));
        Fill(needleX - 1, y - 2, 3, BarHeight + 4, Outline, alpha);
        Fill(needleX, y - 1, 1, BarHeight + 2, Needle * tint, alpha);
    }

    // One pip per check above the bar: gold once cleared, blinking parchment for the current one.
    private void DrawPips(int x, int y, float alpha)
    {
        int cleared = Round + (CurrentPhase == Phase.Hit || CurrentPhase == Phase.Complete ? 1 : 0);
        const int size = 5;
        const int gap = 2;
        int total = RoundCount * size + (RoundCount - 1) * gap;
        int pipX = x + (BarWidth - total) / 2;
        int pipY = y - size - 4;
        bool blink = Mathf.FloorToInt(Time.unscaledTime / 0.25f) % 2 == 0;
        for (int i = 0; i < RoundCount; i++)
        {
            Color fill = i < cleared ? ZoneLight : i == cleared && CurrentPhase == Phase.Sweep && blink ? Parchment : PipEmpty;
            Fill(pipX, pipY, size, size, Outline, alpha);
            Fill(pipX + 1, pipY + 1, size - 2, size - 2, fill, alpha);
            pipX += size + gap;
        }
    }

    private void DrawShards(int x, int y)
    {
        float t = PhaseTime;
        float half = ShatterDuration * 0.5f;
        float alpha = t < half ? 1f : 1f - (t - half) / half;
        // Fade in quarter steps, like the rest of the pixel art.
        alpha = Mathf.Ceil(alpha * 4f) / 4f;
        if (alpha <= 0f)
        {
            return;
        }

        foreach (Shard shard in shards)
        {
            Vector2 offset = shard.velocity * t + new Vector2(0f, 0.5f * ShardGravity * t * t);
            RectInt source = shard.source;
            var piece = new Rect(x + source.x + Mathf.Round(offset.x), y + source.y + Mathf.Round(offset.y), source.width, source.height);
            // The group clips the whole bar down to this shard's piece of it.
            GUI.BeginGroup(piece);
            DrawBar(-source.x, -source.y, alpha, FailTint);
            GUI.EndGroup();
        }
    }

    private static void Fill(int x, int y, int width, int height, Color color, float alpha)
    {
        GUI.color = Fade(color, alpha);
        GUI.DrawTexture(new Rect(x, y, width, height), Texture2D.whiteTexture);
    }

    private static Color Fade(Color color, float alpha)
    {
        return new Color(color.r, color.g, color.b, color.a * alpha);
    }
}
