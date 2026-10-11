using System.Collections.Generic;
using UnityEngine;

// Hand-drawn pixel art for the gravel sidewalk, in the style of Minecraft gravel: grey
// stones packed edge to edge. Each map row is one pixel row, top to bottom; each character
// is a palette entry. The tile wraps on both axes, so stones that run off one edge continue
// on the opposite one. Light comes from the top-left: a stone's top/left rim uses its light
// shade, the middle its base, the bottom/right its dark shade, with dark gaps between stones.
// Edit the map directly; the island map reimports with the new art.
public static class SidewalkGravelArt
{
    private static readonly Dictionary<char, Color32> Palette = new Dictionary<char, Color32>
    {
        // Gaps between stones.
        { ':', new Color32(50, 48, 48, 255) },
        { '.', new Color32(64, 62, 62, 255) },
        // Each stone family: dark, base, light.
        // Mid grey (most stones).
        { 'a', new Color32(92, 89, 88, 255) },
        { 'b', new Color32(122, 119, 118, 255) },
        { 'c', new Color32(152, 149, 148, 255) },
        // Pale grey.
        { 'd', new Color32(108, 105, 104, 255) },
        { 'e', new Color32(140, 137, 136, 255) },
        { 'f', new Color32(172, 169, 168, 255) },
        // Dark grey.
        { 'g', new Color32(76, 74, 74, 255) },
        { 'h', new Color32(100, 97, 96, 255) },
        { 'i', new Color32(126, 123, 122, 255) },
        // Faintly warm grey, the odd pinkish stone.
        { 'j', new Color32(98, 91, 88, 255) },
        { 'k', new Color32(130, 122, 118, 255) },
        { 'l', new Color32(158, 150, 145, 255) },
    };

    // 32 x 32, one tile. At the material's default tiling it covers 2 x 2 m (16 px per metre).
    public static readonly string[] Gravel =
    {
        ":gcbba:.feee:iihhgllkkjcbagg:aa.",
        "ig.ba:cadeedgihhhglkkkjcbb:ccagi",
        "hhg.:ccbadd:a.ghg:lkkjjcbacbbbai",
        "hg:accabba:cccag:djkjj:aa:cbca:g",
        ".:ca.bbba:acbbaffed..:ccaabba:ca",
        "accca.a:ccacbb:feefdcccbb:.:ccbb",
        "accbbaccbba..afeedd:.cbbaca.cbbb",
        ":a.caacbbbalj:feed:igabb:ccacbba",
        "iigaaacbba:lkjddd:iihg.:ccba.baa",
        "ihhga:aba:lklk:d:gihhgca.bcb:a.:",
        "hhig:lj.:j.kkjfffdihh:cba.a:ccag",
        "ggg:llkjccaj.:ffed.g:cbbbaccbbb:",
        "ig:llkkjccbcaffeedllj.bba:aabb:i",
        "hhglkjkjcbbbad.de:llkj.a:iiga:ii",
        "hhg.jjj:.bba:llj:llkkkj:iihhgiih",
        "hg:caiiigaa:llkkjlklkjjgihhh:.hh",
        "ggccaiihhigdlkkkj.kkj.:a.hh:ig.i",
        "g:cbaihhhg:d.kk.:j.j:dccag:ihggg",
        "caabaghhggfed.:llkjffdcbbaiihhg:",
        "bbaa:gggg:feedllkjjded.baa..gg:c",
        "aa:iihg:ljddddlklkjj.:daaaccccaa",
        "ffdghhgllkljd:.jj..:ffffd:ccbbba",
        "feed.hglkkk:igiiiiigffeeedcbbba:",
        ".ee:a.:jkk:iig.hhhggfeeedd.bbaa.",
        "ad:caaigj:iihhgihgg:deedd:jaaa:c",
        "baabbaiiigihhhgggg:fdd.d:lkj:ccc",
        "cbaca:iihg.hhgg:i:ffdfd:lkkkjccb",
        "baacaiihihgig.:a:dfeeedgjkkj:a.b",
        "a.:a:ghhgggg:ccb:g.eed:igj:igcaa",
        "cacccaghg.:ccbbaiigdd:ihh:ihgccc",
        "baccbbag:fdabbb:.hhg:ghh:iihgcbb",
        "a:cbaba:ffedaa:iggg:lj.:aghhgcbb",
    };

    // Changes whenever the map or palette changes, so the importer knows to rebuild.
    public static uint ContentHash()
    {
        uint hash = 2166136261;
        foreach (string row in Gravel)
        {
            foreach (char ch in row)
            {
                hash = (hash ^ ch) * 16777619;
            }
        }
        foreach (KeyValuePair<char, Color32> entry in Palette)
        {
            hash = (hash ^ entry.Key) * 16777619;
            hash = (hash ^ (uint)(entry.Value.r | entry.Value.g << 8 | entry.Value.b << 16)) * 16777619;
        }
        return hash;
    }

    // Repeating, point-filtered texture. Mipmaps keep the far-away path from crawling.
    public static Texture2D Build(string name)
    {
        int height = Gravel.Length;
        int width = Gravel[0].Length;
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, true, false)
        {
            name = name,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Repeat,
        };
        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
        {
            string row = Gravel[y];
            for (int x = 0; x < width; x++)
            {
                char key = x < row.Length ? row[x] : '.';
                Color32 color;
                if (!Palette.TryGetValue(key, out color))
                {
                    color = new Color32(255, 0, 255, 255);
                }
                // Texture rows count from the bottom; maps are written top-down.
                pixels[(height - 1 - y) * width + x] = color;
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(true, false);
        return texture;
    }

    // How far each stone bulges: stones are domes that rise with distance from the nearest
    // gap pixel, pale stones sit a little proud and dark ones a little sunk. Gaps are lowest.
    private const float DomeRadius = 2.5f;

    // Height of every texel in 0..1, rows bottom-up like the textures.
    public static float[,] HeightField()
    {
        int height = Gravel.Length;
        int width = Gravel[0].Length;
        var gaps = new List<Vector2Int>();
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (IsGap(At(x, y))) gaps.Add(new Vector2Int(x, y));
            }
        }

        var field = new float[width, height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                char key = At(x, y);
                float h;
                if (key == ':') h = 0f;
                else if (key == '.') h = 0.12f;
                else
                {
                    // Distance to the nearest gap, wrapping like the tile does.
                    float best = float.MaxValue;
                    foreach (Vector2Int g in gaps)
                    {
                        float dx = Mathf.Abs(g.x - x);
                        float dy = Mathf.Abs(g.y - y);
                        dx = Mathf.Min(dx, width - dx);
                        dy = Mathf.Min(dy, height - dy);
                        best = Mathf.Min(best, dx * dx + dy * dy);
                    }
                    h = 0.3f + 0.7f * Mathf.SmoothStep(0f, 1f, Mathf.Sqrt(best) / DomeRadius);
                    if ("def".IndexOf(key) >= 0) h += 0.08f;
                    else if ("ghi".IndexOf(key) >= 0) h -= 0.08f;
                }
                field[x, height - 1 - y] = Mathf.Clamp01(h);
            }
        }
        return field;
    }

    // Tangent-space normal map from the height field (x = right, y = up the texture).
    public static Texture2D BuildNormalMap(string name, float strength)
    {
        float[,] field = HeightField();
        int width = field.GetLength(0);
        int height = field.GetLength(1);
        var texture = NewLinear(name, width, height);
        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float dx = field[(x + 1) % width, y] - field[(x + width - 1) % width, y];
                float dy = field[x, (y + 1) % height] - field[x, (y + height - 1) % height];
                Vector3 n = new Vector3(-dx * strength, -dy * strength, 1f).normalized;
                pixels[y * width + x] = new Color32(Encode(n.x), Encode(n.y), Encode(n.z), 255);
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(true, false);
        return texture;
    }

    // Height map for URP's parallax (read from the green channel).
    public static Texture2D BuildHeightMap(string name)
    {
        float[,] field = HeightField();
        int width = field.GetLength(0);
        int height = field.GetLength(1);
        var texture = NewLinear(name, width, height);
        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                byte h = (byte)Mathf.RoundToInt(field[x, y] * 255f);
                pixels[y * width + x] = new Color32(h, h, h, 255);
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(true, false);
        return texture;
    }

    private static char At(int x, int y)
    {
        string row = Gravel[y];
        return x < row.Length ? row[x] : '.';
    }

    private static bool IsGap(char key) => key == ':' || key == '.';

    private static byte Encode(float v) => (byte)Mathf.RoundToInt(Mathf.Clamp01(v * 0.5f + 0.5f) * 255f);

    private static Texture2D NewLinear(string name, int width, int height)
    {
        return new Texture2D(width, height, TextureFormat.RGBA32, true, true)
        {
            name = name,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Repeat,
        };
    }
}
