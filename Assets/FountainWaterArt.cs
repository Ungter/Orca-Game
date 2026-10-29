using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

// Pixel art for the fountain's water. The droplets and splashes are hand-drawn: one string
// per pixel row, top to bottom, one palette letter per pixel, '.' clear. They're laid out as
// particle texture sheets. The pool is drawn by code in the same palette so it fits the
// basin at whatever size it is, and loops over FrameCount frames.
public static class FountainWaterArt
{
    public const int FrameCount = 12;
    // Same density as the sidewalk gravel the fountain stands on.
    public const float PixelsPerMetre = 16f;
    // Side of one droplet or splash cell, in pixels. Cells are square so particles are too.
    public const int CellSize = 7;
    public const int DropletVariants = 4;
    public const int DropletStages = 4;
    public const int SplashFrames = 4;

    private static readonly Dictionary<char, Color32> Palette = new Dictionary<char, Color32>
    {
        // Falling water: dark edge, body, lit side, highlight, foam.
        { 'D', new Color32(30, 62, 122, 255) },
        { 'B', new Color32(52, 108, 180, 255) },
        { 'L', new Color32(98, 166, 224, 255) },
        { 'C', new Color32(168, 222, 248, 255) },
        { 'W', new Color32(238, 250, 255, 255) },
        // Pool: deep, base, ripple, glint, foam, white foam.
        { 'd', new Color32(22, 48, 92, 255) },
        { 'm', new Color32(34, 72, 128, 255) },
        { 'l', new Color32(58, 112, 172, 255) },
        { 'h', new Color32(110, 172, 226, 255) },
        { 'f', new Color32(196, 234, 250, 255) },
        { 'w', new Color32(240, 250, 255, 255) },
    };

    // Rows are variants; across each row, the stages of a droplet's life: a sparkle as it
    // bursts from the spout, a round drop, a smaller drop, a streak as it falls.
    private static readonly string[][][] Droplets =
    {
        new[]
        {
            new[] { ".......", "...W...", "...C...", ".WCWCW.", "...C...", "...W...", "......." },
            new[] { ".......", ".......", "...W...", "..CWC..", "...C...", ".......", "......." },
            new[] { ".......", ".......", "...W...", "...C...", ".......", ".......", "......." },
            new[] { ".......", "...W...", "...C...", "...L...", ".......", ".......", "......." },
        },
        new[]
        {
            new[] { ".......", ".......", "..W.W..", "...C...", "..W.W..", ".......", "......." },
            new[] { ".......", ".......", "..CW...", "..WC...", ".......", ".......", "......." },
            new[] { ".......", ".......", "...C...", "...L...", ".......", ".......", "......." },
            new[] { ".......", "...C...", "...C...", "...L...", "...B...", ".......", "......." },
        },
        new[]
        {
            new[] { ".......", ".......", "...C...", "..CWC..", "...C...", ".......", "......." },
            new[] { ".......", ".......", "...C...", "..CLC..", "...L...", ".......", "......." },
            new[] { ".......", ".......", ".......", "...L...", ".......", ".......", "......." },
            new[] { ".......", ".......", "...C...", "...L...", ".......", ".......", "......." },
        },
        new[]
        {
            new[] { ".......", ".......", ".......", "...W...", ".......", ".......", "......." },
            new[] { ".......", ".......", ".......", "...C...", ".......", ".......", "......." },
            new[] { ".......", ".......", "...L...", "...B...", ".......", ".......", "......." },
            new[] { ".......", ".......", "...L...", "...B...", ".......", ".......", "......." },
        },
    };

    // A droplet landing in the pool: impact, a little crown, drops flying out, last drops.
    // The cell is centred where the droplet hit, so the bottom half is under the water and
    // the art sits in the top half, its last row on the water line.
    private static readonly string[][] Splash =
    {
        new[] { ".......", ".......", "...W...", "..LCL..", ".......", ".......", "......." },
        new[] { ".......", "..W.W..", ".C.C.C.", ".L...L.", ".......", ".......", "......." },
        new[] { ".W...W.", "C.....C", ".......", "L.....L", ".......", ".......", "......." },
        new[] { "W.....W", ".......", "L.....L", ".......", ".......", ".......", "......." },
    };

    // Foam clumps scattered round the ring where most droplets land.
    private static readonly string[][] PoolFoam =
    {
        new[] { ".w.", "wfw", ".h." },
        new[] { "fw", "hf" },
        new[] { "w.f", "fh." },
        new[] { "wf", ".h" },
        new[] { "f" },
    };

    // Every droplet cell: DropletVariants rows of DropletStages cells, for a particle
    // system's texture sheet (row 0 at the top).
    public static Texture2D DropletSheet()
    {
        var canvas = new Canvas(CellSize * DropletStages, CellSize * DropletVariants);
        for (int row = 0; row < DropletVariants; row++)
        {
            for (int stage = 0; stage < DropletStages; stage++)
            {
                canvas.Stamp(Droplets[row][stage], stage * CellSize, row * CellSize);
            }
        }
        return canvas.ToTexture("fountain_droplets", false);
    }

    // The splash frames in one row.
    public static Texture2D SplashSheet()
    {
        var canvas = new Canvas(CellSize * SplashFrames, CellSize);
        for (int frame = 0; frame < SplashFrames; frame++)
        {
            canvas.Stamp(Splash[frame], frame * CellSize, 0);
        }
        return canvas.ToTexture("fountain_splash", false);
    }

    // The water in the basin seen from above, every frame: size pixels square, centred on
    // the fountain. Radii in pixels: the column at the water line, the ring where most
    // droplets land, and the basin's inner wall.
    public static Texture2D[] Pool(int size, float column, float landing, float wall)
    {
        var frames = new Texture2D[FrameCount];
        for (int frame = 0; frame < FrameCount; frame++)
        {
            var canvas = new Canvas(size, size);
            DrawPool(canvas, frame, column, landing, wall);
            frames[frame] = canvas.ToTexture("fountain_pool_" + frame, true);
        }
        return frames;
    }

    private static void DrawPool(Canvas canvas, int frame, float column, float landing, float wall)
    {
        int size = canvas.Width;
        float t = frame / (float)FrameCount;
        float c = size / 2f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - c;
                float dy = y + 0.5f - c;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                // Deeper colour against the wall and the column.
                char key = d > wall - 3f || d < column + 2f ? 'd' : 'm';
                // Broken rings running out from the landing ring to the wall, and in to the column.
                float angle = Mathf.Atan2(dy, dx);
                for (int j = 0; j < 2; j++)
                {
                    float p = Mathf.Repeat(t + j / 2f, 1f);
                    float outer = landing + 4f + p * (wall - landing - 7f);
                    float inner = landing - 4f - p * (landing - column - 6f);
                    if (OnRipple(d, angle, outer, j, p) || OnRipple(d, angle, inner, j, p))
                    {
                        key = 'l';
                    }
                }
                // Churned water where most droplets come down.
                float band = Mathf.Abs(d - landing);
                if (band < 6f && Hash01(x, y, frame / 2) < 0.5f - band * 0.07f)
                {
                    key = 'l';
                }
                canvas.Put(x, y, key);
            }
        }

        // Each foam clump lives four frames, then reappears nearby as another shape.
        int clumps = (int)(2f * Mathf.PI * landing / 3.2f);
        for (int k = 0; k < clumps; k++)
        {
            int life = (frame + (int)(Hash01(k, 9, 0) * 4f)) % FrameCount / 4;
            float angle = (k + Hash01(k, life, 1) - 0.5f) / clumps * 2f * Mathf.PI;
            float radius = landing + (Hash01(k, life, 2) - 0.5f) * 10f;
            string[] stamp = PoolFoam[(int)(Hash01(k, life, 3) * PoolFoam.Length)];
            int x = Mathf.RoundToInt(c + Mathf.Cos(angle) * radius - stamp[0].Length / 2f);
            int y = Mathf.RoundToInt(c + Mathf.Sin(angle) * radius - stamp.Length / 2f);
            canvas.Stamp(stamp, x, y);
        }

        // Glints: short strokes on a jittered grid, each blinking on its own phase.
        const int CellWidth = 11;
        const int CellHeight = 7;
        for (int gy = 0; gy <= size / CellHeight; gy++)
        {
            for (int gx = 0; gx <= size / CellWidth; gx++)
            {
                float life = Mathf.Repeat(t + Hash01(gx, gy, 3), 1f);
                if (life > 0.55f)
                {
                    continue;
                }
                int x0 = gx * CellWidth + (int)(Hash01(gx, gy, 1) * (CellWidth - 5));
                int y0 = gy * CellHeight + (int)(Hash01(gx, gy, 2) * (CellHeight - 1));
                x0 += Mathf.RoundToInt(Mathf.Sin(life / 0.55f * Mathf.PI) * 1.5f);
                int length = 2 + (int)(Hash01(gx, gy, 4) * 3f);
                for (int i = 0; i < length; i++)
                {
                    float dx = x0 + i + 0.5f - c;
                    float dy = y0 + 0.5f - c;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d > column + 2f && d < wall - 3f && Mathf.Abs(d - landing) > 7f)
                    {
                        canvas.Put(x0 + i, y0, i == length - 1 && life < 0.3f ? 'h' : 'l');
                    }
                }
            }
        }
    }

    // On ripple ring j at radius ring, broken into dashes that thin out as it spreads (p 0 to 1).
    private static bool OnRipple(float d, float angle, float ring, int j, float p)
    {
        if (Mathf.Abs(d - ring) >= 0.55f)
        {
            return false;
        }
        int segment = (int)((angle + Mathf.PI) / (2f * Mathf.PI) * ring * 0.9f);
        return Hash01(segment, j, (int)ring) > 0.25f + 0.6f * p;
    }

    // FNV-1a over three ints, then mixed: a stable pseudo-random number in [0, 1).
    private static float Hash01(int a, int b, int c)
    {
        uint hash = 2166136261;
        hash = (hash ^ (uint)a) * 16777619;
        hash = (hash ^ (uint)b) * 16777619;
        hash = (hash ^ (uint)c) * 16777619;
        hash ^= hash >> 13;
        hash *= 1274126177;
        return (hash & 0xFFFFFF) / (float)0x1000000;
    }

    // Palette letters on a grid, rows counted from the top like the maps.
    private class Canvas
    {
        public readonly int Width;
        public readonly int Height;
        private readonly char[] keys;

        public Canvas(int width, int height)
        {
            Width = width;
            Height = height;
            keys = new char[width * height];
        }

        public void Put(int x, int y, char key)
        {
            if (x >= 0 && x < Width && y >= 0 && y < Height)
            {
                keys[y * Width + x] = key;
            }
        }

        public void Stamp(string[] rows, int x, int y)
        {
            for (int row = 0; row < rows.Length; row++)
            {
                for (int col = 0; col < rows[row].Length; col++)
                {
                    if (rows[row][col] != '.')
                    {
                        Put(x + col, y + row, rows[row][col]);
                    }
                }
            }
        }

        // Point filtered. The pool gets mipmaps like the gravel (crisp up close, no crawling
        // far away); particle sheets don't, or neighbouring cells would bleed together and
        // alpha-clipped droplets would fade out in the distance. Clear pixels keep the
        // water's body colour so mipmaps don't fringe dark.
        public Texture2D ToTexture(string name, bool mipmaps)
        {
            Color32 clear = Palette['B'];
            clear.a = 0;
            var pixels = new Color32[Width * Height];
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    char key = keys[y * Width + x];
                    // Texture rows count from the bottom.
                    pixels[(Height - 1 - y) * Width + x] = key == '\0' ? clear : Palette[key];
                }
            }
            var texture = new Texture2D(Width, Height, GraphicsFormat.R8G8B8A8_SRGB,
                mipmaps ? TextureCreationFlags.MipChain : TextureCreationFlags.None)
            {
                name = name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            texture.SetPixels32(pixels);
            texture.Apply(mipmaps, true);
            return texture;
        }
    }
}
