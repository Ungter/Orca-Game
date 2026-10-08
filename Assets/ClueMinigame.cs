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
    private const string PlayControls = "[WASD] STEP    [E] RESTART    [R] NEW BOARD    [ESC] CLOSE";
    private const string SolvedControls = "[ESC] CLOSE";

    private const int GridSize = 20;
    private const int BorderSize = GridSize + 2;
    private const int TileSize = 35;
    private const int CanvasSize = BorderSize * TileSize;
    // The sketch measured tiles in 14 px; TilePx rescales those sizes to this board.
    private const float SketchTile = 14f;
    private const int Floor = -1;

    // Where the board, step counter and colour hint sit on Letter_screen.png (384 x 240,
    // measured from the top-left). Its blank paper spans x 6-359, y 12-196; the board is
    // centred on it, the counter and hint fill the margins either side.
    private const float BoardSide = 168f;
    private static readonly Vector2 LetterSize = new Vector2(384f, 240f);
    private static readonly Vector2 BoardCentre = new Vector2(182f, 115f);
    private static readonly Vector2 StepsCentre = new Vector2(313f, 104f);
    private const float HintX = 52f;
    private const float HintSwatch = 30f;
    private static readonly Color32 Ink = new Color32(80, 68, 56, 255);

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

    private enum Outcome { Playing, Won, OutOfSteps }

    private static readonly Vector2Int Start = new Vector2Int(GridSize / 2, GridSize / 2);

    private static readonly Vector2Int[] Directions =
    {
        new Vector2Int(1, 0),
        new Vector2Int(-1, 0),
        new Vector2Int(0, 1),
        new Vector2Int(0, -1),
    };

    // Muted pigments, so the tiles read as paint on parchment yet stay easy to tell apart.
    private static readonly Color32[] Colors =
    {
        new Color32(176, 52, 38, 255),   // vermilion
        new Color32(204, 114, 40, 255),  // orange ochre
        new Color32(218, 174, 58, 255),  // saffron
        new Color32(70, 132, 84, 255),   // verdigris
        new Color32(44, 80, 154, 255),   // lapis
        new Color32(94, 56, 120, 255),   // murex purple
        new Color32(238, 230, 206, 255), // chalk
    };

    // Pen ink for grid lines, tile and swatch outlines.
    private static readonly Color32 Outline = new Color32(52, 36, 24, 255);
    // The grid keeps going past the board onto the paper and fades out over this many tiles.
    private const float PaperLineFade = 3.5f;
    // Paper-lines texture resolution, in texels per letter pixel.
    private const int PaperLineScale = 4;

    [SerializeField]
    private string triggerItem = "Clue Letter";


    private readonly System.Random random = new System.Random();
    private readonly int[,] layout = new int[GridSize, GridSize];
    private readonly int[,] board = new int[GridSize, GridSize];
    // Breadth-first scratch for the generator.
    private readonly int[] distance = new int[GridSize * GridSize];
    private readonly int[] queue = new int[GridSize * GridSize];
    private int stepBudget;
    private int stepsLeft;
    private Vector2Int player;
    private int currentColor;
    private Outcome outcome;

    private RectTransform surface;
    private RectTransform boardRoot;
    private Image letterImage;
    private Texture2D texture;
    // Runtime caches. NonSerialized so a script reload in Play mode resets them to null
    // (Unity would otherwise restore them as empty arrays) and they get rebuilt.
    [System.NonSerialized]
    private Color32[] pixels;
    [System.NonSerialized]
    private float[] grain;
    // The paper lines: the board grid continued onto the letter (hidden on the end screen).
    private GameObject border;
    private Texture2D paperLinesTexture;
    private Sprite paperLinesFor;
    private PixelText stepsLabel;
    // The clue, written out in cursive once the puzzle is solved.
    private const string ClueText = "Fell, Jerk, Thief";
    private RawImage clueInk;
    private LetterBurn burn;
    [System.NonSerialized]
    private CursiveWriter clueWriter;
    private GameObject hint;
    private PixelText hintHeading;
    private PixelText hintThen;
    private RawImage nextSwatch;
    private RawImage afterSwatch;
    // One painted tile per colour (the board's own PaintTile) for the colour hints.
    [System.NonSerialized]
    private Texture2D[] swatchTextures;


    public bool IsShowing => surface != null && surface.gameObject.activeSelf;

    // Once solved, the clue stays revealed for good: no restart, no new board.
    public bool IsSolved => outcome == Outcome.Won;

    public string Controls => IsSolved ? SolvedControls : PlayControls;

    public bool Handles(string itemName)
    {
        return itemName == triggerItem;
    }

    // Progress survives closing the letter; only E or R start over.
    public void Show(RectTransform parent, Sprite letter)
    {
        if (surface == null)
        {
            BuildView(parent);
        }
        letterImage.sprite = letter;
        letterImage.enabled = letter != null;
        if (paperLinesTexture == null || paperLinesFor != letter)
        {
            BuildPaperLines(letter);
        }
        surface.gameObject.SetActive(true);
        Redraw();
    }

public void Hide()
    {
        // Closing mid-animation finishes the reset (or the writing) at once.
        burn?.Cancel();
        if (clueWriter != null && clueWriter.IsPlaying)
        {
            clueWriter.Complete();
        }
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
        if (paperLinesTexture != null)
        {
            Destroy(paperLinesTexture);
        }
        if (swatchTextures != null)
        {
            foreach (Texture2D swatch in swatchTextures)
            {
                if (swatch != null) Destroy(swatch);
            }
        }
        burn?.Dispose();
        clueWriter?.Dispose();
    }

    // Lays the cursive clue out once and points the clue image at its texture.
    private void EnsureClueWriter()
    {
        if (clueWriter == null)
        {
            clueWriter = new CursiveWriter(ClueText, 300f, 18.2f, 4);
        }
        if (clueInk != null && clueInk.texture != clueWriter.Texture)
        {
            clueInk.texture = clueWriter.Texture;
            PlaceOnLetter(clueInk.rectTransform, BoardCentre, clueWriter.SizeInLetterPixels);
        }
    }

private void Update()
    {
        if (!IsShowing)
        {
            return;
        }
        if (burn != null && burn.IsPlaying)
        {
            burn.Tick(Time.unscaledDeltaTime);
            return; // Input waits for the letter to rebuild.
        }
        if (clueWriter != null && clueWriter.IsPlaying)
        {
            clueWriter.Tick(Time.unscaledDeltaTime);
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || IsSolved)
        {
            return;
        }

        if (keyboard.rKey.wasPressedThisFrame)
        {
            BurnAndReset(true);
        }
        else if (keyboard.eKey.wasPressedThisFrame)
        {
            BurnAndReset(false);
        }
        else if (Step(keyboard))
        {
            if (outcome == Outcome.Won)
            {
                EnsureClueWriter();
                clueWriter.Play();
            }
            Redraw();
            if (outcome == Outcome.OutOfSteps)
            {
                // The last step is drawn, then the letter goes up in flames from the board's
                // centre while "OUT OF STEPS" burns into view.
                BurnAndReset(false, true);
            }
        }
    }

    // The letter burns up; while it is ash the board resets (or is replaced), and
    // the letter rebuilds showing the fresh game.
// The letter burns up; while it is ash the board resets (or is replaced), and
    // the letter rebuilds showing the fresh game. Running out of steps always burns from
    // the board's centre and writes the flame headline; E and R start random fires.
    private void BurnAndReset(bool newBoard, bool outOfSteps = false)
    {
        burn ??= new LetterBurn(surface, letterImage);
        burn.Play(() =>
        {
            if (newBoard)
            {
                NewBoard();
            }
            Restart();
            Redraw();
        }, outOfSteps ? OnLetter(BoardCentre) : (Vector2?)null, outOfSteps ? "OUT OF STEPS" : null);
    }

    private void LateUpdate()
    {
        if (!IsShowing)
        {
            return;
        }

        // The letter takes an inspected sprite's footprint; the board keeps its place on the paper.
        Rect bounds = ((RectTransform)surface.parent).rect;
        Sprite letter = letterImage.sprite;
        float aspect = letter != null ? letter.rect.width / letter.rect.height : LetterSize.x / LetterSize.y;
        float height = Mathf.Min(bounds.height * 0.76f, bounds.width * 0.84f / aspect);
        surface.sizeDelta = new Vector2(height * aspect, height);
        float side = height * BoardSide / LetterSize.y;
        boardRoot.sizeDelta = new Vector2(side, side);
        // Sizes are cap heights; PixelText rounds them to whole screen pixels per font pixel.
        float inkSize = height * 0.035f;
        stepsLabel.fontSize = inkSize;
        hintHeading.fontSize = inkSize;
        hintThen.fontSize = height * 0.028f;
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
            // Walling off the next colour is never announced; the player finds out by
            // running out of steps.
            outcome = Outcome.OutOfSteps;
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
        EnsureCanvas();
        EnsureSwatches();
        // Running out of steps still shows the board: the burn takes it from there. Once
        // solved the board is hidden (and the player has no colour left to wear).
        DrawPaper();
        if (!IsSolved)
        {
            DrawBoard();
            DrawPlayer();
        }
        ApplyLighting();
        texture.SetPixels32(pixels);
        texture.Apply();

        stepsLabel.text = "Steps left\n" + stepsLeft;

        // Solving wipes the puzzle off the letter and leaves only the clue written on it.
        boardRoot.gameObject.SetActive(!IsSolved);
        border.SetActive(!IsSolved); // The grid running out onto the paper goes with the board.
        stepsLabel.gameObject.SetActive(!IsSolved);
        clueInk.gameObject.SetActive(IsSolved);
        if (IsSolved)
        {
            EnsureClueWriter();
            if (!clueWriter.HasStarted)
            {
                clueWriter.Complete(); // Solved earlier: show it already written.
            }
        }

        // The player wears the colour it is on; the hint shows the two that follow it.
        int next = currentColor + 1;
        int after = currentColor + 2;
        hint.SetActive(!IsSolved && next < Colors.Length);
        if (hint.activeSelf)
        {
            nextSwatch.texture = swatchTextures[next];
            bool hasAfter = after < Colors.Length;
            afterSwatch.gameObject.SetActive(hasAfter);
            hintThen.gameObject.SetActive(hasAfter);
            if (hasAfter)
            {
                afterSwatch.texture = swatchTextures[after];
            }
        }
    }

private void DrawBoard()
    {
        int inset = TilePx(1.6f);
        int face = TileSize - inset * 2;
        for (int y = 0; y < GridSize; y++)
        {
            for (int x = 0; x < GridSize; x++)
            {
                int tile = board[y, x];
                if (tile != Floor)
                {
                    PaintTile((x + 1) * TileSize + inset, (y + 1) * TileSize + inset, face, Colors[tile]);
                }
            }
        }

        // Pen grid over the paint, wobbling and fading like ink from a nib.
        int start = TileSize;
        int end = TileSize * (GridSize + 1);
        for (int k = 0; k <= GridSize; k++)
        {
            int at = TileSize * (k + 1);
            int width = k == 0 || k == GridSize ? 3 : 2;
            InkLine(start, at, end - start, true, width, k * 7 + 1);
            InkLine(at, start, end - start, false, width, k * 7 + 3);
        }
    }

    // A square of pigment: soft cast shadow, ragged brush edges, granulated paint,
    // light from the top-left (bright upper/left rims, dark lower/right rims), inked outline.
    private void PaintTile(int x0, int y0, int size, Color32 color)
    {
        int shadow = TilePx(1.2f);
        for (int v = 0; v < size; v++)
        {
            for (int u = 0; u < size; u++)
            {
                int x = x0 + u + shadow;
                int y = y0 + v + shadow;
                Blend(x, y, Outline, 0.22f * (0.7f + 0.6f * Grain(x, y)));
            }
        }

        int rim = Mathf.Max(2, TilePx(0.9f));
        for (int v = 0; v < size; v++)
        {
            for (int u = 0; u < size; u++)
            {
                int x = x0 + u;
                int y = y0 + v;
                int edge = Mathf.Min(Mathf.Min(u, v), Mathf.Min(size - 1 - u, size - 1 - v));
                if (edge < 2 && Hash01(x, y, 11) < 0.4f * (2 - edge) / 2f)
                {
                    continue; // Ragged brush edge.
                }

                float diagonal = (u + v) / (2f * size);
                float light = 1.08f - 0.2f * diagonal;
                if (u < rim || v < rim) light *= 1.14f;
                if (u >= size - rim || v >= size - rim) light *= 0.76f;
                light *= 0.9f + 0.2f * Grain(x, y);

                var paint = new Color32(
                    (byte)Mathf.Clamp(color.r * light, 0f, 255f),
                    (byte)Mathf.Clamp(color.g * light, 0f, 255f),
                    (byte)Mathf.Clamp(color.b * light, 0f, 255f),
                    255);
                Blend(x, y, paint, 0.96f);

                if (edge == 0 || edge == 1 && Hash01(x, y, 13) < 0.3f)
                {
                    Blend(x, y, Outline, 0.55f * (0.6f + 0.4f * Grain(x, y)));
                }
            }
        }
    }

    // A pen stroke along one grid line. Ink density follows the paper grain and the line
    // drifts sideways by a pixel now and then.
    private void InkLine(int x, int y, int length, bool horizontal, int width, int seed)
    {
        for (int t = 0; t < length; t++)
        {
            int wobble = Mathf.RoundToInt((Noise(t / 46f, seed) - 0.5f) * 2.4f);
            for (int w = 0; w < width; w++)
            {
                int px = horizontal ? x + t : x + wobble + w - width / 2;
                int py = horizontal ? y + wobble + w - width / 2 : y + t;
                float density = 0.5f + 0.35f * Grain(px, py) + 0.15f * Noise(t / 9f, seed + 5);
                Blend(px, py, Outline, density * (w == 0 || w == width - 1 ? 0.75f : 1f));
            }
        }
    }

// The player is a wax seal in the colour it carries: drop shadow, darker pressed rim,
    // a stamped inner ring, shading from the top-left and a small glint.
    private void DrawPlayer()
    {
        float cx = (player.x + 1) * TileSize + TileSize * 0.5f;
        float cy = (player.y + 1) * TileSize + TileSize * 0.5f;
        float r = TileSize * 0.36f;
        float shadowOffset = TilePx(1.4f);
        Color32 wax = Colors[currentColor];

        int x0 = Mathf.FloorToInt(cx - r - 2f);
        int y0 = Mathf.FloorToInt(cy - r - 2f);
        int span = Mathf.CeilToInt(r * 2f + 4f + shadowOffset);
        for (int y = y0; y < y0 + span; y++)
        {
            for (int x = x0; x < x0 + span; x++)
            {
                float sx = x + 0.5f - cx - shadowOffset;
                float sy = y + 0.5f - cy - shadowOffset;
                float shadowCover = Mathf.Clamp01(r + 1.5f - Mathf.Sqrt(sx * sx + sy * sy));
                Blend(x, y, Outline, shadowCover * 0.35f);
            }
        }

        for (int y = y0; y < y0 + span; y++)
        {
            for (int x = x0; x < x0 + span; x++)
            {
                float dx = x + 0.5f - cx;
                float dy = y + 0.5f - cy;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float cover = Mathf.Clamp01(r + 0.5f - d);
                if (cover <= 0f)
                {
                    continue;
                }

                float light = 1f - 0.28f * (dx + dy) / (r * 1.41f);
                if (d > r * 0.78f) light *= 0.72f;
                float ring = Mathf.Abs(d - r * 0.48f);
                if (ring < 1.2f) light *= dx + dy < 0f ? 0.8f : 1.2f; // Stamp edge catches light below-right.
                else if (d < r * 0.48f) light *= 0.9f;
                light *= 0.94f + 0.12f * Grain(x, y);

                var body = new Color32(
                    (byte)Mathf.Clamp(wax.r * light, 0f, 255f),
                    (byte)Mathf.Clamp(wax.g * light, 0f, 255f),
                    (byte)Mathf.Clamp(wax.b * light, 0f, 255f),
                    255);
                Blend(x, y, body, cover);

                float gx = dx + r * 0.38f;
                float gy = dy + r * 0.38f;
                float glint = Mathf.Clamp01(r * 0.16f - Mathf.Sqrt(gx * gx + gy * gy) + 0.5f);
                Blend(x, y, new Color32(255, 248, 230, 255), glint * 0.6f);

                float edgeInk = Mathf.Clamp01(1.4f - Mathf.Abs(d - r));
                Blend(x, y, Outline, edgeInk * 0.7f);
            }
        }
    }


    // Thin aged wash over the board area; the letter's paper shows through it.
// The board has no background of its own: the letter's paper shows straight through.
    private void DrawPaper()
    {
        System.Array.Clear(pixels, 0, pixels.Length);
    }

    // Static candle light over the finished board: brighter and warmer at the top-left,
    // falling off toward the bottom-right, darker toward the edges.
    private void ApplyLighting()
    {
        float inv = 1f / CanvasSize;
        for (int y = 0; y < CanvasSize; y++)
        {
            for (int x = 0; x < CanvasSize; x++)
            {
                float nx = x * inv;
                float ny = y * inv;
                float ex = nx - 0.5f;
                float ey = ny - 0.5f;
                float vignette = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.42f, 0.74f, Mathf.Sqrt(ex * ex + ey * ey)));
                float light = (1.07f - 0.17f * (nx + ny) * 0.5f) * (1f - 0.3f * vignette);

                int i = Index(x, y);
                Color32 c = pixels[i];
                c.r = (byte)Mathf.Clamp(c.r * light * 1.04f, 0f, 255f);
                c.g = (byte)Mathf.Clamp(c.g * light, 0f, 255f);
                c.b = (byte)Mathf.Clamp(c.b * light * 0.9f, 0f, 255f);
                pixels[i] = c;
            }
        }
    }

    private static int TilePx(float sketchPixels)
    {
        return Mathf.FloorToInt(sketchPixels * TileSize / SketchTile + 0.5f);
    }

    // Alpha-blends a rectangle measured from the top-left, like p5's rect().
private void Fill(int x, int y, int width, int height, Color32 color)
    {
        float alpha = color.a / 255f;
        color.a = 255;
        for (int row = y; row < y + height; row++)
        {
            for (int col = x; col < x + width; col++)
            {
                Blend(col, row, color, alpha);
            }
        }
    }

    // Paints color over one canvas pixel (top-left origin), keeping the canvas's own
    // transparency so the letter shows through thin washes.
    private void Blend(int x, int y, Color32 color, float alpha)
    {
        if (x < 0 || y < 0 || x >= CanvasSize || y >= CanvasSize || alpha <= 0f)
        {
            return;
        }
        int i = Index(x, y);
        Color32 under = pixels[i];
        float a = Mathf.Clamp01(alpha);
        float ua = under.a / 255f;
        float outA = a + ua * (1f - a);
        if (outA <= 0f)
        {
            return;
        }
        pixels[i] = new Color32(
            (byte)((color.r * a + under.r * ua * (1f - a)) / outA),
            (byte)((color.g * a + under.g * ua * (1f - a)) / outA),
            (byte)((color.b * a + under.b * ua * (1f - a)) / outA),
            (byte)(outA * 255f));
    }

    // Texture rows run bottom-up; the canvas is drawn top-down.
    private static int Index(int x, int y)
    {
        return (CanvasSize - 1 - y) * CanvasSize + x;
    }

    // Static paper grain, 0..1: soft blotches, finer mottling, horizontal fibres and the
    // odd dark fleck. Built once, so every redraw shares the same paper.
    private float Grain(int x, int y)
    {
        if (grain == null || grain.Length != CanvasSize * CanvasSize)
        {
            grain = new float[CanvasSize * CanvasSize];
            for (int gy = 0; gy < CanvasSize; gy++)
            {
                for (int gx = 0; gx < CanvasSize; gx++)
                {
                    float n = ValueNoise(gx / 60f, gy / 60f, 1) * 0.45f
                        + ValueNoise(gx / 14f, gy / 14f, 2) * 0.3f
                        + ValueNoise(gx / 24f, gy / 2.5f, 3) * 0.25f;
                    if (Hash01(gx, gy, 4) < 0.003f) n -= 0.4f;
                    grain[gy * CanvasSize + gx] = Mathf.Clamp01(n);
                }
            }
        }
        x = Mathf.Clamp(x, 0, CanvasSize - 1);
        y = Mathf.Clamp(y, 0, CanvasSize - 1);
        return grain[y * CanvasSize + x];
    }

    private static float Noise(float t, int seed)
    {
        return ValueNoise(t, seed * 3.7f, seed);
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

    // Recreates the board buffer and texture if they were lost (e.g. a script reload while
    // playing), and hands the texture back to the board image.
    private void EnsureCanvas()
    {
        if (pixels == null || pixels.Length != CanvasSize * CanvasSize)
        {
            pixels = new Color32[CanvasSize * CanvasSize];
        }
        if (texture == null)
        {
            texture = new Texture2D(CanvasSize, CanvasSize, TextureFormat.RGBA32, true)
            {
                name = "Clue minigame",
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            Transform image = boardRoot != null ? boardRoot.Find("Board image") : null;
            if (image != null)
            {
                image.GetComponent<RawImage>().texture = texture;
            }
        }
    }

    private void BuildView(RectTransform parent)
    {
        surface = new GameObject("Clue minigame", typeof(RectTransform)).GetComponent<RectTransform>();
        surface.SetParent(parent, false);
        surface.anchorMin = surface.anchorMax = new Vector2(0.5f, 0.5f);
        surface.anchoredPosition = Vector2.zero;

        letterImage = new GameObject("Letter", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        letterImage.transform.SetParent(surface, false);
        letterImage.raycastTarget = false;
        letterImage.rectTransform.anchorMin = Vector2.zero;
        letterImage.rectTransform.anchorMax = Vector2.one;
        letterImage.rectTransform.offsetMin = letterImage.rectTransform.offsetMax = Vector2.zero;

        boardRoot = new GameObject("Board", typeof(RectTransform)).GetComponent<RectTransform>();
        boardRoot.SetParent(surface, false);
        boardRoot.anchorMin = boardRoot.anchorMax = OnLetter(BoardCentre);
        boardRoot.anchoredPosition = Vector2.zero;

        pixels = new Color32[CanvasSize * CanvasSize];
        // Mipmaps keep the thin grid lines even when the view is shrunk on small screens.
        texture = new Texture2D(CanvasSize, CanvasSize, TextureFormat.RGBA32, true)
        {
            name = "Clue minigame",
            filterMode = FilterMode.Trilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        CreateTile("Board image", boardRoot, 0, 0, BorderSize).texture = texture;

        // Drawn on the paper under the board (see BuildPaperLines).
        RawImage lines = new GameObject("Paper lines", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        lines.transform.SetParent(surface, false);
        lines.transform.SetSiblingIndex(letterImage.transform.GetSiblingIndex() + 1);
        lines.raycastTarget = false;
        lines.rectTransform.anchorMin = Vector2.zero;
        lines.rectTransform.anchorMax = Vector2.one;
        lines.rectTransform.offsetMin = lines.rectTransform.offsetMax = Vector2.zero;
        border = lines.gameObject;


        // Written on the paper beside the board.
        stepsLabel = CreateLabel("Steps left", surface, Ink);
        RectTransform steps = stepsLabel.rectTransform;
        steps.anchorMin = steps.anchorMax = OnLetter(StepsCentre);
        steps.sizeDelta = Vector2.zero;

        // Written on the paper's other margin: the next two colours in the sequence.
        hint = new GameObject("Colour hint", typeof(RectTransform));
        RectTransform hintRect = (RectTransform)hint.transform;
        hintRect.SetParent(surface, false);
        hintRect.anchorMin = Vector2.zero;
        hintRect.anchorMax = Vector2.one;
        hintRect.offsetMin = hintRect.offsetMax = Vector2.zero;
        hintHeading = CreateLabel("Next", hintRect, Ink);
        hintHeading.text = "Next";
        PlaceOnLetter(hintHeading.rectTransform, new Vector2(HintX, 58f), Vector2.zero);
        nextSwatch = CreateSwatch("Next colour", hintRect, new Vector2(HintX, 86f));
        hintThen = CreateLabel("Then", hintRect, Ink);
        hintThen.text = "then";
        PlaceOnLetter(hintThen.rectTransform, new Vector2(HintX, 114f), Vector2.zero);
        afterSwatch = CreateSwatch("Colour after", hintRect, new Vector2(HintX, 142f));

        // Revealed on the paper once the puzzle is solved.
        clueInk = new GameObject("Clue", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        clueInk.transform.SetParent(surface, false);
        clueInk.raycastTarget = false;
        clueInk.gameObject.SetActive(false);
    }

    // A colour hint on the letter: shows a painted tile, the same as on the board.
    private static RawImage CreateSwatch(string name, Transform parent, Vector2 centre)
    {
        RawImage swatch = new GameObject(name, typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        swatch.transform.SetParent(parent, false);
        swatch.raycastTarget = false;
        PlaceOnLetter(swatch.rectTransform, centre, new Vector2(HintSwatch, HintSwatch));
        return swatch;
    }

    // Paints each colour's tile with PaintTile into the board buffer and copies it out into
    // its own texture. Runs at the start of Redraw, which repaints the buffer right after.
    // Rebuilt whenever the textures are missing (first use, or a script reload while playing).
    private void EnsureSwatches()
    {
        if (swatchTextures != null && swatchTextures.Length == Colors.Length && System.Array.TrueForAll(swatchTextures, t => t != null))
        {
            return;
        }

        int inset = TilePx(1.6f);
        int face = TileSize - inset * 2;
        int size = face + TilePx(1.2f) + 2;
        swatchTextures = new Texture2D[Colors.Length];
        for (int colour = 0; colour < Colors.Length; colour++)
        {
            System.Array.Clear(pixels, 0, pixels.Length);
            PaintTile(1, 1, face, Colors[colour]);
            var swatch = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    swatch[(size - 1 - y) * size + x] = pixels[Index(x, y)];
                }
            }
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = "Clue swatch " + colour,
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            tex.SetPixels32(swatch);
            tex.Apply(true);
            swatchTextures[colour] = tex;
        }
        System.Array.Clear(pixels, 0, pixels.Length);
    }

    // Spans a box measured in Letter_screen pixels, so it scales with the letter.
    private static void PlaceOnLetter(RectTransform rect, Vector2 centre, Vector2 size)
    {
        rect.anchorMin = OnLetter(centre + new Vector2(-size.x, size.y) * 0.5f);
        rect.anchorMax = OnLetter(centre + new Vector2(size.x, -size.y) * 0.5f);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private static Vector2 OnLetter(Vector2 letterPixels)
    {
        return new Vector2(letterPixels.x / LetterSize.x, 1f - letterPixels.y / LetterSize.y);
    }

// Continues the board's grid onto the letter, fading with distance from the play area,
    // and only where the letter is plain paper (sampled from the letter image itself), so
    // the lines never run over its edges or decoration. Static: built once per letter sprite.
    private void BuildPaperLines(Sprite letter)
    {
        paperLinesFor = letter;
        int lw = letter != null ? Mathf.RoundToInt(letter.rect.width) : (int)LetterSize.x;
        int lh = letter != null ? Mathf.RoundToInt(letter.rect.height) : (int)LetterSize.y;
        Color32[] paper = letter != null ? ReadSprite(letter, lw, lh) : null;

        float tile = BoardSide / BorderSize;
        float left = BoardCentre.x - BoardSide * 0.5f + tile;
        float top = BoardCentre.y - BoardSide * 0.5f + tile;
        float play = GridSize * tile;
        float fadeDistance = PaperLineFade * tile;
        float halfWidth = tile / TileSize * PaperLineScale; // One board-canvas pixel, in texels.

        // The paper's own colour, averaged under the board where the letter is blank.
        Vector3 reference = new Vector3(214f, 196f, 156f);
        if (paper != null)
        {
            Vector3 sum = Vector3.zero;
            int count = 0;
            for (int y = Mathf.Max(0, (int)top); y < Mathf.Min(lh, (int)(top + play)); y += 2)
            {
                for (int x = Mathf.Max(0, (int)left); x < Mathf.Min(lw, (int)(left + play)); x += 2)
                {
                    Color32 c = paper[(lh - 1 - y) * lw + x];
                    if (c.a > 200)
                    {
                        sum += new Vector3(c.r, c.g, c.b);
                        count++;
                    }
                }
            }
            if (count > 0) reference = sum / count;
        }

        int w = lw * PaperLineScale;
        int h = lh * PaperLineScale;
        var texels = new Color32[w * h];
        for (int ty = 0; ty < h; ty++)
        {
            float ly = (ty + 0.5f) / PaperLineScale; // Letter pixels from the top.
            for (int tx = 0; tx < w; tx++)
            {
                float lx = (tx + 0.5f) / PaperLineScale;
                float dx = Mathf.Max(Mathf.Max(left - lx, lx - (left + play)), 0f);
                float dy = Mathf.Max(Mathf.Max(top - ly, ly - (top + play)), 0f);
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d <= 0f || d >= fadeDistance)
                {
                    continue; // The board draws inside the play area itself.
                }

                float gx = (lx - left) / tile;
                float gy = (ly - top) / tile;
                int kx = Mathf.RoundToInt(gx);
                int ky = Mathf.RoundToInt(gy);
                float vertical = Mathf.Clamp01(halfWidth + 0.5f - Mathf.Abs(gx - kx) * tile * PaperLineScale);
                float horizontal = Mathf.Clamp01(halfWidth + 0.5f - Mathf.Abs(gy - ky) * tile * PaperLineScale);
                // Ink density varies along each line, like the pen grid on the board.
                vertical *= 0.6f + 0.4f * ValueNoise(gy * 0.7f, kx * 3.1f, 21);
                horizontal *= 0.6f + 0.4f * ValueNoise(gx * 0.7f, ky * 3.1f, 22);
                float ink = Mathf.Max(vertical, horizontal);
                if (ink <= 0f)
                {
                    continue;
                }

                float fade = 1f - d / fadeDistance;
                fade *= fade;

                float paperness = 1f;
                if (paper != null)
                {
                    Color32 c = paper[(lh - 1 - Mathf.Min(lh - 1, (int)ly)) * lw + Mathf.Min(lw - 1, (int)lx)];
                    float diff = (Mathf.Abs(c.r - reference.x) + Mathf.Abs(c.g - reference.y) + Mathf.Abs(c.b - reference.z)) / 3f;
                    paperness = c.a < 128 ? 0f : 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(18f, 45f, diff));
                }

                Color32 line = Outline;
                line.a = (byte)(255f * Mathf.Clamp01(ink * fade * paperness * 0.85f));
                texels[(h - 1 - ty) * w + tx] = line;
            }
        }

        if (paperLinesTexture == null || paperLinesTexture.width != w || paperLinesTexture.height != h)
        {
            if (paperLinesTexture != null) Destroy(paperLinesTexture);
            paperLinesTexture = new Texture2D(w, h, TextureFormat.RGBA32, true)
            {
                name = "Clue paper lines",
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
        }
        paperLinesTexture.SetPixels32(texels);
        paperLinesTexture.Apply(true);
        border.GetComponent<RawImage>().texture = paperLinesTexture;
    }

    // Reads a sprite's pixels through the GPU, so the texture needs no Read/Write flag.
    private static Color32[] ReadSprite(Sprite sprite, int width, int height)
    {
        Texture source = sprite.texture;
        Rect r = sprite.textureRect;
        RenderTexture target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(source, target, new Vector2(r.width / source.width, r.height / source.height),
            new Vector2(r.x / source.width, r.y / source.height));
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = target;
        var readback = new Texture2D(width, height, TextureFormat.RGBA32, false);
        readback.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
        Color32[] pixels = readback.GetPixels32();
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(target);
        Destroy(readback);
        return pixels;
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

private static PixelText CreateLabel(string name, Transform parent, Color color)
    {
        PixelText label = new GameObject(name, typeof(RectTransform), typeof(PixelText)).GetComponent<PixelText>();
        label.transform.SetParent(parent, false);
        label.color = color;
        label.alignment = TextAnchor.MiddleCenter;
        label.raycastTarget = false;
        return label;
    }
}
