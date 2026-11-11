using System;
using System.Collections.Generic;
using UnityEngine;

// A cell of the hex board, in axial coordinates. The hexes are "pointy-top" seen from above:
// flat sides face +x and -x, so direction 0 is +x and the rest follow anticlockwise (towards +z)
// every 60 degrees.
public readonly struct Hex : IEquatable<Hex>
{
    public readonly int Q;
    public readonly int R;

    public static readonly Hex[] Directions =
    {
        new Hex(1, 0), new Hex(0, 1), new Hex(-1, 1), new Hex(-1, 0), new Hex(0, -1), new Hex(1, -1)
    };

    public Hex(int q, int r)
    {
        Q = q;
        R = r;
    }

    public Hex Neighbour(int direction)
    {
        Hex step = Directions[((direction % 6) + 6) % 6];
        return new Hex(Q + step.Q, R + step.R);
    }

    public static int Distance(Hex a, Hex b)
    {
        int dq = a.Q - b.Q, dr = a.R - b.R;
        return (Math.Abs(dq) + Math.Abs(dr) + Math.Abs(dq + dr)) / 2;
    }

    public bool Equals(Hex other) => Q == other.Q && R == other.R;
    public override bool Equals(object obj) => obj is Hex other && Equals(other);
    public override int GetHashCode() => (Q * 73856093) ^ (R * 19349663);
    public static bool operator ==(Hex a, Hex b) => a.Equals(b);
    public static bool operator !=(Hex a, Hex b) => !a.Equals(b);
    public override string ToString() => "(" + Q + ", " + R + ")";
}

// Where the hexes sit in the world: centres, corners and which hex a point falls in. Radius is
// centre to corner; neighbouring centres are sqrt(3) x radius apart.
public readonly struct HexGrid
{
    public readonly Vector2 Origin;
    public readonly float Radius;

    public HexGrid(Vector2 origin, float radius)
    {
        Origin = origin;
        Radius = Mathf.Max(0.01f, radius);
    }

    public float Width => Mathf.Sqrt(3f) * Radius;

    public Vector2 Centre(Hex hex) =>
        Origin + new Vector2(Width * (hex.Q + hex.R * 0.5f), 1.5f * Radius * hex.R);

    public Vector3 Centre(Hex hex, float y)
    {
        Vector2 c = Centre(hex);
        return new Vector3(c.x, y, c.y);
    }

    // Corner i sits between edge i-1 and edge i; edge i (corner i to corner i+1) faces direction i.
    public Vector2 Corner(Hex hex, int corner)
    {
        float angle = (60f * corner - 30f) * Mathf.Deg2Rad;
        return Centre(hex) + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Radius;
    }

    public Hex At(float x, float z)
    {
        float px = x - Origin.x, pz = z - Origin.y;
        float r = pz / (1.5f * Radius);
        float q = px / Width - r * 0.5f;
        // Round in cube coordinates so the nearest centre always wins.
        float s = -q - r;
        int rq = Mathf.RoundToInt(q), rr = Mathf.RoundToInt(r), rs = Mathf.RoundToInt(s);
        float dq = Mathf.Abs(rq - q), dr = Mathf.Abs(rr - r), ds = Mathf.Abs(rs - s);
        if (dq > dr && dq > ds) rq = -rr - rs;
        else if (dr > ds) rr = -rq - rs;
        return new Hex(rq, rr);
    }
}

// The rules of the lighthouse's final game, with nothing in the world: the island is a set of
// hexes, the houses are walls from the start, and each turn the player strikes one hex with the
// lighthouse's light (it sinks into the sea) and then the villain takes one step towards the
// shore by the shortest way. Standing on a shore hex (one the board does not surround on every
// side) at his turn, he gets away into the sea; once no way to the shore is left he is trapped.
public sealed class MouseTrapBoard
{
    public enum Outcome { Playing, Trapped, Escaped }

    public enum StepKind { None, Moved, Escaped, Trapped }

    public readonly struct Step
    {
        public readonly StepKind Kind;
        public readonly Hex From;
        public readonly Hex To;
        // Escaped: the direction he leaves the board in (towards the sea).
        public readonly int Direction;

        public Step(StepKind kind, Hex from, Hex to, int direction)
        {
            Kind = kind;
            From = from;
            To = to;
            Direction = direction;
        }
    }

    private readonly HashSet<Hex> cells;
    private readonly HashSet<Hex> walls;
    private readonly HashSet<Hex> sunk = new HashSet<Hex>();
    private readonly System.Random random;

    public Hex Mouse { get; private set; }
    public Outcome State { get; private set; } = Outcome.Playing;
    public int Strikes { get; private set; }
    public IReadOnlyCollection<Hex> Cells => cells;
    public IReadOnlyCollection<Hex> Walls => walls;
    public IReadOnlyCollection<Hex> Sunk => sunk;

    public MouseTrapBoard(IEnumerable<Hex> cells, IEnumerable<Hex> walls, int seed)
    {
        this.cells = new HashSet<Hex>(cells);
        this.walls = new HashSet<Hex>();
        if (walls != null)
            foreach (Hex wall in walls)
                if (this.cells.Contains(wall)) this.walls.Add(wall);
        random = new System.Random(seed);
        List<Hex> start = CentralCells();
        Mouse = start.Count > 0 ? start[random.Next(start.Count)] : default;
    }

    public bool Contains(Hex hex) => cells.Contains(hex);
    public bool IsWall(Hex hex) => walls.Contains(hex);
    public bool IsSunk(Hex hex) => sunk.Contains(hex);
    public bool IsOpen(Hex hex) => cells.Contains(hex) && !walls.Contains(hex) && !sunk.Contains(hex);

    // A shore hex: the board does not surround it, so from it the villain can reach the sea.
    public bool IsShore(Hex hex)
    {
        if (!cells.Contains(hex)) return false;
        for (int d = 0; d < 6; d++)
            if (!cells.Contains(hex.Neighbour(d))) return true;
        return false;
    }

    public bool CanStrike(Hex hex) => State == Outcome.Playing && IsOpen(hex) && hex != Mouse;

    public bool Strike(Hex hex)
    {
        if (!CanStrike(hex)) return false;
        sunk.Add(hex);
        Strikes++;
        return true;
    }

    // Puts the villain on a hex of the caller's choosing (tests, a fixed start).
    public void PlaceMouse(Hex hex)
    {
        if (IsOpen(hex)) Mouse = hex;
    }

    public Step MouseTurn()
    {
        if (State != Outcome.Playing) return new Step(StepKind.None, Mouse, Mouse, -1);
        if (IsShore(Mouse))
        {
            var ways = new List<int>();
            for (int d = 0; d < 6; d++)
                if (!cells.Contains(Mouse.Neighbour(d))) ways.Add(d);
            int direction = ways[random.Next(ways.Count)];
            State = Outcome.Escaped;
            return new Step(StepKind.Escaped, Mouse, Mouse.Neighbour(direction), direction);
        }
        Dictionary<Hex, int> toShore = ShoreDistances();
        if (!toShore.TryGetValue(Mouse, out int here))
        {
            State = Outcome.Trapped;
            return new Step(StepKind.Trapped, Mouse, Mouse, -1);
        }
        var best = new List<Hex>();
        for (int d = 0; d < 6; d++)
        {
            Hex next = Mouse.Neighbour(d);
            if (IsOpen(next) && toShore.TryGetValue(next, out int distance) && distance == here - 1) best.Add(next);
        }
        Hex from = Mouse;
        Mouse = best[random.Next(best.Count)];
        return new Step(StepKind.Moved, from, Mouse, -1);
    }

    // Whether the villain could still reach the shore (he is not yet shut in).
    public bool CanReachShore() => ShoreDistances().ContainsKey(Mouse);

    // Steps from each open hex to the nearest open shore hex, over open hexes. The villain's own
    // hex counts as open.
    public Dictionary<Hex, int> ShoreDistances()
    {
        var distance = new Dictionary<Hex, int>();
        var queue = new Queue<Hex>();
        foreach (Hex cell in cells)
        {
            if (IsOpen(cell) && IsShore(cell))
            {
                distance[cell] = 0;
                queue.Enqueue(cell);
            }
        }
        while (queue.Count > 0)
        {
            Hex cell = queue.Dequeue();
            for (int d = 0; d < 6; d++)
            {
                Hex next = cell.Neighbour(d);
                if (IsOpen(next) && !distance.ContainsKey(next))
                {
                    distance[next] = distance[cell] + 1;
                    queue.Enqueue(next);
                }
            }
        }
        return distance;
    }

    // Steps from each hex of the board to the edge of the board, ignoring walls and sunk hexes:
    // how deep inland a hex is.
    public Dictionary<Hex, int> Depths()
    {
        var depth = new Dictionary<Hex, int>();
        var queue = new Queue<Hex>();
        foreach (Hex cell in cells)
        {
            if (IsShore(cell))
            {
                depth[cell] = 0;
                queue.Enqueue(cell);
            }
        }
        while (queue.Count > 0)
        {
            Hex cell = queue.Dequeue();
            for (int d = 0; d < 6; d++)
            {
                Hex next = cell.Neighbour(d);
                if (cells.Contains(next) && !depth.ContainsKey(next))
                {
                    depth[next] = depth[cell] + 1;
                    queue.Enqueue(next);
                }
            }
        }
        return depth;
    }

    // Where the villain may start: open hexes in the heart of the island, as deep inland as any
    // open hex less one, and not shut in by the houses.
    public List<Hex> CentralCells()
    {
        Dictionary<Hex, int> depth = Depths();
        Dictionary<Hex, int> toShore = ShoreDistances();
        int deepest = -1;
        foreach (var pair in depth)
            if (IsOpen(pair.Key) && toShore.ContainsKey(pair.Key)) deepest = Mathf.Max(deepest, pair.Value);
        var central = new List<Hex>();
        foreach (var pair in depth)
            if (IsOpen(pair.Key) && toShore.ContainsKey(pair.Key) && pair.Value >= deepest - 1) central.Add(pair.Key);
        // Hash sets don't keep an order; sort so a seed always gives the same start.
        central.Sort((a, b) => a.Q != b.Q ? a.Q.CompareTo(b.Q) : a.R.CompareTo(b.R));
        return central;
    }
}
