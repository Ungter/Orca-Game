using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

// Hand-drawn pixel font: capitals, lowercase, digits and punctuation on a
// 7-pixel grid. Capitals and digits use the top 5 rows (cap height); lowercase
// descenders (g j p q y , ;) drop into the bottom 2 rows. Glyphs are variable width.
//
// Draw calls work in GUI units, so under a scaled GUI.matrix one font pixel
// becomes an exact block of screen pixels. For plain screen-space use, the
// overloads that take a pixelSize set up that matrix for you.
//
//     PixelFont.Draw(new Vector2(20, 20), "Hello, world!", 3, Color.white, Color.black);
//
// Edit a glyph by changing its rows below: '#' is ink, '.' is empty.
public static class PixelFont
{
    public const int CapHeight = 5;
    public const int Height = 7;
    public const int LineHeight = 8;
    public const int Spacing = 1;

    private static readonly Dictionary<char, string[]> Glyphs = new Dictionary<char, string[]>
    {
        // Capitals.
        { 'A', new[] { ".#.", "#.#", "###", "#.#", "#.#" } },
        { 'B', new[] { "##.", "#.#", "##.", "#.#", "##." } },
        { 'C', new[] { ".##", "#..", "#..", "#..", ".##" } },
        { 'D', new[] { "##.", "#.#", "#.#", "#.#", "##." } },
        { 'E', new[] { "###", "#..", "##.", "#..", "###" } },
        { 'F', new[] { "###", "#..", "##.", "#..", "#.." } },
        { 'G', new[] { ".##", "#..", "#.#", "#.#", ".##" } },
        { 'H', new[] { "#.#", "#.#", "###", "#.#", "#.#" } },
        { 'I', new[] { "###", ".#.", ".#.", ".#.", "###" } },
        { 'J', new[] { "..#", "..#", "..#", "#.#", ".#." } },
        { 'K', new[] { "#.#", "#.#", "##.", "#.#", "#.#" } },
        { 'L', new[] { "#..", "#..", "#..", "#..", "###" } },
        { 'M', new[] { "#...#", "##.##", "#.#.#", "#...#", "#...#" } },
        { 'N', new[] { "#..#", "##.#", "#.##", "#..#", "#..#" } },
        { 'O', new[] { ".#.", "#.#", "#.#", "#.#", ".#." } },
        { 'P', new[] { "##.", "#.#", "##.", "#..", "#.." } },
        { 'Q', new[] { ".#.", "#.#", "#.#", "##.", ".##" } },
        { 'R', new[] { "##.", "#.#", "##.", "#.#", "#.#" } },
        { 'S', new[] { ".##", "#..", ".#.", "..#", "##." } },
        { 'T', new[] { "###", ".#.", ".#.", ".#.", ".#." } },
        { 'U', new[] { "#.#", "#.#", "#.#", "#.#", "###" } },
        { 'V', new[] { "#.#", "#.#", "#.#", "#.#", ".#." } },
        { 'W', new[] { "#...#", "#...#", "#.#.#", "##.##", "#...#" } },
        { 'X', new[] { "#.#", "#.#", ".#.", "#.#", "#.#" } },
        { 'Y', new[] { "#.#", "#.#", ".#.", ".#.", ".#." } },
        { 'Z', new[] { "###", "..#", ".#.", "#..", "###" } },

        // Lowercase: x-height is rows 1-4, ascenders reach row 0.
        { 'a', new[] { "...", "##.", ".##", "#.#", ".##" } },
        { 'b', new[] { "#..", "##.", "#.#", "#.#", "##." } },
        { 'c', new[] { "...", ".##", "#..", "#..", ".##" } },
        { 'd', new[] { "..#", ".##", "#.#", "#.#", ".##" } },
        { 'e', new[] { "...", ".#.", "###", "#..", ".##" } },
        { 'f', new[] { ".##", "#..", "##.", "#..", "#.." } },
        { 'g', new[] { "...", ".##", "#.#", "#.#", ".##", "..#", "##." } },
        { 'h', new[] { "#..", "#..", "##.", "#.#", "#.#" } },
        { 'i', new[] { "#", ".", "#", "#", "#" } },
        { 'j', new[] { "..#", "...", "..#", "..#", "..#", "#.#", ".#." } },
        { 'k', new[] { "#..", "#.#", "##.", "##.", "#.#" } },
        { 'l', new[] { "#.", "#.", "#.", "#.", ".#" } },
        { 'm', new[] { ".....", "##.#.", "#.#.#", "#.#.#", "#.#.#" } },
        { 'n', new[] { "...", "##.", "#.#", "#.#", "#.#" } },
        { 'o', new[] { "...", ".#.", "#.#", "#.#", ".#." } },
        { 'p', new[] { "...", "##.", "#.#", "#.#", "##.", "#..", "#.." } },
        { 'q', new[] { "...", ".##", "#.#", "#.#", ".##", "..#", "..#" } },
        { 'r', new[] { "...", "#.#", "##.", "#..", "#.." } },
        { 's', new[] { "...", ".##", "#..", "..#", "##." } },
        { 't', new[] { ".#.", "###", ".#.", ".#.", ".##" } },
        { 'u', new[] { "...", "#.#", "#.#", "#.#", ".##" } },
        { 'v', new[] { "...", "#.#", "#.#", "#.#", ".#." } },
        { 'w', new[] { ".....", "#...#", "#...#", "#.#.#", ".#.#." } },
        { 'x', new[] { "...", "#.#", ".#.", ".#.", "#.#" } },
        { 'y', new[] { "...", "#.#", "#.#", "#.#", ".##", "..#", "##." } },
        { 'z', new[] { "...", "###", "..#", "#..", "###" } },

        // Digits.
        { '0', new[] { "###", "#.#", "#.#", "#.#", "###" } },
        { '1', new[] { ".#.", "##.", ".#.", ".#.", "###" } },
        { '2', new[] { "##.", "..#", ".#.", "#..", "###" } },
        { '3', new[] { "##.", "..#", ".#.", "..#", "##." } },
        { '4', new[] { "#.#", "#.#", "###", "..#", "..#" } },
        { '5', new[] { "###", "#..", "##.", "..#", "##." } },
        { '6', new[] { ".##", "#..", "###", "#.#", "###" } },
        { '7', new[] { "###", "..#", ".#.", ".#.", ".#." } },
        { '8', new[] { "###", "#.#", "###", "#.#", "###" } },
        { '9', new[] { "###", "#.#", "###", "..#", "##." } },

        // Punctuation and symbols.
        { ' ', new[] { "..", "..", "..", "..", ".." } },
        { '.', new[] { ".", ".", ".", ".", "#" } },
        { ',', new[] { ".", ".", ".", ".", "#", "#" } },
        { ':', new[] { ".", "#", ".", "#", "." } },
        { ';', new[] { ".", "#", ".", "#", "#" } },
        { '!', new[] { "#", "#", "#", ".", "#" } },
        { '?', new[] { "##.", "..#", ".#.", "...", ".#." } },
        { '\'', new[] { "#", "#", ".", ".", "." } },
        { '"', new[] { "#.#", "#.#", "...", "...", "..." } },
        { '-', new[] { "..", "..", "##", "..", ".." } },
        { '+', new[] { "...", ".#.", "###", ".#.", "..." } },
        { '=', new[] { "...", "###", "...", "###", "..." } },
        { '*', new[] { "...", "#.#", ".#.", "#.#", "..." } },
        { '/', new[] { "..#", "..#", ".#.", "#..", "#.." } },
        { '\\', new[] { "#..", "#..", ".#.", "..#", "..#" } },
        { '_', new[] { "...", "...", "...", "...", "###" } },
        { '|', new[] { "#", "#", "#", "#", "#" } },
        { '(', new[] { ".#", "#.", "#.", "#.", ".#" } },
        { ')', new[] { "#.", ".#", ".#", ".#", "#." } },
        { '[', new[] { "##", "#.", "#.", "#.", "##" } },
        { ']', new[] { "##", ".#", ".#", ".#", "##" } },
        { '<', new[] { "..#", ".#.", "#..", ".#.", "..#" } },
        { '>', new[] { "#..", ".#.", "..#", ".#.", "#.." } },
        { '%', new[] { "#.#", "..#", ".#.", "#..", "#.#" } },
        { '&', new[] { ".#..", "#.#.", ".#.#", "#.#.", ".#.#" } },
        { '#', new[] { ".#.#.", "#####", ".#.#.", "#####", ".#.#." } },
        { '@', new[] { ".##.", "#..#", "#.##", "#...", ".##." } },
        { '$', new[] { ".##", "##.", ".#.", ".##", "##." } },
        { '^', new[] { ".#.", "#.#", "...", "...", "..." } },
        { '~', new[] { "....", ".#.#", "#.#.", "....", "...." } },
    };

    private static Texture2D atlas;
    private static readonly Dictionary<char, RectInt> glyphRects = new Dictionary<char, RectInt>();

    // Point-filtered atlas holding every glyph in white; tint with GUI.color.
    public static Texture2D Atlas
    {
        get
        {
            EnsureAtlas();
            return atlas;
        }
    }

    public static IEnumerable<char> Characters => Glyphs.Keys;

    public static bool HasGlyph(char c) => Glyphs.ContainsKey(c);

    // Width in font pixels of the widest line.
    public static int Measure(string text)
    {
        int widest = 0;
        int width = 0;
        bool lineHasGlyph = false;
        foreach (char c in text)
        {
            if (c == '\n')
            {
                widest = Mathf.Max(widest, lineHasGlyph ? width - Spacing : 0);
                width = 0;
                lineHasGlyph = false;
                continue;
            }
            width += GlyphWidth(c) + Spacing;
            lineHasGlyph = true;
        }
        return Mathf.Max(widest, lineHasGlyph ? width - Spacing : 0);
    }

    // Height in font pixels, from the top of the first line to the bottom of the
    // last line's descenders.
    public static int MeasureHeight(string text)
    {
        int lines = 1;
        foreach (char c in text)
        {
            if (c == '\n') lines++;
        }
        return (lines - 1) * LineHeight + Height;
    }

    // Splits text into lines no wider than maxWidth font pixels, breaking at spaces
    // and existing newlines. A single word wider than maxWidth gets its own line.
    public static List<string> Wrap(string text, int maxWidth)
    {
        var lines = new List<string>();
        foreach (string paragraph in text.Split('\n'))
        {
            string line = "";
            foreach (string word in paragraph.Split(' '))
            {
                string candidate = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && Measure(candidate) > maxWidth)
                {
                    lines.Add(line);
                    line = word;
                }
                else
                {
                    line = candidate;
                }
            }
            lines.Add(line);
        }
        return lines;
    }

    // Draws in GUI units under the current GUI.matrix. (x, y) is the top-left of the
    // first line's cap height. Supports '\n'.
    public static void Draw(float x, float y, string text, Color color)
    {
        if (Event.current != null && Event.current.type != EventType.Repaint)
        {
            return;
        }

        EnsureAtlas();
        Color previous = GUI.color;
        GUI.color = previous * color;
        float atlasWidth = atlas.width;
        float atlasHeight = atlas.height;
        float penX = x;
        foreach (char c in text)
        {
            if (c == '\n')
            {
                penX = x;
                y += LineHeight;
                continue;
            }

            RectInt glyph = GlyphRect(c);
            var uv = new Rect(glyph.x / atlasWidth, 0f, glyph.width / atlasWidth, glyph.height / atlasHeight);
            GUI.DrawTextureWithTexCoords(new Rect(penX, y, glyph.width, glyph.height), atlas, uv);
            penX += glyph.width + Spacing;
        }
        GUI.color = previous;
    }

    // Same as Draw, with a hard one-pixel drop shadow down and to the right.
    public static void Draw(float x, float y, string text, Color color, Color shadow)
    {
        if (shadow.a > 0f)
        {
            Draw(x + 1f, y + 1f, text, shadow);
        }
        Draw(x, y, text, color);
    }

    // Screen-space convenience: draws at a screen position (IMGUI top-left origin)
    // with each font pixel as a pixelSize x pixelSize block.
    public static void Draw(Vector2 screenPosition, string text, int pixelSize, Color color, Color shadow = default)
    {
        Matrix4x4 previous = GUI.matrix;
        int size = Mathf.Max(1, pixelSize);
        var origin = new Vector3(Mathf.Round(screenPosition.x), Mathf.Round(screenPosition.y), 0f);
        GUI.matrix = previous * Matrix4x4.TRS(origin, Quaternion.identity, new Vector3(size, size, 1f));
        Draw(0f, 0f, text, color, shadow);
        GUI.matrix = previous;
    }

    private static int GlyphWidth(char c)
    {
        string[] rows;
        return Glyphs.TryGetValue(c, out rows) ? rows[0].Length : Glyphs['?'][0].Length;
    }

    // Glyph rows top-down ('#' = ink), 5-7 rows. Unknown characters map to '?'.
    public static string[] GetRows(char c)
    {
        string[] rows;
        return Glyphs.TryGetValue(c, out rows) ? rows : Glyphs['?'];
    }

    // Glyph's cell in Atlas, in atlas pixels (full 7-row height). Unknown characters map to '?'.
    public static RectInt GetGlyph(char c)
    {
        EnsureAtlas();
        return GlyphRect(c);
    }

    private static RectInt GlyphRect(char c)
    {
        RectInt rect;
        return glyphRects.TryGetValue(c, out rect) ? rect : glyphRects['?'];
    }

    private static void EnsureAtlas()
    {
        if (atlas != null)
        {
            return;
        }

        glyphRects.Clear();
        int width = 0;
        foreach (var glyph in Glyphs.Values)
        {
            width += glyph[0].Length + 1;
        }

        atlas = new Texture2D(width, Height, GraphicsFormat.R8G8B8A8_SRGB, TextureCreationFlags.None)
        {
            name = "pixel_font",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave,
        };

        var pixels = new Color32[width * Height];
        int x = 0;
        foreach (var pair in Glyphs)
        {
            string[] rows = pair.Value;
            int glyphWidth = rows[0].Length;
            for (int row = 0; row < rows.Length && row < Height; row++)
            {
                for (int col = 0; col < glyphWidth; col++)
                {
                    if (rows[row][col] == '#')
                    {
                        // Texture rows count from the bottom; glyph rows are written top-down.
                        pixels[(Height - 1 - row) * width + x + col] = new Color32(255, 255, 255, 255);
                    }
                }
            }
            glyphRects[pair.Key] = new RectInt(x, 0, glyphWidth, Height);
            x += glyphWidth + 1;
        }
        atlas.SetPixels32(pixels);
        atlas.Apply(false, true);
    }
}
