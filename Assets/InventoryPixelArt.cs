using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

// Hand-drawn pixel art for the inventory HUD. Each map row is one pixel row,
// top to bottom; each character is a palette entry ('.' is transparent).
// Edit the maps directly - textures are rebuilt from them at runtime with point
// filtering, so every art pixel lands on an exact block of screen pixels.
public static class InventoryPixelArt
{
    private static readonly Dictionary<char, Color32> Palette = new Dictionary<char, Color32>
    {
        { '.', new Color32(0, 0, 0, 0) },
        // Outline and wood.
        { 'K', new Color32(26, 15, 10, 255) },
        { 'D', new Color32(59, 36, 22, 255) },
        { 'W', new Color32(92, 58, 33, 255) },
        { 'L', new Color32(122, 82, 48, 255) },
        { 'H', new Color32(148, 104, 61, 255) },
        // Iron studs.
        { 'I', new Color32(58, 61, 68, 255) },
        { 'i', new Color32(107, 111, 120, 255) },
        { 's', new Color32(168, 172, 180, 255) },
        // Gold studs.
        { 'G', new Color32(138, 90, 20, 255) },
        { 'g', new Color32(212, 160, 42, 255) },
        { 'y', new Color32(242, 211, 107, 255) },
        // Slot wells: shadow, empty, filled.
        { 'B', new Color32(14, 9, 7, 235) },
        { 'b', new Color32(28, 20, 15, 215) },
        { 'c', new Color32(44, 30, 19, 225) },
        // Parchment.
        { 'P', new Color32(233, 214, 168, 255) },
        { 'p', new Color32(200, 168, 114, 255) },
        { 'q', new Color32(154, 122, 72, 255) },
    };

    // Wooden slot frame with iron corner studs. 9-slice: left 6, right 5, top 6, bottom 5.
    public static readonly string[] Slot =
    {
        ".KKKKKKKKKKKKKKKKKK.",
        "KssiKHHHHHHHHHHKssiK",
        "KsiIKLLLLLLLLLLKsiIK",
        "KiIIKWWWWWWWWWWKiIIK",
        "KKKKKKKKKKKKKKKKKKKK",
        "KHLWKBBBBBBBBBBKLWDK",
        "KHLWKBbbbbbbbbbKLWDK",
        "KHLWKBbbbbbbbbbKLWDK",
        "KHLWKBbbbbbbbbbKLWDK",
        "KHLWKBbbbbbbbbbKLWDK",
        "KHLWKBbbbbbbbbbKLWDK",
        "KHLWKBbbbbbbbbbKLWDK",
        "KHLWKBbbbbbbbbbKLWDK",
        "KHLWKBbbbbbbbbbKLWDK",
        "KHLWKBbbbbbbbbbKLWDK",
        "KKKKKKKKKKKKKKKKKKKK",
        "KssiKLLLLLLLLLLKssiK",
        "KsiIKWWWWWWWWWWKsiIK",
        "KiIIKDDDDDDDDDDKiIIK",
        ".KKKKKKKKKKKKKKKKKK.",
    };

    // Same frame for a slot holding an item: gold studs and a warmer well.
    public static readonly string[] SlotFilled =
    {
        ".KKKKKKKKKKKKKKKKKK.",
        "KyygKHHHHHHHHHHKyygK",
        "KygGKLLLLLLLLLLKygGK",
        "KgGGKWWWWWWWWWWKgGGK",
        "KKKKKKKKKKKKKKKKKKKK",
        "KHLWKBBBBBBBBBBKLWDK",
        "KHLWKBcccccccccKLWDK",
        "KHLWKBcccccccccKLWDK",
        "KHLWKBcccccccccKLWDK",
        "KHLWKBcccccccccKLWDK",
        "KHLWKBcccccccccKLWDK",
        "KHLWKBcccccccccKLWDK",
        "KHLWKBcccccccccKLWDK",
        "KHLWKBcccccccccKLWDK",
        "KHLWKBcccccccccKLWDK",
        "KKKKKKKKKKKKKKKKKKKK",
        "KyygKLLLLLLLLLLKyygK",
        "KygGKWWWWWWWWWWKygGK",
        "KgGGKDDDDDDDDDDKgGGK",
        ".KKKKKKKKKKKKKKKKKK.",
    };

    public static readonly RectOffset SlotBorder = new RectOffset(6, 5, 6, 5);

    // Parchment scroll for the header, rolled at both ends. 9-slice: left 5, right 5.
    public static readonly string[] Banner =
    {
        ".KKK....KKK.",
        "KPpqK..KPpqK",
        "KPpqKKKKPpqK",
        "KPpqKPPKPpqK",
        "KPpqKPPKPpqK",
        "KPpqKPPKPpqK",
        "KPpqKPPKPpqK",
        "KPpqKPPKPpqK",
        "KPpqKPPKPpqK",
        "KPpqKPPKPpqK",
        "KPpqKppKPpqK",
        "KPpqKKKKPpqK",
        "KqqqK..KqqqK",
        ".KKK....KKK.",
    };

    public static readonly RectOffset BannerBorder = new RectOffset(5, 5, 0, 0);

    private static Texture2D slotTexture;
    private static Texture2D slotFilledTexture;
    private static Texture2D bannerTexture;
    public static Texture2D SlotTexture => slotTexture != null ? slotTexture : slotTexture = Build(Slot, "inv_slot");
    public static Texture2D SlotFilledTexture => slotFilledTexture != null ? slotFilledTexture : slotFilledTexture = Build(SlotFilled, "inv_slot_filled");
    public static Texture2D BannerTexture => bannerTexture != null ? bannerTexture : bannerTexture = Build(Banner, "inv_banner");

    public static Texture2D Build(string[] rows, string name)
    {
        int height = rows.Length;
        int width = rows[0].Length;
        var texture = NewTexture(width, height, name);
        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
        {
            string row = rows[y];
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
        texture.Apply(false, true);
        return texture;
    }

    private static Texture2D NewTexture(int width, int height, string name)
    {
        return new Texture2D(width, height, GraphicsFormat.R8G8B8A8_SRGB, TextureCreationFlags.None)
        {
            name = name,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave,
        };
    }
}
