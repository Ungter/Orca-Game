using System.Collections.Generic;
using UnityEngine;

// The island surveyed for the clue letter's potion hunt. The ground block (IslandDemo:pCube1)
// is cut into a checkerboard of cells, CellsAcross along its longer side, and a finer walk grid
// of SamplesPerCell spots to a cell side records what each spot is (sea, grass, path, trees, a
// building...), the height the player stands at there, whether the sky is open above it, and
// which neighbouring spots the player can walk between. Routes are counted in cells crossed,
// the same steps the hunt charges, so a step budget built from them is always walkable.
// Surveying is all physics queries; Advance runs it a slice at a time so no frame stalls.
public sealed class ClueMap
{
    public enum Kind : byte { Water, Ground, Paved, Dock, Foliage, Building, Structure }

    public const int CellsAcross = 24;
    public const int SamplesPerCell = 8;
    public const int Unreachable = int.MaxValue;

    // Potions keep at least this many cells from the start and each other while there is room,
    // and are picked at random among the spots at least SpreadShare as far from the rest as the
    // farthest one, so they spread over the whole island.
    private const int MinSeparation = 4;
    private const float SpreadShare = 0.65f;
    // The budget is the shortest route through every potion plus this share, at least MinSlack.
    // Walking isn't stepping: corners get cut and borders crossed twice, so it is roomier than
    // the paper board's 10%.
    private const float BudgetSlack = 0.25f;
    private const int MinSlack = 4;

    // The player's capsule (CharacterController radius 0.5, height 1.82, step offset 0.3).
    private const float Radius = 0.45f;
    private const float Height = 1.82f;
    private const float StepClearance = 0.3f;
    // Wide raised surfaces up to this high (paths, slabs, the plaza, the sideWalkAlch cubes) are
    // walked on; taller ones are in the way.
    private const float MaxPavedHeight = 2f;
    // Neighbouring spots further apart in height than this aren't one step apart.
    private const float MaxClimb = 1f;
    // Rays between neighbours at these heights above the feet find walls, fences and trunks.
    private static readonly float[] PassHeights = { 0.5f, 1.3f };
    private static readonly string[] FoliageNames = { "tree", "hedge", "shrub", "bush" };

    public readonly Collider Ground;
    public readonly Vector2 Origin;
    public readonly float CellSize;
    public readonly float SampleSize;
    public readonly int CellsX;
    public readonly int CellsZ;
    public readonly int SamplesX;
    public readonly int SamplesZ;
    public readonly Kind[] Kinds;
    // Where the player stands at each spot (world y); NaN where there is nothing to stand on.
    public readonly float[] Heights;
    public readonly bool[] Walkable;
    // Nothing overhead: the first thing a ray from the sky meets is what the player stands on.
    public readonly bool[] Open;
    // Open, walkable and with room around it for a potion.
    public readonly bool[] Roomy;

    private readonly Collider ocean;
    private readonly HashSet<Collider> ignored;
    private readonly float seaLevel;
    private readonly float skyY;
    // Bit 0: the player can walk to the +x neighbour, bit 1: to the +z neighbour.
    private readonly byte[] edges;
    private readonly Dictionary<Collider, Kind> owners = new Dictionary<Collider, Kind>();
    private readonly RaycastHit[] hits = new RaycastHit[64];
    private readonly Collider[] overlaps = new Collider[32];
    private int cursor;

    public bool IsReady { get; private set; }
    public int SampleCount => Kinds.Length;
    public float Progress => Mathf.Clamp01(cursor / (2f * SampleCount));

    // ignore: colliders that move about and must not count (the player).
    public ClueMap(Collider ground, Collider ocean, IEnumerable<Collider> ignore)
    {
        Ground = ground;
        this.ocean = ocean;
        ignored = new HashSet<Collider>(ignore ?? new Collider[0]);
        Bounds bounds = ground.bounds;
        CellSize = Mathf.Max(bounds.size.x, bounds.size.z) / CellsAcross;
        SampleSize = CellSize / SamplesPerCell;
        CellsX = Mathf.Max(1, Mathf.CeilToInt(bounds.size.x / CellSize - 0.001f));
        CellsZ = Mathf.Max(1, Mathf.CeilToInt(bounds.size.z / CellSize - 0.001f));
        Origin = new Vector2(bounds.center.x - CellsX * CellSize * 0.5f, bounds.center.z - CellsZ * CellSize * 0.5f);
        SamplesX = CellsX * SamplesPerCell;
        SamplesZ = CellsZ * SamplesPerCell;
        seaLevel = ocean != null ? ocean.bounds.max.y : bounds.min.y;
        skyY = bounds.max.y + 150f;
        int count = SamplesX * SamplesZ;
        Kinds = new Kind[count];
        Heights = new float[count];
        Walkable = new bool[count];
        Open = new bool[count];
        Roomy = new bool[count];
        edges = new byte[count];
    }

    // Surveys up to budget more spots (or the whole island with int.MaxValue). True once done.
    public bool Advance(int budget)
    {
        if (IsReady) return true;
        bool backfaces = Physics.queriesHitBackfaces;
        // Single-sided walls and fences still block a ray coming from behind them.
        Physics.queriesHitBackfaces = true;
        try
        {
            int count = SampleCount;
            for (int done = 0; done < budget && cursor < count * 2; done++, cursor++)
            {
                if (cursor < count) SurveySpot(cursor);
                else SurveyEdges(cursor - count);
            }
        }
        finally
        {
            Physics.queriesHitBackfaces = backfaces;
        }
        IsReady = cursor >= SampleCount * 2;
        return IsReady;
    }

    public int SampleAt(Vector3 world)
    {
        int sx = Mathf.FloorToInt((world.x - Origin.x) / SampleSize);
        int sz = Mathf.FloorToInt((world.z - Origin.y) / SampleSize);
        return sx < 0 || sz < 0 || sx >= SamplesX || sz >= SamplesZ ? -1 : sz * SamplesX + sx;
    }

    public Vector2Int CellAt(Vector3 world)
    {
        return new Vector2Int(Mathf.FloorToInt((world.x - Origin.x) / CellSize), Mathf.FloorToInt((world.z - Origin.y) / CellSize));
    }

    public Vector2Int CellOf(int sample)
    {
        return new Vector2Int(sample % SamplesX / SamplesPerCell, sample / SamplesX / SamplesPerCell);
    }

    // A cell's footprint in world x (Rect x) and z (Rect y).
    public Rect CellRect(Vector2Int cell)
    {
        return new Rect(Origin.x + cell.x * CellSize, Origin.y + cell.y * CellSize, CellSize, CellSize);
    }

    public Vector3 SamplePosition(int sample)
    {
        float y = Heights[sample];
        return new Vector3(Origin.x + (sample % SamplesX + 0.5f) * SampleSize, float.IsNaN(y) ? 0f : y,
            Origin.y + (sample / SamplesX + 0.5f) * SampleSize);
    }

    // Island land, including paths and what stands on it; not the sea or the pier.
    public bool IsLand(int sample) => sample >= 0 && Kinds[sample] != Kind.Water && Kinds[sample] != Kind.Dock;

    // The closest walkable land spot within reach samples of a point, or -1.
    public int NearestWalkable(Vector3 world, int reach)
    {
        int sx = Mathf.FloorToInt((world.x - Origin.x) / SampleSize);
        int sz = Mathf.FloorToInt((world.z - Origin.y) / SampleSize);
        int best = -1;
        float bestDistance = float.MaxValue;
        for (int dz = -reach; dz <= reach; dz++)
        {
            for (int dx = -reach; dx <= reach; dx++)
            {
                int x = sx + dx, z = sz + dz;
                if (x < 0 || z < 0 || x >= SamplesX || z >= SamplesZ) continue;
                int sample = z * SamplesX + x;
                if (!Walkable[sample] || !IsLand(sample)) continue;
                Vector3 p = SamplePosition(sample);
                float distance = (p.x - world.x) * (p.x - world.x) + (p.z - world.z) * (p.z - world.z);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = sample;
                }
            }
        }
        return best;
    }

    // Fewest cells crossed walking from source to every spot (Unreachable where it can't be
    // walked to). A breadth-first search where a move inside a cell is free and a move over a
    // cell border costs one step.
    public void Distances(int source, int[] into)
    {
        for (int i = 0; i < into.Length; i++) into[i] = Unreachable;
        if (source < 0) return;
        int capacity = SampleCount * 4 + 1;
        var deque = new int[capacity];
        int head = 0, tail = 0;
        into[source] = 0;
        deque[tail] = source;
        tail = (tail + 1) % capacity;
        while (head != tail)
        {
            int s = deque[head];
            head = (head + 1) % capacity;
            int sx = s % SamplesX, sz = s / SamplesX;
            for (int n = 0; n < 4; n++)
            {
                int t;
                if (n == 0) t = (edges[s] & 1) != 0 ? s + 1 : -1;
                else if (n == 1) t = sx > 0 && (edges[s - 1] & 1) != 0 ? s - 1 : -1;
                else if (n == 2) t = (edges[s] & 2) != 0 ? s + SamplesX : -1;
                else t = sz > 0 && (edges[s - SamplesX] & 2) != 0 ? s - SamplesX : -1;
                if (t < 0) continue;
                int cost = CellOf(s) == CellOf(t) ? 0 : 1;
                int distance = into[s] + cost;
                if (distance >= into[t]) continue;
                into[t] = distance;
                if (cost == 0)
                {
                    head = (head - 1 + capacity) % capacity;
                    deque[head] = t;
                }
                else
                {
                    deque[tail] = t;
                    tail = (tail + 1) % capacity;
                }
            }
        }
    }

    // Places count potions around the island for a hunt starting at the start spot, each in a
    // cell of its own, open to the sky, roomy and walkable to from the start. The budget is the
    // fewest cells crossed to collect them all in the best order, plus slack.
    public bool PlanHunt(int start, int count, System.Random random, List<int> potions, out int budget)
    {
        potions.Clear();
        budget = 0;
        if (!IsReady || start < 0) return false;
        var fields = new List<int[]>();
        var fromStart = new int[SampleCount];
        Distances(start, fromStart);
        fields.Add(fromStart);
        var usedCells = new HashSet<Vector2Int> { CellOf(start) };
        var candidates = new List<int>();
        for (int i = 0; i < SampleCount; i++)
        {
            if (fromStart[i] != Unreachable && IsPotionSpot(i)) candidates.Add(i);
        }
        for (int k = 0; k < count; k++)
        {
            int pick = -1;
            for (int separation = MinSeparation; separation >= 1 && pick < 0; separation--)
            {
                pick = PickSpread(candidates, fields, usedCells, separation, random);
            }
            if (pick < 0)
            {
                potions.Clear();
                return false;
            }
            potions.Add(pick);
            usedCells.Add(CellOf(pick));
            var field = new int[SampleCount];
            Distances(pick, field);
            fields.Add(field);
        }
        int best = ShortestRoute(fields, potions);
        budget = best + Mathf.Max(MinSlack, Mathf.CeilToInt(best * BudgetSlack));
        return true;
    }

    // The fewest cells crossed from the start (fields[0]) through every potion, in the best order.
    public static int ShortestRoute(List<int[]> fields, List<int> potions)
    {
        int n = potions.Count;
        var used = new bool[n];
        int best = int.MaxValue;
        void Visit(int depth, int at, int length)
        {
            if (length >= best) return;
            if (depth == n)
            {
                best = length;
                return;
            }
            for (int k = 0; k < n; k++)
            {
                if (used[k]) continue;
                used[k] = true;
                int leg = fields[at][potions[k]];
                if (leg != Unreachable) Visit(depth + 1, k + 1, length + leg);
                used[k] = false;
            }
        }
        Visit(0, 0, 0);
        return best;
    }

    // A potion needs open sky, room around it and walkable ground all round, so it never sits
    // inside, under or against anything.
    public bool IsPotionSpot(int sample)
    {
        if (!Roomy[sample] || !IsLand(sample)) return false;
        int sx = sample % SamplesX, sz = sample / SamplesX;
        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int x = sx + dx, z = sz + dz;
                if (x < 0 || z < 0 || x >= SamplesX || z >= SamplesZ || !Walkable[z * SamplesX + x]) return false;
            }
        }
        return true;
    }

    private int PickSpread(List<int> candidates, List<int[]> fields, HashSet<Vector2Int> usedCells, int separation, System.Random random)
    {
        var scores = new int[candidates.Count];
        int farthest = -1;
        for (int c = 0; c < candidates.Count; c++)
        {
            int sample = candidates[c];
            scores[c] = -1;
            if (usedCells.Contains(CellOf(sample))) continue;
            int nearest = int.MaxValue;
            foreach (int[] field in fields) nearest = Mathf.Min(nearest, field[sample]);
            if (nearest == Unreachable || nearest < separation) continue;
            scores[c] = nearest;
            farthest = Mathf.Max(farthest, nearest);
        }
        if (farthest < 0) return -1;
        int floor = Mathf.Max(separation, Mathf.FloorToInt(farthest * SpreadShare));
        var pool = new List<int>();
        for (int c = 0; c < candidates.Count; c++)
        {
            if (scores[c] >= floor) pool.Add(candidates[c]);
        }
        return pool.Count == 0 ? -1 : pool[random.Next(pool.Count)];
    }

    // ---- Survey -----------------------------------------------------------------------

    private void SurveySpot(int i)
    {
        Kinds[i] = Kind.Water;
        Heights[i] = float.NaN;
        float x = Origin.x + (i % SamplesX + 0.5f) * SampleSize;
        float z = Origin.y + (i / SamplesX + 0.5f) * SampleSize;
        var sky = new Vector3(x, skyY, z);
        float depth = skyY - seaLevel + 50f;
        bool overGround = Ground.Raycast(new Ray(sky, Vector3.down), out RaycastHit groundHit, depth);
        bool any = FirstHit(sky, Vector3.down, depth, out RaycastHit first);

        if (!overGround)
        {
            // A pier or boat deck just above the sea; everything else off the island is sea.
            if (any && OwnerOf(first.collider) == Kind.Paved && first.normal.y > 0.6f &&
                first.point.y > seaLevel - 0.5f && first.point.y < seaLevel + 4f)
            {
                Kinds[i] = Kind.Dock;
                Heights[i] = first.point.y;
                Walkable[i] = Fits(x, first.point.y, z);
            }
            return;
        }

        float groundY = groundHit.point.y;
        Kind kind = any ? Classify(first, groundY) : Kind.Ground;
        Kinds[i] = kind;
        bool standsOnTop = kind == Kind.Ground || kind == Kind.Paved;
        float stand = standsOnTop ? (any ? first.point.y : groundY) : StandingHeight(x, z, groundY);
        Heights[i] = stand;
        Open[i] = standsOnTop;
        Walkable[i] = Fits(x, stand, z);
        Roomy[i] = Open[i] && Walkable[i] && Empty(new Vector3(x, stand + 1.3f, z), new Vector3(1.1f, 0.85f, 1.1f));
    }

    private void SurveyEdges(int i)
    {
        if (!Walkable[i]) return;
        int sx = i % SamplesX, sz = i / SamplesX;
        if (sx + 1 < SamplesX && Passable(i, i + 1)) edges[i] |= 1;
        if (sz + 1 < SamplesZ && Passable(i, i + SamplesX)) edges[i] |= 2;
    }

    private bool Passable(int a, int b)
    {
        if (!Walkable[b] || Mathf.Abs(Heights[a] - Heights[b]) > MaxClimb) return false;
        Vector3 from = SamplePosition(a);
        Vector3 to = SamplePosition(b);
        foreach (float height in PassHeights)
        {
            Vector3 start = from + Vector3.up * height;
            Vector3 delta = to + Vector3.up * height - start;
            int count = Physics.RaycastNonAlloc(start, delta.normalized, hits, delta.magnitude,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int k = 0; k < count; k++)
            {
                if (!ignored.Contains(hits[k].collider)) return false;
            }
        }
        return true;
    }

    // What a ray from the sky first meets says what the spot is: trees, buildings and the sea
    // by their place in the scene, low wide surfaces are paths and slabs, the rest is in the way.
    private Kind Classify(RaycastHit hit, float groundY)
    {
        if (hit.collider == Ground) return Kind.Ground;
        Kind owner = OwnerOf(hit.collider);
        if (owner != Kind.Paved) return owner;
        Vector3 size = hit.collider.bounds.size;
        bool wide = Mathf.Min(size.x, size.z) >= 2f;
        return hit.point.y - groundY <= MaxPavedHeight && hit.normal.y > 0.6f && wide ? Kind.Paved : Kind.Structure;
    }

    // Foliage, Building, Water (the sea) or Structure from the collider's name and parents;
    // Paved when nothing names it, for Classify to judge by shape.
    private Kind OwnerOf(Collider collider)
    {
        if (owners.TryGetValue(collider, out Kind kind)) return kind;
        kind = Kind.Paved;
        if (collider == ocean) kind = Kind.Water;
        for (Transform t = collider.transform; t != null && kind == Kind.Paved; t = t.parent)
        {
            string name = t.name.ToLowerInvariant();
            if (name.Contains("ocean")) kind = Kind.Water;
            else if (name.Contains("building")) kind = Kind.Building;
            // Boulders and the rocky shore (IslandScenery) are in the way, never a deck or a path.
            else if (name == "islanddemo:pcylinder3" || name.StartsWith("lamp") || name.Contains("rock")) kind = Kind.Structure;
            else
            {
                foreach (string foliage in FoliageNames)
                {
                    if (name.Contains(foliage))
                    {
                        kind = Kind.Foliage;
                        break;
                    }
                }
            }
        }
        owners[collider] = kind;
        return kind;
    }

    // Under a roof or a canopy: the surface just above ground level that the player would stand
    // on. Cast from low down, so low-hanging leaves above aren't taken for the floor.
    private float StandingHeight(float x, float z, float groundY)
    {
        var from = new Vector3(x, groundY + 1.4f, z);
        return FirstHit(from, Vector3.down, 3f, out RaycastHit hit) ? hit.point.y : groundY;
    }

    // The player's capsule fits standing at height y, its feet a step above the surface.
    private bool Fits(float x, float y, float z)
    {
        var bottom = new Vector3(x, y + StepClearance + Radius, z);
        var top = new Vector3(x, y + Height - Radius, z);
        int count = Physics.OverlapCapsuleNonAlloc(bottom, top, Radius, overlaps, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int k = 0; k < count; k++)
        {
            if (!ignored.Contains(overlaps[k])) return false;
        }
        return true;
    }

    private bool Empty(Vector3 centre, Vector3 halfExtents)
    {
        int count = Physics.OverlapBoxNonAlloc(centre, halfExtents, overlaps, Quaternion.identity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int k = 0; k < count; k++)
        {
            if (!ignored.Contains(overlaps[k])) return false;
        }
        return true;
    }

    private bool FirstHit(Vector3 origin, Vector3 direction, float distance, out RaycastHit best)
    {
        int count = Physics.RaycastNonAlloc(origin, direction, hits, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        best = default;
        float nearest = float.MaxValue;
        for (int k = 0; k < count; k++)
        {
            if (ignored.Contains(hits[k].collider) || hits[k].distance >= nearest) continue;
            nearest = hits[k].distance;
            best = hits[k];
        }
        return nearest < float.MaxValue;
    }

    // The height to run a grid wall along the border between two neighbouring spots, where the
    // border crosses land the player can walk; false over the sea and through buildings.
    public bool BorderHeight(int a, int b, out float height)
    {
        height = 0f;
        bool hasA = a >= 0 && IsLand(a) && !float.IsNaN(Heights[a]);
        bool hasB = b >= 0 && IsLand(b) && !float.IsNaN(Heights[b]);
        if (!hasA && !hasB) return false;
        bool walkable = (hasA && Walkable[a]) || (hasB && Walkable[b]);
        if (!walkable) return false;
        height = hasA && hasB ? Mathf.Max(Heights[a], Heights[b]) : hasA ? Heights[a] : Heights[b];
        return true;
    }

    // The spot index at grid coordinates, or -1 outside.
    public int Sample(int sx, int sz) => sx < 0 || sz < 0 || sx >= SamplesX || sz >= SamplesZ ? -1 : sz * SamplesX + sx;
}
