using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// The colour maze written on the clue letter, ported from references/minigameOne.js and
// grown into a random 20 x 20 board. InventoryInspection shows it in place of the letter's
// sprite. Step onto tiles in rainbow order, red to white: any tile of the next colour
// counts, every other colour is a wall, and clearing white reveals the clue. Each board is
// built around a carved route so it is always solvable, and the step budget sits just
// above the best route the generator could find.
public class ClueMinigame : MonoBehaviour
{
    public const string Controls = "W A S D  Step     E  Restart     R  New board     ESC  Close";

    private const int GridSize = 20;
    private const int BorderSize = GridSize + 2;
    private const int TileSize = 35;
    private const int CanvasSize = BorderSize * TileSize;
    // The sketch measured tiles in 14 px and its end screen against a 12-tile canvas of
    // 70 px tiles; both are rescaled to this board.
    private const float SketchTile = 14f;
    private const float SketchCanvas = 840f;
    private const int Floor = -1;

    // Walls take the colour of their nearest target, so each colour gathers in its own
    // patch of the map, with a share of strays. Legs between targets keep routes long.
    private const float WallDensity = 0.65f;
    private const float StrayColours = 0.15f;
    private const int MinLeg = 10;
    private const int MaxLeg = 18;
    // The budget search tries the nearest few tiles of each colour, then adds 10%, at least 3.
    private const int SearchBranches = 5;
    private const float BudgetSlack = 0.1f;
    private const int MinBudgetSlack = 3;

    private enum Outcome { Playing, Won, OutOfSteps, DeadEnd }

    private static readonly Vector2Int Start = new Vector2Int(GridSize / 2, GridSize / 2);

    private static readonly Vector2Int[] Directions =
    {
        new Vector2Int(1, 0),
        new Vector2Int(-1, 0),
        new Vector2Int(0, 1),
        new Vector2Int(0, -1),
    };

    private static readonly Color32[] Colors =
    {
        new Color32(255, 0, 0, 255),
        new Color32(255, 127, 0, 255),
        new Color32(255, 255, 0, 255),
        new Color32(0, 255, 0, 255),
        new Color32(0, 0, 255, 255),
        new Color32(75, 0, 130, 255),
        new Color32(255, 255, 255, 255),
    };

    private static readonly Color32 Background = new Color32(80, 80, 120, 255);
    private static readonly Color32 Outline = new Color32(38, 35, 80, 255);
    private static readonly Color32 Shade = new Color32(0, 0, 0, 70);
    private static readonly Color32 Shine = new Color32(255, 255, 255, 90);
    private static readonly Color32 PlayerShadow = new Color32(0, 0, 0, 120);
    private static readonly Color32 White = new Color32(255, 255, 255, 255);
    private static readonly Color32 EndBackground = new Color32(8, 8, 12, 255);
    private static readonly Color32 EndBox = new Color32(20, 20, 30, 255);

    [SerializeField]
    private string triggerItem = "Clue Letter";

    // Cycled clockwise around the frame: orca, gazelle, iguana, panther.
    // Unassigned tiles draw black, like the sketch before its images load.
    [SerializeField]
    private Texture2D[] borderTiles = new Texture2D[4];

    private readonly System.Random random = new System.Random();
    private readonly int[,] layout = new int[GridSize, GridSize];
    private readonly int[,] board = new int[GridSize, GridSize];
    // Breadth-first scratch shared by the generator and the dead-end check.
    private readonly int[] distance = new int[GridSize * GridSize];
    private readonly int[] queue = new int[GridSize * GridSize];
    private int stepBudget;
    private int stepsLeft;
    private Vector2Int player;
    private int currentColor;
    private Outcome outcome;

    private RectTransform surface;
    private Texture2D texture;
    private Color32[] pixels;
    private GameObject border;
    private Text endTitle;
    private Text endSubtitle;
    private Text stepsLabel;

    public bool IsShowing => surface != null && surface.gameObject.activeSelf;

    public bool Handles(string itemName)
    {
        return itemName == triggerItem;
    }

    // Progress survives closing the letter; only E or R start over.
    public void Show(RectTransform parent)
    {
        if (surface == null)
        {
            BuildView(parent);
        }
        surface.gameObject.SetActive(true);
        Redraw();
    }

    public void Hide()
    {
        if (surface != null)
        {
            surface.gameObject.SetActive(false);
        }
    }

    private void Awake()
    {
        NewBoard();
        Restart();
    }

    private void OnDestroy()
    {
        if (texture != null)
        {
            Destroy(texture);
        }
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (!IsShowing || keyboard == null)
        {
            return;
        }

        bool changed = true;
        if (keyboard.rKey.wasPressedThisFrame)
        {
            NewBoard();
            Restart();
        }
        else if (keyboard.eKey.wasPressedThisFrame)
        {
            Restart();
        }
        else
        {
            changed = Step(keyboard);
        }
        if (changed)
        {
            Redraw();
        }
    }

    private void LateUpdate()
    {
        if (!IsShowing)
        {
            return;
        }

        // Square, leaving room for the step counter under the board.
        Rect bounds = ((RectTransform)surface.parent).rect;
        float side = Mathf.Min(bounds.height * 0.72f, bounds.width * 0.84f);
        surface.sizeDelta = new Vector2(side, side);
        float scale = side / SketchCanvas;
        endTitle.fontSize = Mathf.Max(1, Mathf.RoundToInt(42f * scale));
        endSubtitle.fontSize = Mathf.Max(1, Mathf.RoundToInt(18f * scale));
    }

    private void Restart()
    {
        System.Array.Copy(layout, board, layout.Length);
        player = Start;
        currentColor = 0;
        stepsLeft = stepBudget;
        outcome = Outcome.Playing;
    }

    // One step per key press, so every move is deliberate. Bumping a wall is free.
    private bool Step(Keyboard keyboard)
    {
        if (outcome != Outcome.Playing)
        {
            return false;
        }

        Vector2Int step;
        if (keyboard.aKey.wasPressedThisFrame) step = new Vector2Int(-1, 0);
        else if (keyboard.dKey.wasPressedThisFrame) step = new Vector2Int(1, 0);
        else if (keyboard.wKey.wasPressedThisFrame) step = new Vector2Int(0, -1);
        else if (keyboard.sKey.wasPressedThisFrame) step = new Vector2Int(0, 1);
        else return false;

        Vector2Int next = player + step;
        if (next.x < 0 || next.x >= GridSize || next.y < 0 || next.y >= GridSize)
        {
            return false;
        }

        int tile = board[next.y, next.x];
        if (tile != Floor && tile != currentColor)
        {
            return false; // Only the next colour in the sequence can be stepped on.
        }
        if (tile != Floor)
        {
            board[next.y, next.x] = Floor;
            currentColor += 1;
        }
        player = next;
        stepsLeft -= 1;

        if (currentColor >= Colors.Length)
        {
            outcome = Outcome.Won;
        }
        else if (stepsLeft <= 0)
        {
            outcome = Outcome.OutOfSteps;
        }
        else if (FindTiles(player, currentColor, 1, null, null) == 0)
        {
            // Clearing the wrong tile can wall off every tile of the next colour.
            outcome = Outcome.DeadEnd;
        }
        return true;
    }

    private void NewBoard()
    {
        // Lay out the route first: a target per colour, each one leg from the last,
        // joined by corridors that are never filled in.
        var reserved = new bool[GridSize, GridSize];
        var targets = new Vector2Int[Colors.Length];
        Vector2Int at = Start;
        reserved[at.y, at.x] = true;
        for (int colour = 0; colour < Colors.Length; colour++)
        {
            Vector2Int target = PickTarget(at, reserved);
            CarveCorridor(at, target, reserved);
            reserved[target.y, target.x] = true;
            targets[colour] = target;
            at = target;
        }

        for (int y = 0; y < GridSize; y++)
        {
            for (int x = 0; x < GridSize; x++)
            {
                if (reserved[y, x] || random.NextDouble() >= WallDensity)
                {
                    layout[y, x] = Floor;
                }
                else
                {
                    layout[y, x] = random.NextDouble() < StrayColours ? random.Next(Colors.Length) : NearestTarget(targets, x, y);
                }
            }
        }
        for (int colour = 0; colour < Colors.Length; colour++)
        {
            layout[targets[colour].y, targets[colour].x] = colour;
        }

        // The carved route is always walkable, so its length caps the search.
        System.Array.Copy(layout, board, layout.Length);
        int planned = 0;
        at = Start;
        for (int colour = 0; colour < Colors.Length; colour++)
        {
            Vector2Int target = targets[colour];
            FindTiles(at, colour, int.MaxValue, null, null);
            planned += distance[target.y * GridSize + target.x];
            board[target.y, target.x] = Floor;
            at = target;
        }

        System.Array.Copy(layout, board, layout.Length);
        int best = Search(Start, 0, 0, planned);
        stepBudget = best + Mathf.Max(MinBudgetSlack, Mathf.CeilToInt(best * BudgetSlack));
    }

    private Vector2Int PickTarget(Vector2Int from, bool[,] reserved)
    {
        var inRange = new List<Vector2Int>();
        var free = new List<Vector2Int>();
        for (int y = 0; y < GridSize; y++)
        {
            for (int x = 0; x < GridSize; x++)
            {
                if (reserved[y, x])
                {
                    continue;
                }
                var cell = new Vector2Int(x, y);
                free.Add(cell);
                int leg = Mathf.Abs(x - from.x) + Mathf.Abs(y - from.y);
                if (leg >= MinLeg && leg <= MaxLeg)
                {
                    inRange.Add(cell);
                }
            }
        }
        List<Vector2Int> pool = inRange.Count > 0 ? inRange : free;
        return pool[random.Next(pool.Count)];
    }

    // A random staircase from one target to the cell beside the next.
    private void CarveCorridor(Vector2Int from, Vector2Int to, bool[,] reserved)
    {
        int stepX = System.Math.Sign(to.x - from.x);
        int stepY = System.Math.Sign(to.y - from.y);
        int remainingX = Mathf.Abs(to.x - from.x);
        int remainingY = Mathf.Abs(to.y - from.y);
        Vector2Int at = from;
        while (remainingX + remainingY > 1)
        {
            if (random.Next(remainingX + remainingY) < remainingX)
            {
                at.x += stepX;
                remainingX -= 1;
            }
            else
            {
                at.y += stepY;
                remainingY -= 1;
            }
            reserved[at.y, at.x] = true;
        }
    }

    private static int NearestTarget(Vector2Int[] targets, int x, int y)
    {
        int nearest = 0;
        int nearestDistance = int.MaxValue;
        for (int i = 0; i < targets.Length; i++)
        {
            int dx = targets[i].x - x;
            int dy = targets[i].y - y;
            if (dx * dx + dy * dy < nearestDistance)
            {
                nearestDistance = dx * dx + dy * dy;
                nearest = i;
            }
        }
        return nearest;
    }

    // Depth-first over the cheapest few tiles of each colour, pruned by the best total so far.
    private int Search(Vector2Int at, int colour, int spent, int best)
    {
        if (colour >= Colors.Length)
        {
            return spent;
        }

        var tiles = new Vector2Int[SearchBranches];
        var costs = new int[SearchBranches];
        int count = FindTiles(at, colour, SearchBranches, tiles, costs);
        for (int i = 0; i < count && spent + costs[i] < best; i++)
        {
            Vector2Int tile = tiles[i];
            board[tile.y, tile.x] = Floor;
            best = Mathf.Min(best, Search(tile, colour + 1, spent + costs[i], best));
            board[tile.y, tile.x] = colour;
        }
        return best;
    }

    // Walks the floor outward from `from` and returns up to `limit` tiles of `colour`
    // with the steps needed to stand on them, cheapest first. Leaves every reached
    // cell's step count in `distance`.
    private int FindTiles(Vector2Int from, int colour, int limit, Vector2Int[] tiles, int[] costs)
    {
        for (int i = 0; i < distance.Length; i++)
        {
            distance[i] = -1;
        }

        int head = 0;
        int tail = 0;
        int found = 0;
        int origin = from.y * GridSize + from.x;
        distance[origin] = 0;
        queue[tail++] = origin;
        while (head < tail)
        {
            int cell = queue[head++];
            int cx = cell % GridSize;
            int cy = cell / GridSize;
            foreach (Vector2Int direction in Directions)
            {
                int nx = cx + direction.x;
                int ny = cy + direction.y;
                if (nx < 0 || nx >= GridSize || ny < 0 || ny >= GridSize)
                {
                    continue;
                }
                int next = ny * GridSize + nx;
                if (distance[next] >= 0)
                {
                    continue;
                }

                int tile = board[ny, nx];
                if (tile == Floor)
                {
                    distance[next] = distance[cell] + 1;
                    queue[tail++] = next;
                }
                else if (tile == colour)
                {
                    distance[next] = distance[cell] + 1;
                    if (tiles != null)
                    {
                        tiles[found] = new Vector2Int(nx, ny);
                        costs[found] = distance[next];
                    }
                    found += 1;
                    if (found == limit)
                    {
                        return found;
                    }
                }
            }
        }
        return found;
    }

    private void Redraw()
    {
        bool ended = outcome != Outcome.Playing;
        if (ended)
        {
            DrawEndScreen();
        }
        else
        {
            Fill(0, 0, CanvasSize, CanvasSize, Background);
            DrawBoard();
            DrawPlayer();
        }
        texture.SetPixels32(pixels);
        texture.Apply();

        // The end screen covers the whole canvas, frame included.
        border.SetActive(!ended);
        endTitle.gameObject.SetActive(ended);
        endSubtitle.gameObject.SetActive(ended);
        endTitle.text = outcome == Outcome.Won ? "Fell, Jerk, Thief" : outcome == Outcome.OutOfSteps ? "Out of steps" : "Dead end";
        endSubtitle.text = outcome == Outcome.Won ? "Press 'E' to Restart" : "Press 'E' to Retry";
        stepsLabel.text = "Steps left: " + stepsLeft;
    }

    private void DrawBoard()
    {
        int edge = TilePx(0.8f);
        int inset = TilePx(1.6f);
        int face = TileSize - TilePx(3.2f);
        int shine = TilePx(1.2f);
        for (int y = 0; y < GridSize; y++)
        {
            for (int x = 0; x < GridSize; x++)
            {
                int px = (x + 1) * TileSize;
                int py = (y + 1) * TileSize;
                Frame(px, py, TileSize, TileSize, edge, Outline);

                int tile = board[y, x];
                if (tile == Floor)
                {
                    continue;
                }
                Fill(px + inset, py + inset, face, face, Colors[tile]);
                Fill(px + inset, py + face, face, inset, Shade);
                Fill(px + face, py + inset, inset, face, Shade);
                Fill(px + inset, py + inset, face, shine, Shine);
                Fill(px + inset, py + inset, shine, face, Shine);
            }
        }
    }

    private void DrawPlayer()
    {
        int px = (player.x + 1) * TileSize;
        int py = (player.y + 1) * TileSize;
        int shadowAt = TilePx(3.4f);
        int shadowSize = TileSize - TilePx(5.2f);
        int bodyAt = TilePx(2.8f);
        int bodySize = TileSize - TilePx(5.6f);
        int stroke = TilePx(1.4f);
        int outside = (stroke + 1) / 2;
        Fill(px + shadowAt, py + shadowAt, shadowSize, shadowSize, PlayerShadow);
        Fill(px + bodyAt, py + bodyAt, bodySize, bodySize, Colors[currentColor]);
        // The white stroke straddles the body's edge, as p5 draws it.
        Frame(px + bodyAt - outside, py + bodyAt - outside, bodySize + stroke, bodySize + stroke, stroke, White);
        Fill(px + TilePx(4f), py + TilePx(4f), TilePx(1.6f), TilePx(1.6f), White);
    }

    private void DrawEndScreen()
    {
        int boxWidth = CanvasPx(380f);
        int boxHeight = CanvasPx(200f);
        int corner = CanvasPx(20f);
        int stroke = CanvasPx(5f);
        int boxX = (CanvasSize - boxWidth) / 2;
        int boxY = (CanvasSize - boxHeight) / 2;
        int outside = (stroke + 1) / 2;
        int inside = stroke / 2;

        Fill(0, 0, CanvasSize, CanvasSize, EndBackground);
        Fill(boxX - outside, boxY - outside, boxWidth + outside * 2, boxHeight + outside * 2, White);
        Fill(boxX + inside, boxY + inside, boxWidth - inside * 2, boxHeight - inside * 2, EndBox);
        Fill(boxX, boxY, corner, corner, White);
        Fill(boxX + boxWidth - corner, boxY, corner, corner, White);
        Fill(boxX, boxY + boxHeight - corner, corner, corner, White);
        Fill(boxX + boxWidth - corner, boxY + boxHeight - corner, corner, corner, White);
    }

    private static int TilePx(float sketchPixels)
    {
        return Mathf.FloorToInt(sketchPixels * TileSize / SketchTile + 0.5f);
    }

    private static int CanvasPx(float sketchPixels)
    {
        return Mathf.FloorToInt(sketchPixels * CanvasSize / SketchCanvas + 0.5f);
    }

    private void Frame(int x, int y, int width, int height, int thickness, Color32 color)
    {
        Fill(x, y, width, thickness, color);
        Fill(x, y + height - thickness, width, thickness, color);
        Fill(x, y + thickness, thickness, height - thickness * 2, color);
        Fill(x + width - thickness, y + thickness, thickness, height - thickness * 2, color);
    }

    // Alpha-blends a rectangle measured from the top-left, like p5's rect().
    private void Fill(int x, int y, int width, int height, Color32 color)
    {
        int alpha = color.a;
        int keep = 255 - alpha;
        for (int row = y; row < y + height; row++)
        {
            // Texture rows run bottom-up.
            int start = (CanvasSize - 1 - row) * CanvasSize;
            for (int i = start + x; i < start + x + width; i++)
            {
                Color32 under = pixels[i];
                pixels[i] = alpha == 255 ? color : new Color32(
                    (byte)((color.r * alpha + under.r * keep + 127) / 255),
                    (byte)((color.g * alpha + under.g * keep + 127) / 255),
                    (byte)((color.b * alpha + under.b * keep + 127) / 255),
                    255);
            }
        }
    }

    private void BuildView(RectTransform parent)
    {
        surface = new GameObject("Clue minigame", typeof(RectTransform)).GetComponent<RectTransform>();
        surface.SetParent(parent, false);
        surface.anchorMin = surface.anchorMax = new Vector2(0.5f, 0.5f);
        surface.anchoredPosition = Vector2.zero;

        pixels = new Color32[CanvasSize * CanvasSize];
        // Mipmaps keep the thin grid lines even when the view is shrunk on small screens.
        texture = new Texture2D(CanvasSize, CanvasSize, TextureFormat.RGBA32, true)
        {
            name = "Clue minigame",
            filterMode = FilterMode.Trilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        CreateTile("Board", surface, 0, 0, BorderSize).texture = texture;

        border = new GameObject("Border", typeof(RectTransform));
        RectTransform frame = (RectTransform)border.transform;
        frame.SetParent(surface, false);
        frame.anchorMin = Vector2.zero;
        frame.anchorMax = Vector2.one;
        frame.offsetMin = frame.offsetMax = Vector2.zero;
        // Walk the frame clockwise from the top-left corner, as the sketch does.
        int last = BorderSize - 1;
        int index = 0;
        for (int x = 0; x <= last; x++) AddBorderTile(frame, index++, x, 0);
        for (int y = 1; y <= last; y++) AddBorderTile(frame, index++, last, y);
        for (int x = last - 1; x >= 0; x--) AddBorderTile(frame, index++, x, last);
        for (int y = last - 1; y > 0; y--) AddBorderTile(frame, index++, 0, y);

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        endTitle = CreateLabel("End title", font, Color.white);
        PlaceOnCentre(endTitle, 25f);
        endSubtitle = CreateLabel("End subtitle", font, new Color(180f / 255f, 180f / 255f, 180f / 255f));
        PlaceOnCentre(endSubtitle, -35f);

        stepsLabel = CreateLabel("Steps left", font, new Color(1f, 1f, 1f, 0.85f));
        stepsLabel.fontSize = 16;
        RectTransform steps = stepsLabel.rectTransform;
        steps.anchorMin = Vector2.zero;
        steps.anchorMax = new Vector2(1f, 0f);
        steps.pivot = new Vector2(0.5f, 1f);
        steps.sizeDelta = new Vector2(0f, 22f);
        steps.anchoredPosition = new Vector2(0f, -6f);
    }

    private void AddBorderTile(Transform parent, int index, int x, int y)
    {
        Texture2D art = borderTiles.Length > 0 ? borderTiles[index % borderTiles.Length] : null;
        RawImage tile = CreateTile("Border tile", parent, x, y, 1);
        tile.texture = art;
        tile.color = art != null ? Color.white : Color.black;
    }

    // Places a square image on the tile grid, counted from the top-left.
    private static RawImage CreateTile(string name, Transform parent, int x, int y, int size)
    {
        RawImage image = new GameObject(name, typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        image.transform.SetParent(parent, false);
        image.raycastTarget = false;
        RectTransform rect = image.rectTransform;
        rect.anchorMin = new Vector2((float)x / BorderSize, 1f - (float)(y + size) / BorderSize);
        rect.anchorMax = new Vector2((float)(x + size) / BorderSize, 1f - (float)y / BorderSize);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return image;
    }

    private Text CreateLabel(string name, Font font, Color color)
    {
        Text label = new GameObject(name, typeof(RectTransform), typeof(Text)).GetComponent<Text>();
        label.transform.SetParent(surface, false);
        label.font = font;
        label.fontStyle = FontStyle.Bold;
        label.color = color;
        label.alignment = TextAnchor.MiddleCenter;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        label.raycastTarget = false;
        return label;
    }

    // offsetY is in sketch canvas pixels above the centre, matching its text() calls.
    private static void PlaceOnCentre(Text label, float offsetY)
    {
        float anchorY = 0.5f + offsetY / SketchCanvas;
        label.rectTransform.anchorMin = new Vector2(0f, anchorY);
        label.rectTransform.anchorMax = new Vector2(1f, anchorY);
        label.rectTransform.sizeDelta = Vector2.zero;
    }
}
