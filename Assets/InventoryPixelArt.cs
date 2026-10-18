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
        // Torch fire, darkest to hottest.
        { 'R', new Color32(168, 44, 22, 255) },
        { 'O', new Color32(234, 118, 36, 255) },
        { 'Y', new Color32(255, 206, 84, 255) },
        { 'w', new Color32(255, 244, 200, 255) },
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

    // Recessed groove for the pause menu slider. 9-slice: left 3, right 3.
    public static readonly string[] SliderTrack =
    {
        ".KKKKKK.",
        "KBBBBBBK",
        "KBbbbbbK",
        "KBbbbbbK",
        "KWWWWWWK",
        ".KKKKKK.",
    };

    // Gold filling the groove up to the knob. Same 9-slice as the track.
    public static readonly string[] SliderFill =
    {
        ".KKKKKK.",
        "KyyyyyyK",
        "KggggggK",
        "KggggggK",
        "KGGGGGGK",
        ".KKKKKK.",
    };

    public static readonly RectOffset SliderBorder = new RectOffset(3, 3, 0, 0);

    // Iron stud riding the slider; gold while hovered or dragged.
    public static readonly string[] SliderKnob =
    {
        ".KKKKK.",
        "KssssiK",
        "KsiiiIK",
        "KsiiiIK",
        "KsiiiIK",
        "KsiiiIK",
        "KsiiiIK",
        "KsiiiIK",
        "KiIIIIK",
        ".KKKKK.",
    };

    public static readonly string[] SliderKnobLit =
    {
        ".KKKKK.",
        "KyyyygK",
        "KygggGK",
        "KygggGK",
        "KygggGK",
        "KygggGK",
        "KygggGK",
        "KygggGK",
        "KgGGGGK",
        ".KKKKK.",
    };

    // Gilded frame for the title screen's top-left corner; mirrored into the other three.
    public static readonly string[] Corner =
    {
        "KKKKKKKKKKKKK.",
        "KyyyggggggGGKK",
        "KygKKKKKKKKGgK",
        "KyK........KGK",
        "KgK.........K.",
        "KgK..KKK......",
        "KgK..KyK......",
        "KgK..KKK......",
        "KgK...........",
        "KgK...........",
        "KGK...........",
        "KGGK..........",
        "KKgGK.........",
        ".KKK..........",
    };

    // Iron chain link with the next link seen edge-on above it; tiles vertically.
    public static readonly string[] ChainLink =
    {
        ".KsK.",
        ".KiK.",
        "KKKKK",
        "KsiIK",
        "Ks.IK",
        "Ki.IK",
        "KiIIK",
        "KKKKK",
    };

    // Gold diamond at the centre of the title screen's divider rule.
    public static readonly string[] Gem =
    {
        "...K...",
        "..KyK..",
        ".KygGK.",
        "KygGGGK",
        ".KgGGK.",
        "..KGK..",
        "...K...",
    };

    // Wall torch: iron cresset on a wooden haft with a bracket ring. The flame sits on the rim.
    public static readonly string[] Torch =
    {
        "KKKKKKKKK",
        "KsiiiiiIK",
        ".KiiIIIK.",
        "..KKKKK..",
        "..KHLWK..",
        "..KHLWK..",
        ".KKKKKKK.",
        ".KsiiiIK.",
        ".KKKKKKK.",
        "..KHLWK..",
        "..KHLWK..",
        "..KHLDK..",
        "...KWK...",
        "...KDK...",
        "....K....",
    };

    // Three flicker frames, played in a loop. Same 9x9 size so they swap in place.
    public static readonly string[][] TorchFlame =
    {
        new[]
        {
            "....R....",
            "...RO....",
            "...ROR...",
            "..ROYOR..",
            "..RYwYOR.",
            ".ROYwwYR.",
            ".RYwwwYR.",
            "..RYwYR..",
            "...RRR...",
        },
        new[]
        {
            ".....R...",
            "....ROR..",
            "...ROR...",
            "..ROYOR..",
            ".ROYwYOR.",
            ".RYwwYOR.",
            ".RYwwwYR.",
            "..RYwYR..",
            "...RRR...",
        },
        new[]
        {
            "....R....",
            "....R....",
            "...ROR...",
            "...RYOR..",
            "..RYwYR..",
            ".ROYwwOR.",
            ".RYwwwYR.",
            "..RYwYR..",
            "...RRR...",
        },
    };

    // Parchment speech bubble with rounded corners and a bright top lip. 9-slice: 3 on every side.
    public static readonly string[] SpeechBubble =
    {
        "..KKKKKKKK..",
        ".KwwwwwwwwK.",
        "KwPPPPPPPPpK",
        "KPPPPPPPPPpK",
        "KPPPPPPPPPpK",
        "KPPPPPPPPPpK",
        "KPPPPPPPPPpK",
        "KPPPPPPPPPpK",
        "KPPPPPPPPPpK",
        "KPPPPPPPPPpK",
        ".KppppppppK.",
        "..KKKKKKKK..",
    };

    public static readonly RectOffset SpeechBubbleBorder = new RectOffset(3, 3, 3, 3);

    // Tail leaning down-right out of the bubble. Its top row overlaps the bubble's
    // bottom outline, opening it; the tip is column 6-7 of the last row.
    public static readonly string[] SpeechTail =
    {
        "KppppqK.",
        ".KppppqK",
        "..KKppqK",
        "....KpqK",
        ".....KqK",
        "......KK",
    };

    // Wooden frame around the skill check's recessed track.
    // 9-slice: left 4 (frame plus the well's shadow column), right 3, top 4, bottom 3.
    public static readonly string[] SkillBar =
    {
        ".KKKKKKKKKK.",
        "KHHHHHHHHHHK",
        "KLKKKKKKKKLK",
        "KLKBBBBBBKLK",
        "KLKBbbbbbKLK",
        "KLKBbbbbbKLK",
        "KLKBbbbbbKLK",
        "KLKBbbbbbKLK",
        "KWKKKKKKKKWK",
        "KDDDDDDDDDDK",
        ".KKKKKKKKKK.",
    };

    public static readonly RectOffset SkillBarBorder = new RectOffset(4, 3, 4, 3);

    // Wooden Enter keycap with a gold return arrow.
    public static readonly string[] EnterKey =
    {
        ".KKKKKKKKKKK.",
        "KHHHHHHHHHHHK",
        "KLLLLLLLLyLLK",
        "KLLLyLLLLyLLK",
        "KLLyyyyyyyLLK",
        "KLLLyLLLLLLLK",
        "KLLLLLLLLLLLK",
        "KDDDDDDDDDDDK",
        ".KKKKKKKKKKK.",
    };

    private static Texture2D slotTexture;
    private static Texture2D slotFilledTexture;
    private static Texture2D bannerTexture;
    private static Texture2D sliderTrackTexture;
    private static Texture2D sliderFillTexture;
    private static Texture2D sliderKnobTexture;
    private static Texture2D sliderKnobLitTexture;
    public static Texture2D SlotTexture => slotTexture != null ? slotTexture : slotTexture = Build(Slot, "inv_slot");
    public static Texture2D SlotFilledTexture => slotFilledTexture != null ? slotFilledTexture : slotFilledTexture = Build(SlotFilled, "inv_slot_filled");
    public static Texture2D BannerTexture => bannerTexture != null ? bannerTexture : bannerTexture = Build(Banner, "inv_banner");
    public static Texture2D SliderTrackTexture => sliderTrackTexture != null ? sliderTrackTexture : sliderTrackTexture = Build(SliderTrack, "slider_track");
    public static Texture2D SliderFillTexture => sliderFillTexture != null ? sliderFillTexture : sliderFillTexture = Build(SliderFill, "slider_fill");
    public static Texture2D SliderKnobTexture => sliderKnobTexture != null ? sliderKnobTexture : sliderKnobTexture = Build(SliderKnob, "slider_knob");
    public static Texture2D SliderKnobLitTexture => sliderKnobLitTexture != null ? sliderKnobLitTexture : sliderKnobLitTexture = Build(SliderKnobLit, "slider_knob_lit");

    private static Texture2D cornerTexture;
    private static Texture2D chainLinkTexture;
    private static Texture2D gemTexture;
    private static Texture2D torchTexture;
    private static Texture2D[] torchFlameTextures;
    public static Texture2D CornerTexture => cornerTexture != null ? cornerTexture : cornerTexture = Build(Corner, "title_corner");
    public static Texture2D ChainLinkTexture => chainLinkTexture != null ? chainLinkTexture : chainLinkTexture = Build(ChainLink, "title_chain");
    public static Texture2D GemTexture => gemTexture != null ? gemTexture : gemTexture = Build(Gem, "title_gem");
    public static Texture2D TorchTexture => torchTexture != null ? torchTexture : torchTexture = Build(Torch, "title_torch");

    private static Texture2D speechBubbleTexture;
    private static Texture2D speechTailTexture;
    private static Texture2D enterKeyTexture;
    private static Texture2D skillBarTexture;
    public static Texture2D SkillBarTexture => skillBarTexture != null ? skillBarTexture : skillBarTexture = Build(SkillBar, "skill_bar");
    public static Texture2D SpeechBubbleTexture => speechBubbleTexture != null ? speechBubbleTexture : speechBubbleTexture = Build(SpeechBubble, "speech_bubble");
    public static Texture2D SpeechTailTexture => speechTailTexture != null ? speechTailTexture : speechTailTexture = Build(SpeechTail, "speech_tail");
    public static Texture2D EnterKeyTexture => enterKeyTexture != null ? enterKeyTexture : enterKeyTexture = Build(EnterKey, "key_enter");

    public static Texture2D TorchFlameTexture(int frame)
    {
        if (torchFlameTextures == null || torchFlameTextures.Length != TorchFlame.Length)
        {
            torchFlameTextures = new Texture2D[TorchFlame.Length];
        }
        frame %= TorchFlame.Length;
        if (torchFlameTextures[frame] == null)
        {
            torchFlameTextures[frame] = Build(TorchFlame[frame], "title_flame_" + frame);
        }
        return torchFlameTextures[frame];
    }

    // A GUIStyle that 9-slices the texture, for stretching frames to any size.
    public static GUIStyle NineSlice(Texture2D texture, RectOffset border)
    {
        var style = new GUIStyle { border = new RectOffset(border.left, border.right, border.top, border.bottom) };
        style.normal.background = texture;
        return style;
    }

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
