using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

// Hand-lettered cursive that writes itself out stroke by stroke, with a broad-nib pen
// and little drops of ink flicking off the nib.
//
// Glyphs are authored as pen paths: each stroke is one pen-down movement made of
// pieces (Catmull-Rom splines through hand-placed points; a new piece starts a sharp
// corner without lifting the pen). Units: baseline y = 0, x-height = 1. Lowercase letters
// enter at about (0, 0.18) and exit near the baseline on their right, so a word is written
// in one connected stroke; i-dots are "deferred" and added after the word, as people write.
//
// The pen is a calligraphy nib held at NibAngle: every point along a path stamps a thin
// capsule along the nib direction, so strokes are thick or thin depending on direction,
// with tapered starts and ends. Ink is kept as a coverage buffer that only ever grows.
public class CursiveWriter
{
    private const float NibAngle = 35f;
    private const float NibWidth = 0.17f;
    private const float HairRadius = 0.022f;
    private const float WriteSeconds = 4f;
    private const float LiftPause = 0.14f;
    private const float Margin = 0.35f;
    private const float SpaceAdvance = 0.5f;
    private const float StampStep = 0.35f;
    private const float DropRate = 220f;
    private const int SamplesPerSegment = 14;

    private static readonly Color32 InkColor = new Color32(36, 24, 18, 255);
    private static readonly Color32 DropColor = new Color32(14, 9, 7, 255);

    private sealed class Stroke
    {
        public Vector2[][] Pieces;
        public bool Deferred;
    }

    private sealed class Glyph
    {
        public float Advance;
        public bool JoinIn;
        public bool JoinOut;
        public Stroke[] Strokes;
    }

    private sealed class PenPath
    {
        public Vector2[] Points;
        public float[] Distance;
        public float Length;
    }

    private struct Drop
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public float Age;
        public float Life;
        public int Size;
    }

    private static readonly Dictionary<char, Glyph> Glyphs = BuildGlyphs();

    private readonly List<PenPath> paths = new List<PenPath>();
    private readonly List<Drop> drops = new List<Drop>();
    private readonly System.Random random = new System.Random();
    private readonly byte[] ink;
    private readonly Color32[] frame;
    private readonly int width;
    private readonly int height;
    private readonly float nibHalf;
    private readonly float hair;
    private readonly float speed;
    private readonly int dropSize;

    private int pathIndex;
    private float penDistance;
    private float stampCursor;
    private int segmentCursor;
    private float pause;
    private float dropBudget;

    public Texture2D Texture { get; }
    public Vector2 SizeInLetterPixels { get; }
    public bool IsPlaying { get; private set; }
    public bool HasStarted { get; private set; }

    // maxWidth / xHeight are in letter pixels; texelsPerPixel sets the texture resolution.
    public CursiveWriter(string text, float maxWidth, float xHeight, int texelsPerPixel)
    {
        List<List<Vector2>> strokes = Layout(text);
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);
        foreach (List<Vector2> stroke in strokes)
        {
            foreach (Vector2 p in stroke)
            {
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
        }
        if (strokes.Count == 0)
        {
            min = Vector2.zero;
            max = Vector2.one;
        }
        min -= Vector2.one * Margin;
        max += Vector2.one * Margin;

        Vector2 units = max - min;
        float pixelsPerUnit = Mathf.Min(xHeight, maxWidth / units.x);
        float texelsPerUnit = pixelsPerUnit * texelsPerPixel;
        SizeInLetterPixels = units * pixelsPerUnit;
        width = Mathf.Max(1, Mathf.CeilToInt(units.x * texelsPerUnit));
        height = Mathf.Max(1, Mathf.CeilToInt(units.y * texelsPerUnit));
        nibHalf = NibWidth * 0.5f * texelsPerUnit;
        hair = Mathf.Max(0.6f, HairRadius * texelsPerUnit);
        dropSize = Mathf.Max(2, Mathf.RoundToInt(texelsPerPixel * 0.75f));

        float total = 0f;
        foreach (List<Vector2> stroke in strokes)
        {
            var path = new PenPath { Points = new Vector2[stroke.Count], Distance = new float[stroke.Count] };
            for (int i = 0; i < stroke.Count; i++)
            {
                path.Points[i] = (stroke[i] - min) * texelsPerUnit;
                if (i > 0)
                {
                    path.Distance[i] = path.Distance[i - 1] + Vector2.Distance(path.Points[i], path.Points[i - 1]);
                }
            }
            path.Length = path.Distance[stroke.Count - 1];
            total += path.Length;
            paths.Add(path);
        }
        speed = Mathf.Max(1f, total / WriteSeconds);

        ink = new byte[width * height];
        frame = new Color32[width * height];
        Texture = new Texture2D(width, height, GraphicsFormat.R8G8B8A8_SRGB, TextureCreationFlags.MipChain)
        {
            name = "Cursive clue",
            filterMode = FilterMode.Trilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave,
        };
        Render();
    }

    public void Play()
    {
        System.Array.Clear(ink, 0, ink.Length);
        drops.Clear();
        pathIndex = 0;
        penDistance = 0f;
        stampCursor = 0f;
        segmentCursor = 0;
        pause = 0.25f;
        IsPlaying = true;
        HasStarted = true;
        Render();
    }

    // Shows the finished writing at once.
    public void Complete()
    {
        foreach (PenPath path in paths)
        {
            int segment = 0;
            for (float d = 0f; d <= path.Length; d += StampStep)
            {
                Stamp(PointAt(path, d, ref segment), Taper(path, d));
            }
        }
        pathIndex = paths.Count;
        drops.Clear();
        IsPlaying = false;
        HasStarted = true;
        Render();
    }

    public void Dispose()
    {
        if (Texture != null)
        {
            Object.Destroy(Texture);
        }
    }

    public void Tick(float deltaTime)
    {
        if (!IsPlaying)
        {
            return;
        }

        float dt = Mathf.Min(deltaTime, 0.05f);
        if (pathIndex < paths.Count)
        {
            if (pause > 0f)
            {
                pause -= dt;
            }
            else
            {
                PenPath path = paths[pathIndex];
                float target = Mathf.Min(path.Length, penDistance + speed * dt);
                for (; stampCursor <= target; stampCursor += StampStep)
                {
                    Stamp(PointAt(path, stampCursor, ref segmentCursor), Taper(path, stampCursor));
                }

                int probe = segmentCursor;
                Vector2 tip = PointAt(path, target, ref probe);
                Vector2 heading = tip - PointAt(path, Mathf.Max(0f, target - 2f), ref probe);
                SpawnDrops(tip, heading, dt);

                penDistance = target;
                if (penDistance >= path.Length)
                {
                    pathIndex++;
                    penDistance = 0f;
                    stampCursor = 0f;
                    segmentCursor = 0;
                    pause = LiftPause;
                }
            }
        }

        UpdateDrops(dt);
        if (pathIndex >= paths.Count && drops.Count == 0)
        {
            IsPlaying = false;
        }
        Render();
    }

    // ---- Pen -------------------------------------------------------------------------

    private float Taper(PenPath path, float d)
    {
        float ramp = nibHalf * 3f;
        float start = Mathf.SmoothStep(0f, 1f, d / ramp);
        float end = Mathf.SmoothStep(0f, 1f, (path.Length - d) / ramp);
        return Mathf.Lerp(0.4f, 1f, start) * Mathf.Lerp(0.4f, 1f, end);
    }

    private static Vector2 PointAt(PenPath path, float d, ref int segment)
    {
        if (segment > 0 && path.Distance[segment] > d)
        {
            segment = 0;
        }
        int last = path.Points.Length - 1;
        while (segment < last - 1 && path.Distance[segment + 1] < d)
        {
            segment++;
        }
        if (last == 0)
        {
            return path.Points[0];
        }
        float span = path.Distance[segment + 1] - path.Distance[segment];
        float t = span > 1e-5f ? Mathf.Clamp01((d - path.Distance[segment]) / span) : 0f;
        return Vector2.Lerp(path.Points[segment], path.Points[segment + 1], t);
    }

    // One nib imprint: a capsule along the nib direction. Coverage only grows (max), so
    // overlapping imprints never darken past solid ink.
    private void Stamp(Vector2 p, float pressure)
    {
        float angle = NibAngle * Mathf.Deg2Rad;
        Vector2 n = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (nibHalf * pressure);
        Vector2 a = p - n;
        Vector2 b = p + n;
        int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, b.x) - hair - 1f));
        int x1 = Mathf.Min(width - 1, Mathf.CeilToInt(Mathf.Max(a.x, b.x) + hair + 1f));
        int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, b.y) - hair - 1f));
        int y1 = Mathf.Min(height - 1, Mathf.CeilToInt(Mathf.Max(a.y, b.y) + hair + 1f));
        Vector2 ab = b - a;
        float abLength = Mathf.Max(ab.sqrMagnitude, 1e-5f);
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                var q = new Vector2(x + 0.5f, y + 0.5f);
                float t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / abLength);
                float distance = Vector2.Distance(q, a + ab * t);
                float cover = Mathf.Clamp01(hair + 0.5f - distance);
                if (cover > 0f)
                {
                    int i = y * width + x;
                    byte value = (byte)(cover * 255f);
                    if (value > ink[i]) ink[i] = value;
                }
            }
        }
    }

    // Tiny drops flick off the nib, mostly backwards and sideways from the motion,
    // fall slightly and fade.
    private void SpawnDrops(Vector2 tip, Vector2 heading, float dt)
    {
        dropBudget += DropRate * dt;
        Vector2 back = heading.sqrMagnitude > 1e-4f ? -heading.normalized : Vector2.down;
        while (dropBudget >= 1f)
        {
            dropBudget -= 1f;
            float angle = Range(-80f, 80f) * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(back.x * Mathf.Cos(angle) - back.y * Mathf.Sin(angle), back.x * Mathf.Sin(angle) + back.y * Mathf.Cos(angle));
            drops.Add(new Drop
            {
                Position = tip + new Vector2(Range(-1f, 1f), Range(-1f, 1f)) * nibHalf * 0.6f,
                Velocity = dir * Range(40f, 170f) * (nibHalf / 5f),
                Life = Range(0.2f, 0.55f),
                Size = dropSize + (random.NextDouble() < 0.3 ? dropSize : random.Next(0, dropSize)),
            });
        }
    }

    private void UpdateDrops(float dt)
    {
        for (int i = drops.Count - 1; i >= 0; i--)
        {
            Drop drop = drops[i];
            drop.Age += dt;
            if (drop.Age >= drop.Life)
            {
                drops.RemoveAt(i);
                continue;
            }
            drop.Velocity *= 1f - Mathf.Min(1f, 4f * dt);
            drop.Velocity.y -= 60f * (nibHalf / 5f) * dt;
            drop.Position += drop.Velocity * dt;
            drops[i] = drop;
        }
    }

    private float Range(float min, float max)
    {
        return min + (float)random.NextDouble() * (max - min);
    }

    // ---- Output ------------------------------------------------------------------------

    private void Render()
    {
        for (int i = 0; i < frame.Length; i++)
        {
            Color32 c = InkColor;
            c.a = ink[i];
            frame[i] = c;
        }

        foreach (Drop drop in drops)
        {
            float fade = Mathf.Clamp01(1.4f * (1f - drop.Age / drop.Life));
            int x0 = Mathf.FloorToInt(drop.Position.x);
            int y0 = Mathf.FloorToInt(drop.Position.y);
            for (int y = y0; y < y0 + drop.Size; y++)
            {
                for (int x = x0; x < x0 + drop.Size; x++)
                {
                    if (x < 0 || y < 0 || x >= width || y >= height) continue;
                    int i = y * width + x;
                    byte alpha = (byte)(255f * fade);
                    if (alpha > frame[i].a || frame[i].a == 0)
                    {
                        Color32 c = DropColor;
                        c.a = (byte)Mathf.Max(alpha, frame[i].a);
                        frame[i] = c;
                    }
                }
            }
        }

        Texture.SetPixels32(frame);
        Texture.Apply(true);
    }

    // ---- Layout ------------------------------------------------------------------------

    // Turns text into pen strokes, in writing order, in glyph units.
    private static List<List<Vector2>> Layout(string text)
    {
        var strokes = new List<List<Vector2>>();
        var deferred = new List<List<Vector2>>();
        List<Vector2> current = null;
        bool canJoin = false;
        float penX = 0f;

        void Flush()
        {
            if (current != null && current.Count > 1)
            {
                strokes.Add(current);
            }
            current = null;
        }

        foreach (char c in text)
        {
            if (c == ' ')
            {
                Flush();
                strokes.AddRange(deferred);
                deferred.Clear();
                canJoin = false;
                penX += SpaceAdvance;
                continue;
            }

            Glyph glyph;
            if (!Glyphs.TryGetValue(c, out glyph))
            {
                Debug.LogWarning("[CursiveWriter] No cursive glyph for '" + c + "'");
                Flush();
                canJoin = false;
                penX += 0.6f;
                continue;
            }

            for (int s = 0; s < glyph.Strokes.Length; s++)
            {
                List<Vector2> points = Sample(glyph.Strokes[s].Pieces, penX);
                if (glyph.Strokes[s].Deferred)
                {
                    deferred.Add(points);
                }
                else if (s == 0 && glyph.JoinIn && canJoin && current != null)
                {
                    current.AddRange(points);
                }
                else
                {
                    Flush();
                    current = points;
                }
            }

            canJoin = glyph.JoinOut;
            if (!glyph.JoinOut)
            {
                Flush();
            }
            penX += glyph.Advance;
        }

        Flush();
        strokes.AddRange(deferred);
        return strokes;
    }

    private static List<Vector2> Sample(Vector2[][] pieces, float offsetX)
    {
        var result = new List<Vector2>();
        var offset = new Vector2(offsetX, 0f);
        foreach (Vector2[] piece in pieces)
        {
            List<Vector2> curve = CatmullRom(piece);
            for (int i = result.Count == 0 ? 0 : 1; i < curve.Count; i++)
            {
                result.Add(curve[i] + offset);
            }
        }
        return result;
    }

    // Smooth curve through every point; the ends are extended in a straight line.
    private static List<Vector2> CatmullRom(Vector2[] p)
    {
        var result = new List<Vector2> { p[0] };
        int n = p.Length;
        for (int i = 0; i < n - 1; i++)
        {
            Vector2 p0 = i == 0 ? 2f * p[0] - p[1] : p[i - 1];
            Vector2 p1 = p[i];
            Vector2 p2 = p[i + 1];
            Vector2 p3 = i + 2 < n ? p[i + 2] : 2f * p[n - 1] - p[n - 2];
            for (int k = 1; k <= SamplesPerSegment; k++)
            {
                float t = k / (float)SamplesPerSegment;
                float t2 = t * t;
                float t3 = t2 * t;
                result.Add(0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3));
            }
        }
        return result;
    }

    // ---- Glyphs ------------------------------------------------------------------------

    private static Vector2[] P(params float[] xy)
    {
        var points = new Vector2[xy.Length / 2];
        for (int i = 0; i < points.Length; i++)
        {
            points[i] = new Vector2(xy[i * 2], xy[i * 2 + 1]);
        }
        return points;
    }

    private static Stroke S(params Vector2[][] pieces) => new Stroke { Pieces = pieces };

    private static Stroke Later(params Vector2[][] pieces) => new Stroke { Pieces = pieces, Deferred = true };

    private static Glyph G(float advance, bool joinIn, bool joinOut, params Stroke[] strokes) =>
        new Glyph { Advance = advance, JoinIn = joinIn, JoinOut = joinOut, Strokes = strokes };

    // Ascender loop shared by l, k and h: up and around, then straight down.
    private static Vector2[] LoopUp(float bottomY)
    {
        return P(0f, 0.18f, 0.32f, 0.75f, 0.55f, 1.55f, 0.48f, 1.95f, 0.3f, 1.85f, 0.22f, 1.15f, 0.2f, bottomY);
    }

    private static Dictionary<char, Glyph> BuildGlyphs()
    {
        return new Dictionary<char, Glyph>
        {
            { 'e', G(0.85f, true, true, S(P(0f, 0.18f, 0.4f, 0.5f, 0.62f, 0.76f, 0.52f, 0.98f, 0.3f, 0.88f, 0.18f, 0.52f, 0.24f, 0.14f, 0.48f, 0f, 0.8f, 0.2f))) },
            { 'l', G(0.75f, true, true, S(P(0f, 0.18f, 0.32f, 0.75f, 0.55f, 1.55f, 0.48f, 1.95f, 0.3f, 1.85f, 0.22f, 1.15f, 0.24f, 0.32f, 0.4f, 0.02f, 0.7f, 0.2f))) },
            { 'r', G(0.95f, true, true, S(
                P(0f, 0.18f, 0.2f, 0.6f, 0.3f, 1f),
                P(0.3f, 1f, 0.42f, 0.86f, 0.6f, 0.98f, 0.66f, 0.72f, 0.6f, 0.22f, 0.68f, 0.02f, 0.9f, 0.2f))) },
            { 'k', G(1f, true, true, S(
                LoopUp(0f),
                P(0.2f, 0f, 0.26f, 0.5f, 0.5f, 0.88f, 0.72f, 0.82f, 0.62f, 0.56f, 0.36f, 0.5f),
                P(0.36f, 0.5f, 0.6f, 0.3f, 0.7f, 0.04f, 0.95f, 0.2f))) },
            { 'h', G(1.05f, true, true, S(
                LoopUp(0f),
                P(0.2f, 0f, 0.28f, 0.6f, 0.5f, 0.95f, 0.7f, 0.8f, 0.72f, 0.3f, 0.78f, 0.03f, 1f, 0.2f))) },
            { 'i', G(0.6f, true, true,
                S(P(0f, 0.18f, 0.22f, 0.6f, 0.32f, 0.95f), P(0.32f, 0.95f, 0.26f, 0.45f, 0.3f, 0.05f, 0.55f, 0.2f)),
                Later(P(0.3f, 1.32f, 0.36f, 1.38f))) },
            { 'f', G(0.8f, true, true, S(P(0f, 0.18f, 0.4f, 0.9f, 0.6f, 1.6f, 0.5f, 1.95f, 0.32f, 1.8f, 0.3f, 1f, 0.28f, 0f, 0.25f, -0.6f, 0.15f, -0.85f, 0.05f, -0.7f, 0.2f, -0.2f, 0.45f, 0.12f, 0.75f, 0.22f))) },
            { ',', G(0.45f, false, false, S(P(0.12f, 0.12f, 0.14f, 0.02f, 0.02f, -0.28f))) },
            { 'F', G(1.45f, false, false,
                S(P(0.2f, 1.55f, 0.6f, 1.74f, 1.1f, 1.68f, 1.5f, 1.82f)),
                S(P(0.98f, 1.72f, 0.88f, 0.9f, 0.72f, 0.25f, 0.46f, -0.02f, 0.15f, 0.1f)),
                S(P(0.5f, 0.92f, 0.8f, 1f, 1.15f, 0.98f))) },
            { 'T', G(1.35f, false, false,
                S(P(0.1f, 1.55f, 0.5f, 1.76f, 1f, 1.68f, 1.5f, 1.8f)),
                S(P(0.95f, 1.72f, 0.85f, 0.9f, 0.7f, 0.25f, 0.45f, -0.02f, 0.15f, 0.1f))) },
            { 'J', G(0.95f, false, true, S(P(0.25f, 1.35f, 0.6f, 1.72f, 0.95f, 1.75f, 1f, 1.45f, 0.85f, 0.5f, 0.68f, -0.3f, 0.48f, -0.8f, 0.24f, -0.85f, 0.2f, -0.6f, 0.5f, -0.15f, 0.9f, 0.15f))) },
        };
    }
}
