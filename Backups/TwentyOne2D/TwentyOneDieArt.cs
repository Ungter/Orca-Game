using UnityEngine;

// A small three-sided selector die, in the same cream/brown palette as the lost dice.
public static class TwentyOneDieArt
{
    public static Texture2D Create(int face)
    {
        if (face < 1 || face > 3) throw new System.ArgumentOutOfRangeException(nameof(face));
        var texture = new Texture2D(32, 32, TextureFormat.RGBA32, false)
        {
            name = "Counting die " + face,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        var pixels = new Color32[32 * 32];
        Color32 ink = new Color32(60, 39, 29, 255);
        Color32 paper = new Color32(250, 235, 191, 255);
        Color32 shadow = new Color32(177, 139, 82, 255);
        for (int y = 4; y < 28; y++)
        for (int x = 4; x < 28; x++)
        {
            if ((x == 4 || x == 27) && (y == 4 || y == 27)) continue;
            bool edge = x <= 5 || x >= 26 || y <= 5 || y >= 26;
            pixels[y * 32 + x] = edge ? ink : x >= 23 || y <= 8 ? shadow : paper;
        }
        if (face != 2) Pip(pixels, 15, 16, ink);
        if (face != 1)
        {
            Pip(pixels, 10, 21, ink);
            Pip(pixels, 20, 11, ink);
        }
        texture.SetPixels32(pixels);
        texture.Apply();
        return texture;
    }

    private static void Pip(Color32[] pixels, int x, int y, Color32 ink)
    {
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++) pixels[(y + dy) * 32 + x + dx] = ink;
    }
}
