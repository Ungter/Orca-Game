using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.UI;

// Burns a UI "letter" away and rebuilds it, as a hand-tuned pixel effect.
//
// Play() flattens everything drawn on the letter surface (the paper sprite, raw
// images, plain images, PixelText) into one bitmap at the paper sprite's own
// resolution, hides the real UI and shows that bitmap instead. The bitmap is
// animated on the CPU in 2x2-pixel cells:
//   burn    - a ragged flame front spreads from two points; each cell scorches,
//             flares yellow > orange > red > char, then is gone, shedding embers.
//   pause   - only embers drift up.
//   rebuild - the midpoint callback changes the game state, the surface is
//             flattened again, and ash specks gather while the paper re-forms
//             from the edges inward behind a warm glowing seam.
// Then the real UI is shown again.
public class LetterBurn
{
    private const int Cell = 2;
    private const float BurnDuration = 1.1f;
    private const float PauseDuration = 0.3f;
    private const float RebuildDuration = 1.0f;
    // Share of the timeline each cell spends burning or re-forming.
    private const float Band = 0.16f;
    // Share of the timeline a cell scorches before it catches.
    private const float ScorchLead = 0.07f;
    private const float EmberChance = 0.03f;
    private const int MaxEmbers = 500;
    private const int AshCount = 380;

    private static readonly Color32[] FireRamp =
    {
        new Color32(255, 244, 190, 255),
        new Color32(255, 196, 70, 255),
        new Color32(246, 120, 28, 255),
        new Color32(190, 52, 18, 255),
        new Color32(88, 24, 12, 255),
        new Color32(28, 16, 12, 255),
    };
    private static readonly Color32 Scorch = new Color32(74, 42, 18, 255);
    private static readonly Color32 Seam = new Color32(255, 170, 80, 255);
    private static readonly Color32 Ash = new Color32(92, 86, 80, 255);

    private struct Ember
    {
        public Vector2 position;
        public Vector2 velocity;
        public float age;
        public float life;
    }

    private struct Speck
    {
        public Vector2 from;
        public Vector2 to;
        public float start;
        public float arrive;
    }

    private enum Phase { Idle, Burn, Pause, Rebuild }

    private readonly RectTransform surface;
    private readonly Image letter;
    private readonly System.Random random = new System.Random();
    private readonly List<Ember> embers = new List<Ember>();
    private readonly List<Speck> specks = new List<Speck>();
    private readonly Dictionary<(Texture, Rect, int, int), Color32[]> readCache = new Dictionary<(Texture, Rect, int, int), Color32[]>();

    private RawImage overlay;
    private Texture2D texture;
    private Color32[] frame;
    private Color32[] before;
    private Color32[] after;
    private float[] igniteAt;
    private float[] reformAt;
    private int width;
    private int height;
    private int cols;
    private int rows;
    private Phase phase;
    private float time;
    private Action midpoint;
    private float clock;

    public LetterBurn(RectTransform surface, Image letter)
    {
        this.surface = surface;
        this.letter = letter;
    }

    public bool IsPlaying => phase != Phase.Idle;

    // onMidpoint runs while the letter is fully burnt: change the game state there.
    // origin: where the fire starts, in 0..1 of the letter (bottom-left origin); null picks
    // two random points near the bottom. headline: optional flame text that grows in while
    // the letter burns and crumbles away as it rebuilds.
    public void Play(Action onMidpoint, Vector2? origin = null, string headline = null)
    {
        if (IsPlaying)
        {
            return;
        }

        midpoint = onMidpoint;
        EnsureOverlay();
        before = Capture();
        igniteAt = BurnOrder(origin);
        PrepareHeadline(headline, origin ?? new Vector2(0.5f, 0.5f));
        clock = 0f;
        embers.Clear();
        specks.Clear();
        SetContentVisible(false);
        overlay.transform.SetAsLastSibling();
        overlay.enabled = true;
        phase = Phase.Burn;
        time = 0f;
        Render();
    }

    public void Tick(float deltaTime)
    {
        if (!IsPlaying)
        {
            return;
        }

        float dt = Mathf.Min(deltaTime, 0.05f);
        time += dt;
        clock += dt;
        UpdateEmbers(dt);
        switch (phase)
        {
            case Phase.Burn:
                if (time >= BurnDuration)
                {
                    phase = Phase.Pause;
                    time = 0f;
                }
                break;

            case Phase.Pause:
                if (time >= (headlinePixels.Count > 0 ? HeadlinePause : PauseDuration))
                {
                    RunMidpoint();
                    after = Capture();
                    reformAt = ReformOrder();
                    SpawnAsh();
                    phase = Phase.Rebuild;
                    time = 0f;
                }
                break;

            case Phase.Rebuild:
                if (time >= RebuildDuration + 0.05f)
                {
                    Finish();
                    return;
                }
                break;
        }
        Render();
    }

    // Skips to the end: the state change still happens, the real UI comes back.
    public void Cancel()
    {
        if (!IsPlaying)
        {
            return;
        }
        RunMidpoint();
        Finish();
    }

    public void Dispose()
    {
        if (texture != null)
        {
            UnityEngine.Object.Destroy(texture);
            texture = null;
        }
    }

    private void RunMidpoint()
    {
        Action action = midpoint;
        midpoint = null;
        action?.Invoke();
    }

    private void Finish()
    {
        phase = Phase.Idle;
        embers.Clear();
        specks.Clear();
        overlay.enabled = false;
        SetContentVisible(true);
    }

    private void SetContentVisible(bool visible)
    {
        foreach (Transform child in surface)
        {
            if (overlay != null && child == overlay.transform)
            {
                continue;
            }
            CanvasGroup group = child.GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = child.gameObject.AddComponent<CanvasGroup>();
                group.interactable = false;
                group.blocksRaycasts = false;
            }
            group.alpha = visible ? 1f : 0f;
        }
    }

    private void EnsureOverlay()
    {
        Sprite sprite = letter != null ? letter.sprite : null;
        int w = sprite != null ? Mathf.RoundToInt(sprite.rect.width) : 384;
        int h = sprite != null ? Mathf.RoundToInt(sprite.rect.height) : 240;

        if (overlay == null)
        {
            overlay = new GameObject("Burn", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            overlay.transform.SetParent(surface, false);
            overlay.raycastTarget = false;
            overlay.rectTransform.anchorMin = Vector2.zero;
            overlay.rectTransform.anchorMax = Vector2.one;
            overlay.rectTransform.offsetMin = overlay.rectTransform.offsetMax = Vector2.zero;
            overlay.enabled = false;
        }

        if (texture == null || texture.width != w || texture.height != h)
        {
            Dispose();
            texture = new Texture2D(w, h, GraphicsFormat.R8G8B8A8_SRGB, TextureCreationFlags.None)
            {
                name = "Letter burn",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            overlay.texture = texture;
        }

        width = w;
        height = h;
        cols = (w + Cell - 1) / Cell;
        rows = (h + Cell - 1) / Cell;
        frame = new Color32[w * h];
    }

    // ---- Timelines ----------------------------------------------------------------

    // When each cell catches, 0..1: distance from two ignition points near the bottom,
    // roughened with noise so the flame front is ragged.
    private float[] BurnOrder(Vector2? origin)
    {
        var a = new Vector2((float)random.NextDouble() * cols, 0f);
        var b = new Vector2((float)random.NextDouble() * cols, (float)random.NextDouble() * rows * 0.4f);
        if (origin.HasValue)
        {
            // One fire, fanning out from a fixed point.
            a = b = new Vector2(origin.Value.x * cols, origin.Value.y * rows);
        }
        int seed = random.Next();
        var order = new float[cols * rows];
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < cols; x++)
            {
                var p = new Vector2(x, y);
                float d = Mathf.Min(Vector2.Distance(p, a), Vector2.Distance(p, b));
                order[y * cols + x] = d / cols + Noise(x, y, seed) * 0.3f;
            }
        }
        Normalise(order);
        return order;
    }

    // When each cell re-forms, 0..1: the edges first, closing in on the middle.
    private float[] ReformOrder()
    {
        int seed = random.Next();
        var order = new float[cols * rows];
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < cols; x++)
            {
                float edge = Mathf.Min(Mathf.Min(x, cols - 1 - x) / (float)cols, Mathf.Min(y, rows - 1 - y) / (float)rows);
                order[y * cols + x] = edge + Noise(x, y, seed) * 0.12f;
            }
        }
        Normalise(order);
        return order;
    }

    private static void Normalise(float[] values)
    {
        float min = float.MaxValue;
        float max = float.MinValue;
        foreach (float v in values)
        {
            min = Mathf.Min(min, v);
            max = Mathf.Max(max, v);
        }
        float span = Mathf.Max(max - min, 1e-5f);
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = (values[i] - min) / span;
        }
    }

    // Two octaves of smooth value noise on the cell grid, 0..1.
    private static float Noise(float x, float y, int seed)
    {
        return ValueNoise(x / 7f, y / 7f, seed) * 0.65f + ValueNoise(x / 2.5f, y / 2.5f, seed + 17) * 0.35f;
    }

    private static float ValueNoise(float x, float y, int seed)
    {
        int xi = Mathf.FloorToInt(x);
        int yi = Mathf.FloorToInt(y);
        float fx = x - xi;
        float fy = y - yi;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);
        float top = Mathf.Lerp(Hash(xi, yi, seed), Hash(xi + 1, yi, seed), fx);
        float bottom = Mathf.Lerp(Hash(xi, yi + 1, seed), Hash(xi + 1, yi + 1, seed), fx);
        return Mathf.Lerp(top, bottom, fy);
    }

    private static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 982451653);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return h / (float)uint.MaxValue;
        }
    }

    // ---- Particles ------------------------------------------------------------------

    private void SpawnEmber(int cx, int cy)
    {
        if (embers.Count >= MaxEmbers)
        {
            return;
        }
        embers.Add(new Ember
        {
            position = new Vector2((cx + 0.5f) * Cell, (cy + 0.5f) * Cell),
            velocity = new Vector2(Range(-8f, 8f), Range(18f, 45f)),
            life = Range(0.5f, 1.3f),
        });
    }

    private void UpdateEmbers(float dt)
    {
        for (int i = embers.Count - 1; i >= 0; i--)
        {
            Ember ember = embers[i];
            ember.age += dt;
            ember.velocity.x += Range(-30f, 30f) * dt;
            ember.position += ember.velocity * dt;
            if (ember.age >= ember.life || ember.position.y >= height)
            {
                embers.RemoveAt(i);
                continue;
            }
            embers[i] = ember;
        }
    }

    // Each speck flies in to a cell of the new letter and lands as that cell re-forms.
    private void SpawnAsh()
    {
        specks.Clear();
        for (int n = 0; n < AshCount; n++)
        {
            int cell = -1;
            for (int attempt = 0; attempt < 8 && cell < 0; attempt++)
            {
                int candidate = random.Next(cols * rows);
                if (HasPaper(after, candidate % cols, candidate / cols))
                {
                    cell = candidate;
                }
            }
            if (cell < 0)
            {
                continue;
            }

            var to = new Vector2((cell % cols + 0.5f) * Cell, (cell / cols + 0.5f) * Cell);
            float angle = Range(0f, Mathf.PI * 2f);
            Vector2 from = to + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Range(15f, 55f);
            from.x = Mathf.Clamp(from.x, 0f, width - 1);
            from.y = Mathf.Clamp(from.y, 0f, height - 1);
            float arrive = reformAt[cell] * RebuildDuration / (1f + Band);
            specks.Add(new Speck { from = from, to = to, start = Mathf.Max(0f, arrive - 0.55f), arrive = arrive });
        }
    }

    private float Range(float min, float max)
    {
        return min + (float)random.NextDouble() * (max - min);
    }

    // ---- Rendering --------------------------------------------------------------------

    private void Render()
    {
        Array.Clear(frame, 0, frame.Length);
        if (phase == Phase.Burn)
        {
            DrawBurn(time / BurnDuration * (1f + Band));
        }
        else if (phase == Phase.Rebuild)
        {
            DrawRebuild(time / RebuildDuration * (1f + Band));
            DrawSpecks();
        }
        DrawEmbers();
        DrawHeadline();
        texture.SetPixels32(frame);
        texture.Apply(false);
    }

    private void DrawBurn(float progress)
    {
        for (int cy = 0; cy < rows; cy++)
        {
            for (int cx = 0; cx < cols; cx++)
            {
                if (!HasPaper(before, cx, cy))
                {
                    continue;
                }
                float age = (progress - igniteAt[cy * cols + cx]) / Band;
                if (age >= 1f)
                {
                    continue; // Burnt away.
                }
                if (age < 0f)
                {
                    // Browns just ahead of the flames.
                    float heat = Mathf.Clamp01(1f + age * Band / ScorchLead);
                    CopyBlock(before, cx, cy, Scorch, heat * heat * 0.8f);
                    continue;
                }

                float flicker = (float)random.NextDouble() * 0.12f;
                int step = Mathf.Clamp((int)((age + flicker) * FireRamp.Length), 0, FireRamp.Length - 1);
                FillBlock(cx, cy, FireRamp[step], 1f);
                if (age > 0.25f && age < 0.6f && random.NextDouble() < EmberChance)
                {
                    SpawnEmber(cx, cy);
                }
            }
        }
    }

    private void DrawRebuild(float progress)
    {
        for (int cy = 0; cy < rows; cy++)
        {
            for (int cx = 0; cx < cols; cx++)
            {
                float age = (progress - reformAt[cy * cols + cx]) / Band;
                if (age < 0f || !HasPaper(after, cx, cy))
                {
                    continue;
                }
                // A warm seam glows where the paper is still knitting together.
                CopyBlock(after, cx, cy, Seam, age >= 1f ? 0f : (1f - age) * 0.85f);
            }
        }
    }

    private void DrawSpecks()
    {
        float fadeIn = Mathf.Clamp01(time / 0.15f);
        foreach (Speck speck in specks)
        {
            if (time >= speck.arrive)
            {
                continue;
            }
            float t = time <= speck.start ? 0f : Mathf.Clamp01((time - speck.start) / Mathf.Max(speck.arrive - speck.start, 1e-4f));
            t = t * t * (3f - 2f * t);
            Vector2 position = Vector2.Lerp(speck.from, speck.to, t);
            Color32 color = Color32.Lerp(Ash, Seam, t * 0.7f);
            FillBlock(Mathf.FloorToInt(position.x / Cell), Mathf.FloorToInt(position.y / Cell), color, fadeIn * 0.85f);
        }
    }

    private void DrawEmbers()
    {
        foreach (Ember ember in embers)
        {
            float t = ember.age / ember.life;
            int step = Mathf.Clamp(1 + (int)(t * 4f), 0, FireRamp.Length - 1);
            FillBlock(Mathf.FloorToInt(ember.position.x / Cell), Mathf.FloorToInt(ember.position.y / Cell), FireRamp[step], 1f - t * 0.7f);
        }
    }

    private bool HasPaper(Color32[] source, int cx, int cy)
    {
        int x = Mathf.Min(cx * Cell + Cell / 2, width - 1);
        int y = Mathf.Min(cy * Cell + Cell / 2, height - 1);
        return source[y * width + x].a > 16;
    }

    // Copies one cell of a captured bitmap, pulled towards tint by amount.
    private void CopyBlock(Color32[] source, int cx, int cy, Color32 tint, float amount)
    {
        int x0 = cx * Cell;
        int y0 = cy * Cell;
        for (int y = y0; y < Mathf.Min(y0 + Cell, height); y++)
        {
            for (int x = x0; x < Mathf.Min(x0 + Cell, width); x++)
            {
                int i = y * width + x;
                Color32 c = source[i];
                if (amount > 0f)
                {
                    byte a = c.a;
                    c = Color32.Lerp(c, tint, amount);
                    c.a = a;
                }
                frame[i] = c;
            }
        }
    }

    // Blends a solid cell over the frame.
    private void FillBlock(int cx, int cy, Color32 color, float alpha)
    {
        if (cx < 0 || cy < 0 || cx >= cols || cy >= rows || alpha <= 0f)
        {
            return;
        }
        int x0 = cx * Cell;
        int y0 = cy * Cell;
        for (int y = y0; y < Mathf.Min(y0 + Cell, height); y++)
        {
            for (int x = x0; x < Mathf.Min(x0 + Cell, width); x++)
            {
                int i = y * width + x;
                frame[i] = Over(frame[i], color, alpha);
            }
        }
    }

    private static Color32 Over(Color32 under, Color32 over, float alpha)
    {
        float a = Mathf.Clamp01(alpha * over.a / 255f);
        float ua = under.a / 255f;
        float outA = a + ua * (1f - a);
        if (outA <= 0f)
        {
            return new Color32(0, 0, 0, 0);
        }
        return new Color32(
            (byte)((over.r * a + under.r * ua * (1f - a)) / outA),
            (byte)((over.g * a + under.g * ua * (1f - a)) / outA),
            (byte)((over.b * a + under.b * ua * (1f - a)) / outA),
            (byte)(outA * 255f));
    }

    // ---- Flame headline ------------------------------------------------------------------

    // Built from PixelFont capitals, each font pixel a 2x2-cell block. Letters appear in a
    // random order; each grows outward from a random seed point inside the letter's box.
    // Three pre-baked frames of flame tongues and colour jitter loop while it's on screen.
    private const float HeadlinePause = 1.2f;
    private const int HeadlineFontPixel = 2;   // Cells per font pixel.
    private const float HeadlineFirst = 0.1f;
    private const float HeadlineLast = 1.0f;
    private const float HeadlineGrow = 0.35f;
    private const float HeadlineFps = 9f;
    private const float HeadlineCrumble = 0.4f;
    private static readonly Color32 HeadlineOutline = new Color32(26, 14, 8, 235);

    private struct HeadPixel
    {
        public int X;       // Font pixels from the phrase's left edge.
        public int Y;       // Font pixels down from cap height; flame tongues are negative.
        public int Letter;
        public int Frame;   // -1 = every frame (the letter body), else only that frame.
    }

    private readonly List<HeadPixel> headlinePixels = new List<HeadPixel>();
    private float[] letterStart = new float[0];
    private Vector2[] letterSeed = new Vector2[0];
    private float[] letterReach = new float[0];
    private int headlineX;
    private int headlineTop;

    private void PrepareHeadline(string text, Vector2 centre)
    {
        headlinePixels.Clear();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var boxes = new List<RectInt>();
        int pen = 0;
        foreach (char c in text)
        {
            string[] glyph = PixelFont.GetRows(c);
            int glyphWidth = glyph[0].Length;
            if (c != ' ')
            {
                int letter = boxes.Count;
                boxes.Add(new RectInt(pen, 0, glyphWidth, PixelFont.CapHeight));
                for (int col = 0; col < glyphWidth; col++)
                {
                    int topInk = -1;
                    for (int row = 0; row < PixelFont.CapHeight && row < glyph.Length; row++)
                    {
                        if (glyph[row][col] != '#') continue;
                        headlinePixels.Add(new HeadPixel { X = pen + col, Y = row, Letter = letter, Frame = -1 });
                        if (topInk < 0) topInk = row;
                    }
                    if (topInk < 0) continue;
                    // Flame tongues licking up from the top of each column, different per frame.
                    for (int frameIndex = 0; frameIndex < 3; frameIndex++)
                    {
                        int tongue = random.Next(0, 3);
                        for (int k = 1; k <= tongue; k++)
                        {
                            headlinePixels.Add(new HeadPixel { X = pen + col, Y = topInk - k, Letter = letter, Frame = frameIndex });
                        }
                    }
                }
            }
            pen += glyphWidth + PixelFont.Spacing;
        }

        // Random reveal order, spread over the burn; random seed point per letter.
        int count = boxes.Count;
        letterStart = new float[count];
        letterSeed = new Vector2[count];
        letterReach = new float[count];
        var order = new List<int>();
        for (int i = 0; i < count; i++) order.Add(i);
        for (int i = count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }
        for (int slot = 0; slot < count; slot++)
        {
            int letter = order[slot];
            letterStart[letter] = Mathf.Lerp(HeadlineFirst, HeadlineLast, count > 1 ? slot / (float)(count - 1) : 0f);
            RectInt box = boxes[letter];
            letterSeed[letter] = new Vector2(box.x + Range(0f, box.width), Range(0f, box.height));
            float reach = 0f;
            foreach (Vector2 corner in new[] { new Vector2(box.xMin, -2f), new Vector2(box.xMax, -2f), new Vector2(box.xMin, box.yMax), new Vector2(box.xMax, box.yMax) })
            {
                reach = Mathf.Max(reach, Vector2.Distance(corner, letterSeed[letter]));
            }
            letterReach[letter] = reach + 1f;
        }

        int phraseWidth = Mathf.Max(0, pen - PixelFont.Spacing);
        headlineX = Mathf.RoundToInt(centre.x * cols) - phraseWidth * HeadlineFontPixel / 2;
        headlineTop = Mathf.RoundToInt(centre.y * rows) + PixelFont.CapHeight * HeadlineFontPixel / 2;
    }

    private void DrawHeadline()
    {
        if (headlinePixels.Count == 0)
        {
            return;
        }

        int frameIndex = (int)(clock * HeadlineFps) % 3;
        float crumble = phase == Phase.Rebuild ? Mathf.Clamp01(time / HeadlineCrumble) : 0f;
        if (crumble >= 1f)
        {
            return;
        }

        // Dark outline first, then the flames, so neighbouring letters never cover each other's glow.
        for (int pass = 0; pass < 2; pass++)
        {
            foreach (HeadPixel pixel in headlinePixels)
            {
                if (pixel.Frame >= 0 && pixel.Frame != frameIndex)
                {
                    continue;
                }
                float age = clock - letterStart[pixel.Letter];
                if (age < 0f)
                {
                    continue;
                }

                // Grows from the seed: a pixel shows once the growth radius passes it (ragged edge).
                float grow = Mathf.Clamp01(age / HeadlineGrow);
                float radius = grow * grow * (3f - 2f * grow) * letterReach[pixel.Letter];
                float jitter = Hash(pixel.X, pixel.Y, 31 + pixel.Letter) * 0.8f;
                float since = radius - (Vector2.Distance(new Vector2(pixel.X + 0.5f, pixel.Y + 0.5f), letterSeed[pixel.Letter]) + jitter);
                if (since < 0f)
                {
                    continue;
                }
                if (crumble > 0f && Hash(pixel.X, pixel.Y, 47) < crumble)
                {
                    continue; // Crumbles away as the letter rebuilds.
                }

                int cx = headlineX + pixel.X * HeadlineFontPixel;
                int cy = headlineTop - (pixel.Y + 1) * HeadlineFontPixel;
                if (pass == 0)
                {
                    for (int y = cy - 1; y <= cy + HeadlineFontPixel; y++)
                    {
                        for (int x = cx - 1; x <= cx + HeadlineFontPixel; x++)
                        {
                            FillBlock(x, y, HeadlineOutline, 0.85f);
                        }
                    }
                    continue;
                }

                Color32 color = FlameColor(pixel, frameIndex);
                // Freshly grown pixels flash white-hot for a moment.
                float flash = Mathf.Clamp01(1f - since * 1.5f);
                color = Color32.Lerp(color, FireRamp[0], flash);
                for (int y = cy; y < cy + HeadlineFontPixel; y++)
                {
                    for (int x = cx; x < cx + HeadlineFontPixel; x++)
                    {
                        FillBlock(x, y, color, 1f);
                    }
                }
            }
        }
    }

    // Hot at the bottom of each letter, cooling to red at the tongues, with a per-frame
    // shift so the colours flicker between the three frames.
    private static Color32 FlameColor(HeadPixel pixel, int frameIndex)
    {
        int step = pixel.Y < 0 ? 3 : pixel.Y <= 1 ? 2 : pixel.Y <= 3 ? 1 : 0;
        float shift = Hash(pixel.X, pixel.Y, 53 + frameIndex);
        if (shift < 0.25f) step = Mathf.Max(0, step - 1);
        else if (shift > 0.8f) step = Mathf.Min(4, step + 1);
        return FireRamp[step];
    }

    // ---- Capture ----------------------------------------------------------------------

    // Flattens what the letter surface shows into one bitmap, bottom row first.
    private Color32[] Capture()
    {
        var buffer = new Color32[width * height];
        readCache.Clear();

        if (letter != null && letter.enabled && letter.sprite != null)
        {
            Sprite sprite = letter.sprite;
            Texture source = sprite.texture;
            Rect r = sprite.textureRect;
            var uv = new Rect(r.x / source.width, r.y / source.height, r.width / source.width, r.height / source.height);
            Blit(buffer, ReadRegion(source, uv, width, height), 0, 0, width, height, letter.color);
        }

        foreach (Graphic graphic in surface.GetComponentsInChildren<Graphic>())
        {
            if (graphic == overlay || graphic == letter || !graphic.enabled)
            {
                continue;
            }

            Rect area = ToLetter(graphic.rectTransform);
            int x = Mathf.RoundToInt(area.xMin);
            int y = Mathf.RoundToInt(area.yMin);
            int w = Mathf.Max(1, Mathf.RoundToInt(area.xMax) - x);
            int h = Mathf.Max(1, Mathf.RoundToInt(area.yMax) - y);

            if (graphic is PixelText label)
            {
                Rasterize(buffer, label, area);
            }
            else if (graphic is RawImage raw && raw.texture != null)
            {
                Blit(buffer, ReadRegion(raw.texture, raw.uvRect, w, h), x, y, w, h, raw.color);
            }
            else if (graphic is Image image && image.sprite != null)
            {
                Sprite sprite = image.sprite;
                Rect r = sprite.textureRect;
                Texture source = sprite.texture;
                var uv = new Rect(r.x / source.width, r.y / source.height, r.width / source.width, r.height / source.height);
                Blit(buffer, ReadRegion(source, uv, w, h), x, y, w, h, image.color);
            }
            else
            {
                FillRect(buffer, x, y, w, h, graphic.color);
            }
        }
        return buffer;
    }

    // A RectTransform's area in bitmap pixels, origin at the bottom-left.
    private Rect ToLetter(RectTransform rect)
    {
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        Vector3 min = surface.InverseTransformPoint(corners[0]);
        Vector3 max = surface.InverseTransformPoint(corners[2]);
        Rect s = surface.rect;
        float x0 = (min.x - s.xMin) / s.width * width;
        float y0 = (min.y - s.yMin) / s.height * height;
        float x1 = (max.x - s.xMin) / s.width * width;
        float y1 = (max.y - s.yMin) / s.height * height;
        return Rect.MinMaxRect(Mathf.Min(x0, x1), Mathf.Min(y0, y1), Mathf.Max(x0, x1), Mathf.Max(y0, y1));
    }

    // Lays PixelText out the same way it draws itself (centred lines, cap-height font
    // pixels), but in bitmap pixels.
    private void Rasterize(Color32[] buffer, PixelText label, Rect area)
    {
        string text = label.uppercase ? label.text.ToUpperInvariant() : label.text;
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        float unitsToPixels = width / surface.rect.width;
        int px = Mathf.Max(1, Mathf.RoundToInt(label.fontSize / PixelFont.CapHeight * unitsToPixels));
        string[] lines = text.Split('\n');
        int block = ((lines.Length - 1) * PixelFont.LineHeight + PixelFont.CapHeight) * px;
        int horizontal = (int)label.alignment % 3;
        int vertical = (int)label.alignment / 3;
        float top = vertical == 0 ? area.yMax : vertical == 2 ? area.yMin + block : area.center.y + block * 0.5f;
        int topRow = Mathf.RoundToInt(top);

        Color main = label.color;
        Color shadow = label.shadowColor;
        shadow.a *= main.a;
        for (int pass = shadow.a > 0f ? 0 : 1; pass < 2; pass++)
        {
            bool isShadow = pass == 0;
            bool inKey = false;
            for (int line = 0; line < lines.Length; line++)
            {
                string text2 = lines[line];
                int lineWidth = PixelFont.Measure(text2) * px;
                float left = horizontal == 0 ? area.xMin : horizontal == 2 ? area.xMax - lineWidth : area.center.x - lineWidth * 0.5f;
                int penX = Mathf.RoundToInt(left) + (isShadow ? px : 0);
                int baseY = topRow - line * PixelFont.LineHeight * px - (isShadow ? px : 0);
                foreach (char c in text2)
                {
                    if (label.highlightBrackets && c == '[') inKey = true;
                    Color ink = isShadow ? shadow : (inKey ? label.keyColor * main : main);
                    string[] glyph = PixelFont.GetRows(c);
                    for (int row = 0; row < glyph.Length; row++)
                    {
                        for (int col = 0; col < glyph[row].Length; col++)
                        {
                            if (glyph[row][col] == '#')
                            {
                                FillRect(buffer, penX + col * px, baseY - (row + 1) * px, px, px, ink);
                            }
                        }
                    }
                    penX += (glyph[0].Length + PixelFont.Spacing) * px;
                    if (label.highlightBrackets && c == ']') inKey = false;
                }
            }
        }
    }

    private void FillRect(Color32[] buffer, int x, int y, int w, int h, Color color)
    {
        Color32 c = color;
        for (int row = Mathf.Max(0, y); row < Mathf.Min(height, y + h); row++)
        {
            for (int col = Mathf.Max(0, x); col < Mathf.Min(width, x + w); col++)
            {
                int i = row * width + col;
                buffer[i] = Over(buffer[i], c, 1f);
            }
        }
    }

    private void Blit(Color32[] buffer, Color32[] source, int x, int y, int w, int h, Color tint)
    {
        for (int row = 0; row < h; row++)
        {
            int ty = y + row;
            if (ty < 0 || ty >= height) continue;
            for (int col = 0; col < w; col++)
            {
                int tx = x + col;
                if (tx < 0 || tx >= width) continue;
                Color32 s = source[row * w + col];
                var tinted = new Color32((byte)(s.r * tint.r), (byte)(s.g * tint.g), (byte)(s.b * tint.b), (byte)(s.a * tint.a));
                int i = ty * width + tx;
                buffer[i] = Over(buffer[i], tinted, 1f);
            }
        }
    }

    // Reads part of any texture back at a given size through the GPU, so source
    // textures don't need Read/Write enabled.
    private Color32[] ReadRegion(Texture source, Rect uv, int w, int h)
    {
        var key = (source, uv, w, h);
        Color32[] pixels;
        if (readCache.TryGetValue(key, out pixels))
        {
            return pixels;
        }

        RenderTexture target = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(source, target, uv.size, uv.position);
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = target;
        var readback = new Texture2D(w, h, TextureFormat.RGBA32, false);
        readback.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
        pixels = readback.GetPixels32();
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(target);
        UnityEngine.Object.Destroy(readback);

        readCache[key] = pixels;
        return pixels;
    }
}
