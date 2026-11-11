using UnityEngine;

// The lighthouse's final game drawn on the clue letter's map (ClueMinigame's board), in the
// letter's own hand: the board's hexes in faint pen, the hexes he can't go on washed over with sea
// (the houses' from the start, like the ones the player sinks), the aimed hex in lantern gold with
// a darker rim (red for a moment when a strike is refused) and the villain as a red crayon cube
// where he is, his hex tinted. The letter's map
// is turned to match the view from the lantern (ClueMapArt.Frame.Turn), so pressing W moves the
// gold hex up the map.
//
// Which hex each pixel of the map falls in, and which pixels lie on an edge between hexes, is
// worked out once; each redraw is then a quick pass over the map.
public sealed class MouseTrapLetter
{
    private static readonly Color32 Pen = new Color32(52, 36, 24, 120);
    private static readonly Color32 Sea = new Color32(122, 152, 166, 240);
    private static readonly Color32 SeaFleck = new Color32(84, 112, 132, 255);
    private static readonly Color32 AimWash = new Color32(244, 198, 84, 165);
    private static readonly Color32 AimEdge = new Color32(170, 104, 20, 255);
    private static readonly Color32 RefusedWash = new Color32(200, 52, 40, 175);
    private static readonly Color32 RefusedEdge = new Color32(130, 24, 18, 255);
    private static readonly Color32 MouseWash = new Color32(178, 34, 46, 90);
    private static readonly Color32 CubeLight = new Color32(222, 92, 92, 255);

    private readonly ClueMapArt.Frame frame;
    private readonly HexGrid grid;
    private readonly RectInt area;
    private readonly Hex[] hexes;
    private readonly bool[] onBoard;
    private readonly bool[] border;
    private readonly Color32[] pixels = new Color32[ClueMapArt.LetterWidth * ClueMapArt.LetterHeight];

    public ClueMapArt.Frame Frame => frame;
    public Color32[] Pixels => pixels;

    public MouseTrapLetter(ClueMapArt.Frame frame, HexGrid grid, MouseTrapBoard board)
    {
        this.frame = frame;
        this.grid = grid;
        area = ClueMapArt.MapArea;
        int count = area.width * area.height;
        hexes = new Hex[count];
        onBoard = new bool[count];
        border = new bool[count];
        for (int v = 0; v < area.height; v++)
            for (int u = 0; u < area.width; u++)
            {
                Hex hex = HexAt(area.x + u + 0.5f, area.y + v + 0.5f);
                hexes[v * area.width + u] = hex;
                onBoard[v * area.width + u] = board.Contains(hex);
            }
        // A pixel is on an edge when the pixel to its right or below is in another hex, and one
        // of the two is on the board: one pixel wide lines round every hex of the board.
        for (int v = 0; v < area.height; v++)
            for (int u = 0; u < area.width; u++)
            {
                int i = v * area.width + u;
                int right = u + 1 < area.width ? i + 1 : i, below = v + 1 < area.height ? i + area.width : i;
                border[i] = hexes[right] != hexes[i] && (onBoard[i] || onBoard[right]) ||
                    hexes[below] != hexes[i] && (onBoard[i] || onBoard[below]);
            }
    }

    // The hex under a point of the letter (letter pixels from its top-left).
    public Hex HexAt(float x, float y)
    {
        Vector3 world = frame.ToWorld(x, y);
        return grid.At(world.x, world.z);
    }

    public Vector2 ToLetter(Vector3 world) => frame.ToLetter(world);

    // The board as it stands: aim is the hex the player is aiming at, refused true while a strike
    // there is being turned down, mouse the villain's world position.
    public Color32[] Draw(MouseTrapBoard board, Hex aim, bool refused, Vector3 mouse)
    {
        System.Array.Clear(pixels, 0, pixels.Length);
        Hex mouseHex = grid.At(mouse.x, mouse.z);
        bool mouseOnBoard = board.Contains(mouseHex);
        for (int v = 0; v < area.height; v++)
            for (int u = 0; u < area.width; u++)
            {
                int i = v * area.width + u;
                int x = area.x + u, y = area.y + v;
                Hex hex = hexes[i];
                Color32 colour = default;
                if (onBoard[i])
                {
                    // Blocked from the start (the houses) or struck since: sea either way.
                    if (board.IsSunk(hex) || board.IsWall(hex)) colour = Hash(x, y) > 0.86f ? SeaFleck : Sea;
                    if (hex == aim) colour = Over(refused ? RefusedWash : AimWash, colour);
                    else if (mouseOnBoard && hex == mouseHex) colour = Over(MouseWash, colour);
                }
                if (hex == aim && onBoard[i] && OnRim(u, v, aim)) colour = refused ? RefusedEdge : AimEdge;
                else if (border[i]) colour = Over(Pen, colour);
                pixels[ClueMapArt.Index(x, y)] = colour;
            }
        DrawCube(ToLetter(mouse));
        return pixels;
    }

    // Inside the aimed hex with a neighbour (of four) outside it: its rim, a pixel thick inside.
    private bool OnRim(int u, int v, Hex aim)
    {
        if (u == 0 || v == 0 || u == area.width - 1 || v == area.height - 1) return true;
        int i = v * area.width + u;
        return hexes[i - 1] != aim || hexes[i + 1] != aim || hexes[i - area.width] != aim || hexes[i + area.width] != aim;
    }

    // The villain: a little red crayon cube, inked round, lit on its top-left.
    private void DrawCube(Vector2 at)
    {
        int cx = Mathf.RoundToInt(at.x), cy = Mathf.RoundToInt(at.y);
        for (int dy = -3; dy <= 3; dy++)
            for (int dx = -3; dx <= 3; dx++)
            {
                int x = cx + dx, y = cy + dy;
                if (x < area.xMin || x >= area.xMax || y < area.yMin || y >= area.yMax) continue;
                bool edge = Mathf.Abs(dx) == 3 || Mathf.Abs(dy) == 3;
                Color32 colour = edge ? ClueMapArt.Ink : dx < 0 && dy < 0 ? CubeLight : ClueMapArt.Crayon;
                pixels[ClueMapArt.Index(x, y)] = colour;
            }
    }

    // a laid over b.
    private static Color32 Over(Color32 a, Color32 b)
    {
        float aa = a.a / 255f, ba = b.a / 255f;
        float outA = aa + ba * (1f - aa);
        if (outA <= 0f) return default;
        float Mix(byte ca, byte cb) => (ca * aa + cb * ba * (1f - aa)) / outA;
        return new Color32((byte)Mix(a.r, b.r), (byte)Mix(a.g, b.g), (byte)Mix(a.b, b.b), (byte)Mathf.RoundToInt(outA * 255f));
    }

    private static float Hash(int x, int y)
    {
        uint h = (uint)(x * 374761393 + y * 668265263);
        h = (h ^ (h >> 13)) * 1274126177u;
        return (h ^ (h >> 16)) / (float)uint.MaxValue;
    }
}
