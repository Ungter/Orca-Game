using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Pixel art for the clue letter's potion hunt: the island map drawn on the letter from the
// ClueMap survey, the potions, the crayon trail and its marks, the glow round the alchemist's
// house, and the meshes of the light walls and potion beams standing on the island.
public static class ClueMapArt
{
    public const int LetterWidth = 384;
    public const int LetterHeight = 240;
    // The map's frame on Letter_screen.png, in letter pixels from the top-left (its blank paper
    // spans x 6-359, y 12-196); the status line is written under it.
    public static readonly RectInt MapArea = new RectInt(14, 16, 336, 158);
    public const float StatusY = 185f;
    // A second line under the status, on the paper's shaded foot.
    public const float PromptY = 199f;

    // Muted pigments, the same as the paper board's potions had.
    public static readonly Color32[] PotionColors =
    {
        new Color32(176, 52, 38, 255),   // vermilion
        new Color32(204, 114, 40, 255),  // orange ochre
        new Color32(218, 174, 58, 255),  // saffron
        new Color32(70, 132, 84, 255),   // verdigris
        new Color32(44, 80, 154, 255),   // lapis
    };

    public static readonly Color32 Ink = new Color32(52, 36, 24, 255);
    public static readonly Color32 Crayon = new Color32(178, 34, 46, 255);

    private static readonly Color32 Sea = new Color32(146, 172, 178, 255);
    private static readonly Color32 SeaInk = new Color32(100, 128, 144, 255);
    private static readonly Color32 Grass = new Color32(168, 180, 112, 255);
    private static readonly Color32 PathColor = new Color32(206, 178, 124, 255);
    private static readonly Color32 PathEdge = new Color32(150, 118, 78, 255);
    private static readonly Color32 Wood = new Color32(146, 104, 62, 255);
    private static readonly Color32 Leaves = new Color32(78, 106, 60, 255);
    private static readonly Color32 LeavesLight = new Color32(114, 140, 78, 255);
    private static readonly Color32 LeavesEdge = new Color32(40, 56, 32, 255);
    private static readonly Color32 Roof = new Color32(150, 84, 62, 255);
    private static readonly Color32 RoofShade = new Color32(118, 62, 46, 255);
    private static readonly Color32 Stone = new Color32(118, 108, 96, 255);
    private static readonly Color32 LampLight = new Color32(244, 214, 116, 255);
    private static readonly Color32 Cork = new Color32(150, 102, 60, 255);
    private static readonly Color32 Glass = new Color32(222, 234, 230, 255);
    private static readonly Color32 Glint = new Color32(255, 251, 238, 255);

    // Each potion is a corked flask holding its colour. Legend: o ink outline, c cork,
    // g empty glass, h glint, s liquid surface, l liquid, d liquid in shadow.
    private static readonly string[] PotionArt =
    {
        "..ooooo..",
        "..occco..",
        "...ooo...",
        "...ogo...",
        "..ohggo..",
        ".ohsssso.",
        "ohllllldo",
        "olllslldo",
        ".olllddo.",
        "..ooooo..",
    };

    // The plaza fountain, seen from above: o stone, w water, c the column.
    private static readonly string[] FountainArt =
    {
        "..ooo..",
        ".owwwo.",
        "owwcwwo",
        "owcccwo",
        "owwcwwo",
        ".owwwo.",
        "..ooo..",
    };

    public static int PotionArtWidth => PotionArt[0].Length;
    public static int PotionArtHeight => PotionArt.Length;

    // Where the map sits on the letter. Map up is world +x and map right world -z: the island
    // as it looks from above in the editor, the pier at the bottom. Turned, it is the other way
    // round (up is -x, right +z): the island as the lighthouse's lantern sees it, for its final
    // game.
    public readonly struct Frame
    {
        public readonly Vector2 Centre;
        public readonly Vector2 WorldCentre;
        public readonly float Scale;
        public readonly bool Turned;

        public Frame(Bounds island, RectInt area)
        {
            Centre = area.center;
            WorldCentre = new Vector2(island.center.x, island.center.z);
            const float margin = 1.06f;
            Scale = Mathf.Min(area.width / Mathf.Max(1f, island.size.z * margin), area.height / Mathf.Max(1f, island.size.x * margin));
            Turned = false;
        }

        private Frame(Vector2 centre, Vector2 worldCentre, float scale, bool turned)
        {
            Centre = centre;
            WorldCentre = worldCentre;
            Scale = scale;
            Turned = turned;
        }

        // The same frame turned half round about the map's middle.
        public Frame Turn() => new Frame(Centre, WorldCentre, Scale, !Turned);

        // Letter pixels from the top-left.
        public Vector2 ToLetter(Vector3 world)
        {
            var offset = new Vector2((WorldCentre.y - world.z) * Scale, -(world.x - WorldCentre.x) * Scale);
            return Centre + (Turned ? -offset : offset);
        }

        public Vector3 ToWorld(float u, float v)
        {
            var offset = new Vector2(u - Centre.x, v - Centre.y);
            if (Turned) offset = -offset;
            return new Vector3(WorldCentre.x - offset.y / Scale, 0f, WorldCentre.y - offset.x / Scale);
        }
    }

    public static Texture2D NewTexture(string name, int width, int height)
    {
        return new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = name,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave,
        };
    }

    // Texture rows run bottom-up; the letter is measured top-down.
    public static int Index(int x, int y) => (LetterHeight - 1 - y) * LetterWidth + x;

    // ---- The island map ----------------------------------------------------------------

    // The island as a hand-coloured map inside an inked frame: washes of sea, grass, paths,
    // trees, roofs and the pier, inked coast and edges, the hunt's cells as a checkerboard with
    // pen lines, the fountain and lamp posts as landmarks. Transparent outside the frame. Without
    // squares, the hunt's checkerboard is left off (the lighthouse game lays its hexes over it).
    public static Color32[] DrawMap(ClueMap map, Frame frame, Vector3? fountain, IReadOnlyList<Vector3> lamps, bool squares = true)
    {
        var pixels = new Color32[LetterWidth * LetterHeight];
        RectInt area = MapArea;
        int w = area.width, h = area.height;
        var kinds = new ClueMap.Kind[w * h];
        var cells = new Vector2Int[w * h];
        for (int v = 0; v < h; v++)
        {
            for (int u = 0; u < w; u++)
            {
                Vector3 world = frame.ToWorld(area.x + u + 0.5f, area.y + v + 0.5f);
                int sample = map.SampleAt(world);
                kinds[v * w + u] = sample < 0 ? ClueMap.Kind.Water : map.Kinds[sample];
                cells[v * w + u] = map.CellAt(world);
            }
        }

        for (int v = 0; v < h; v++)
        {
            for (int u = 0; u < w; u++)
            {
                ClueMap.Kind kind = kinds[v * w + u];
                bool land = kind != ClueMap.Kind.Water;
                ClueMap.Kind left = u > 0 ? kinds[v * w + u - 1] : kind;
                ClueMap.Kind right = u + 1 < w ? kinds[v * w + u + 1] : kind;
                ClueMap.Kind up = v > 0 ? kinds[(v - 1) * w + u] : kind;
                ClueMap.Kind down = v + 1 < h ? kinds[(v + 1) * w + u] : kind;
                bool edge = left != kind || right != kind || up != kind || down != kind;
                bool coast = land && (left == ClueMap.Kind.Water || right == ClueMap.Kind.Water ||
                    up == ClueMap.Kind.Water || down == ClueMap.Kind.Water);
                int x = area.x + u, y = area.y + v;
                float grain = 0.93f + 0.13f * Grain(x, y);
                Color32 color;
                switch (kind)
                {
                    case ClueMap.Kind.Water:
                        bool wave = (u + 2 * v) % 11 == 0 && ValueNoise(u / 6f, v / 3f, 7) > 0.45f;
                        color = wave ? SeaInk : Shade(Sea, grain);
                        break;
                    case ClueMap.Kind.Paved:
                        color = edge ? PathEdge : Shade(PathColor, grain);
                        break;
                    case ClueMap.Kind.Dock:
                        color = Shade(Wood, u % 3 == 0 ? 0.78f : grain);
                        break;
                    case ClueMap.Kind.Foliage:
                        color = edge ? LeavesEdge : Hash01(x, y, 3) > 0.82f ? LeavesLight : Shade(Leaves, grain);
                        break;
                    case ClueMap.Kind.Building:
                        color = edge ? Ink : v % 3 == 0 ? RoofShade : Shade(Roof, grain);
                        break;
                    case ClueMap.Kind.Structure:
                        color = edge ? Shade(Stone, 0.7f) : Shade(Stone, grain);
                        break;
                    default:
                        color = Shade(Grass, grain);
                        break;
                }

                if (squares && land && kind != ClueMap.Kind.Building && kind != ClueMap.Kind.Foliage)
                {
                    Vector2Int cell = cells[v * w + u];
                    if (((cell.x + cell.y) & 1) == 1) color = Shade(color, 0.9f);
                    bool gridLine = (u + 1 < w && cells[v * w + u + 1].y != cell.y) || (v + 1 < h && cells[(v + 1) * w + u].x != cell.x);
                    if (gridLine) color = Color32.Lerp(color, Ink, 0.35f + 0.2f * ValueNoise(u / 9f, v / 9f, 11));
                }
                if (coast) color = Ink;
                // Washes let the paper show through a little; ink is solid.
                color.a = color.Equals(Ink) ? (byte)255 : (byte)226;
                pixels[Index(x, y)] = color;
            }
        }

        if (fountain.HasValue) Stamp(pixels, FountainArt, frame.ToLetter(fountain.Value), 'o', Ink, 'w', Sea, 'c', Stone);
        if (lamps != null)
        {
            foreach (Vector3 lamp in lamps)
            {
                Vector2 p = frame.ToLetter(lamp);
                int lx = Mathf.RoundToInt(p.x), ly = Mathf.RoundToInt(p.y);
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        Put(pixels, lx + dx, ly + dy, dx == 0 && dy == 0 ? LampLight : Ink, area);
                    }
                }
            }
        }

        // A wobbling pen frame round the map.
        for (int u = -2; u < w + 2; u++)
        {
            for (int t = 0; t < 2; t++)
            {
                Put(pixels, area.x + u, area.y - 1 - t, Ink, null);
                Put(pixels, area.x + u, area.yMax + t, Ink, null);
            }
        }
        for (int v = 0; v < h; v++)
        {
            for (int t = 0; t < 2; t++)
            {
                Put(pixels, area.x - 1 - t, area.y + v, Ink, null);
                Put(pixels, area.xMax + t, area.y + v, Ink, null);
            }
        }
        return pixels;
    }

    // ---- Marks on the map --------------------------------------------------------------

    // A potion drawn on the map at one letter pixel per art pixel, centred on at.
    public static void DrawPotion(Color32[] pixels, Vector2 at, Color32 color)
    {
        int x0 = Mathf.RoundToInt(at.x) - PotionArt[0].Length / 2;
        int y0 = Mathf.RoundToInt(at.y) - PotionArt.Length / 2;
        for (int row = 0; row < PotionArt.Length; row++)
        {
            for (int col = 0; col < PotionArt[row].Length; col++)
            {
                char c = PotionArt[row][col];
                if (c != '.') Put(pixels, x0 + col, y0 + row, PotionPixel(c, color, row), null);
            }
        }
    }

    // A crayon cross where a potion was collected.
    public static void DrawCross(Color32[] pixels, Vector2 at)
    {
        int cx = Mathf.RoundToInt(at.x), cy = Mathf.RoundToInt(at.y);
        for (int d = -3; d <= 3; d++)
        {
            Put(pixels, cx + d, cy + d, Crayon, null);
            Put(pixels, cx + d, cy - d, Crayon, null);
            Put(pixels, cx + d + 1, cy + d, Crayon, null);
            Put(pixels, cx + d + 1, cy - d, Crayon, null);
        }
    }

    // An inked ring where the hunt started.
    public static void DrawRing(Color32[] pixels, Vector2 at, int radius, Color32 color)
    {
        int cx = Mathf.RoundToInt(at.x), cy = Mathf.RoundToInt(at.y);
        for (int dy = -radius - 1; dy <= radius + 1; dy++)
        {
            for (int dx = -radius - 1; dx <= radius + 1; dx++)
            {
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                if (Mathf.Abs(d - radius) < 0.6f) Put(pixels, cx + dx, cy + dy, color, null);
            }
        }
    }

    // A waxy crayon dab: a rough disc with gaps where the paper's tooth stops the wax.
    public static void StampCrayon(Color32[] pixels, Vector2 at)
    {
        const float radius = 1.4f;
        int x0 = Mathf.FloorToInt(at.x - radius), x1 = Mathf.CeilToInt(at.x + radius);
        int y0 = Mathf.FloorToInt(at.y - radius), y1 = Mathf.CeilToInt(at.y + radius);
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                if (x < 0 || y < 0 || x >= LetterWidth || y >= LetterHeight) continue;
                float dx = x + 0.5f - at.x, dy = y + 0.5f - at.y;
                if (dx * dx + dy * dy > radius * radius) continue;
                float tooth = Hash01(x, y, 29);
                if (tooth < 0.18f) continue;
                Color32 wax = Crayon;
                wax.a = (byte)(170 + 85 * tooth);
                int i = Index(x, y);
                if (wax.a > pixels[i].a) pixels[i] = wax;
            }
        }
    }

    // ---- Textures ----------------------------------------------------------------------

    // A potion as a texture, scale texels per art pixel, with transparent corners.
    public static Texture2D PotionTexture(Color32 color, int scale, string name)
    {
        int w = PotionArt[0].Length * scale, h = PotionArt.Length * scale;
        var texels = new Color32[w * h];
        for (int row = 0; row < PotionArt.Length; row++)
        {
            for (int col = 0; col < PotionArt[row].Length; col++)
            {
                char c = PotionArt[row][col];
                if (c == '.') continue;
                Color32 pixel = PotionPixel(c, color, row);
                for (int y = 0; y < scale; y++)
                {
                    for (int x = 0; x < scale; x++)
                    {
                        texels[(h - 1 - (row * scale + y)) * w + col * scale + x] = pixel;
                    }
                }
            }
        }
        Texture2D texture = NewTexture(name, w, h);
        texture.SetPixels32(texels);
        texture.Apply();
        return texture;
    }

    // A soft round glow, white, for tinting.
    public static Texture2D GlowTexture()
    {
        const int size = 48;
        var texels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                float fall = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                texels[y * size + x] = new Color32(255, 255, 255, (byte)(255f * fall * fall));
            }
        }
        Texture2D texture = NewTexture("Clue glow", size, size);
        texture.filterMode = FilterMode.Bilinear;
        texture.SetPixels32(texels);
        texture.Apply();
        return texture;
    }

    // "You are here": a crayon ring round a dot.
    public static Texture2D MarkerTexture()
    {
        const int size = 11;
        var texels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - 5f, dy = y - 5f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                bool ring = Mathf.Abs(d - 4.2f) < 0.75f;
                bool dot = d < 1.6f;
                if (ring || dot) texels[y * size + x] = dot ? Ink : Crayon;
            }
        }
        Texture2D texture = NewTexture("Clue marker", size, size);
        texture.SetPixels32(texels);
        texture.Apply();
        return texture;
    }

    // Plain parchment, for when the letter has no artwork of its own.
    public static Sprite PaperSprite()
    {
        var texels = new Color32[LetterWidth * LetterHeight];
        for (int y = 0; y < LetterHeight; y++)
        {
            for (int x = 0; x < LetterWidth; x++)
            {
                bool border = x < 3 || y < 3 || x >= LetterWidth - 3 || y >= LetterHeight - 3;
                texels[y * LetterWidth + x] = border ? new Color32(150, 120, 84, 255) : Shade(new Color32(232, 218, 182, 255), 0.95f + 0.07f * Grain(x, y));
            }
        }
        Texture2D texture = NewTexture("Clue paper", LetterWidth, LetterHeight);
        texture.SetPixels32(texels);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, LetterWidth, LetterHeight), new Vector2(0.5f, 0.5f), 100f);
    }

    // ---- Meshes on the island ----------------------------------------------------------

    // Light walls standing on every cell border that crosses walkable land: quads following the
    // ground, UV y 0 at the foot and 1 at the top, for the Orca/Clue Grid Light shader. UV x is
    // 0.5 (no soft sides: a wall runs on into the next); UV1 x says which way the border runs
    // (0: constant x, 1: constant z), so the shader can tell which cells it parts.
    public static Mesh GridWalls(ClueMap map, float height)
    {
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var sides = new List<Vector2>();
        var colors = new List<Color32>();
        var indices = new List<int>();
        int s = ClueMap.SamplesPerCell;
        // Borders of constant x, running along z.
        for (int k = 0; k <= map.CellsX; k++)
        {
            float x = map.Origin.x + k * map.CellSize;
            int previous = -1;
            for (int sz = 0; sz < map.SamplesZ; sz++)
            {
                if (!map.BorderHeight(map.Sample(k * s - 1, sz), map.Sample(k * s, sz), out float y))
                {
                    previous = -1;
                    continue;
                }
                float z = map.Origin.y + (sz + 0.5f) * map.SampleSize;
                previous = AddPost(vertices, uvs, sides, colors, indices, new Vector3(x, y, z), height, 0f, previous);
            }
        }
        // Borders of constant z, running along x.
        for (int k = 0; k <= map.CellsZ; k++)
        {
            float z = map.Origin.y + k * map.CellSize;
            int previous = -1;
            for (int sx = 0; sx < map.SamplesX; sx++)
            {
                if (!map.BorderHeight(map.Sample(sx, k * s - 1), map.Sample(sx, k * s), out float y))
                {
                    previous = -1;
                    continue;
                }
                float x = map.Origin.x + (sx + 0.5f) * map.SampleSize;
                previous = AddPost(vertices, uvs, sides, colors, indices, new Vector3(x, y, z), height, 1f, previous);
            }
        }
        var mesh = new Mesh { name = "Clue grid walls", hideFlags = HideFlags.HideAndDontSave };
        mesh.indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetUVs(1, sides);
        mesh.SetColors(colors);
        mesh.SetTriangles(indices, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    // A wall post: a foot just under the ground and a top at height, joined to the previous post.
    private static int AddPost(List<Vector3> vertices, List<Vector2> uvs, List<Vector2> sides, List<Color32> colors,
        List<int> indices, Vector3 foot, float height, float side, int previous)
    {
        int i = vertices.Count;
        vertices.Add(foot + Vector3.down * 0.15f);
        vertices.Add(foot + Vector3.up * height);
        uvs.Add(new Vector2(0.5f, 0f));
        uvs.Add(new Vector2(0.5f, 1f));
        sides.Add(new Vector2(side, 0f));
        sides.Add(new Vector2(side, 0f));
        colors.Add(new Color32(255, 255, 255, 255));
        colors.Add(new Color32(255, 255, 255, 255));
        if (previous >= 0)
        {
            indices.Add(previous);
            indices.Add(previous + 1);
            indices.Add(i + 1);
            indices.Add(previous);
            indices.Add(i + 1);
            indices.Add(i);
        }
        return i;
    }

    // A beam of light over a potion: two crossed vertical cards in its colour, soft at the sides.
    public static Mesh Beam(Color32 color, float width, float height)
    {
        var mesh = new Mesh { name = "Clue potion beam", hideFlags = HideFlags.HideAndDontSave };
        float half = width * 0.5f;
        mesh.vertices = new[]
        {
            new Vector3(-half, 0f, 0f), new Vector3(half, 0f, 0f), new Vector3(-half, height, 0f), new Vector3(half, height, 0f),
            new Vector3(0f, 0f, -half), new Vector3(0f, 0f, half), new Vector3(0f, height, -half), new Vector3(0f, height, half),
        };
        mesh.uv = new[]
        {
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f),
        };
        mesh.colors32 = new[] { color, color, color, color, color, color, color, color };
        mesh.triangles = new[] { 0, 2, 3, 0, 3, 1, 4, 6, 7, 4, 7, 5 };
        mesh.RecalculateBounds();
        return mesh;
    }

    // ---- Helpers -----------------------------------------------------------------------

    private static Color32 PotionPixel(char c, Color32 color, int row)
    {
        // The liquid darkens a little toward the bottom of the flask.
        float depth = 1.08f - 0.14f * row / (PotionArt.Length - 1);
        switch (c)
        {
            case 'o': return Ink;
            case 'c': return Cork;
            case 'g': return Glass;
            case 'h': return Glint;
            case 's': return Shade(Color32.Lerp(color, Glint, 0.35f), 1f);
            case 'l': return Shade(color, depth);
            default: return Shade(color, 0.68f);
        }
    }

    private static void Stamp(Color32[] pixels, string[] art, Vector2 centre, char a, Color32 colorA, char b, Color32 colorB, char c, Color32 colorC)
    {
        int x0 = Mathf.RoundToInt(centre.x) - art[0].Length / 2;
        int y0 = Mathf.RoundToInt(centre.y) - art.Length / 2;
        for (int row = 0; row < art.Length; row++)
        {
            for (int col = 0; col < art[row].Length; col++)
            {
                char key = art[row][col];
                if (key == a) Put(pixels, x0 + col, y0 + row, colorA, MapArea);
                else if (key == b) Put(pixels, x0 + col, y0 + row, colorB, MapArea);
                else if (key == c) Put(pixels, x0 + col, y0 + row, colorC, MapArea);
            }
        }
    }

    // One solid letter pixel, kept inside clip when given.
    private static void Put(Color32[] pixels, int x, int y, Color32 color, RectInt? clip)
    {
        if (x < 0 || y < 0 || x >= LetterWidth || y >= LetterHeight) return;
        if (clip.HasValue && !clip.Value.Contains(new Vector2Int(x, y))) return;
        pixels[Index(x, y)] = color;
    }

    public static Color32 Shade(Color32 color, float light)
    {
        return new Color32(
            (byte)Mathf.Clamp(color.r * light, 0f, 255f),
            (byte)Mathf.Clamp(color.g * light, 0f, 255f),
            (byte)Mathf.Clamp(color.b * light, 0f, 255f), color.a);
    }

    // Paper grain, 0..1: soft blotches, finer mottling and horizontal fibres.
    private static float Grain(int x, int y)
    {
        return Mathf.Clamp01(ValueNoise(x / 30f, y / 30f, 1) * 0.45f + ValueNoise(x / 7f, y / 7f, 2) * 0.3f +
            ValueNoise(x / 12f, y / 1.5f, 3) * 0.25f);
    }

    private static float ValueNoise(float x, float y, int seed)
    {
        int xi = Mathf.FloorToInt(x);
        int yi = Mathf.FloorToInt(y);
        float fx = x - xi;
        float fy = y - yi;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);
        float top = Mathf.Lerp(Hash01(xi, yi, seed), Hash01(xi + 1, yi, seed), fx);
        float bottom = Mathf.Lerp(Hash01(xi, yi + 1, seed), Hash01(xi + 1, yi + 1, seed), fx);
        return Mathf.Lerp(top, bottom, fy);
    }

    private static float Hash01(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 982451653);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return h / (float)uint.MaxValue;
        }
    }
}
