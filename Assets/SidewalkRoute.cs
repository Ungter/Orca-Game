using System.Collections.Generic;
using UnityEngine;

// Orca's way about the island: the potion hunt's walkable survey (ClueMap), where a step onto a
// sidewalk costs 1 and a step anywhere else OffSidewalkCost, so the cheapest walk to a place runs
// along the sidewalks and only leaves them for the last stretch. Which spots are sidewalk is found
// by looking straight down at each one for the named sidewalk objects (and anything under them,
// such as the gravel spills of sideWalkAlch), a slice at a time.
public sealed class SidewalkRoute
{
    public static readonly string[] SidewalkNames = { "sideWalkAlch", "IslandDemo:sideWalk", "Front Path", "sidewalkToACC" };
    public const int OffSidewalkCost = 6;
    // A surface this close to a spot's surveyed height is what the spot stands on.
    private const float SurfaceTolerance = 0.8f;

    private static SidewalkRoute shared;

    private readonly ClueMap map;
    private readonly bool[] sidewalk;
    private readonly Dictionary<Collider, bool> owners = new Dictionary<Collider, bool>();
    private int surveyed;
    // Which connected stretch of walkable ground each spot belongs to (-1 for none), and the
    // biggest stretch, the island itself; pockets hemmed in by walls and props are left out.
    private int[] region;
    private int island = -1;

    public bool IsReady => surveyed >= map.SampleCount;
    public int SidewalkCount { get; private set; }

    public SidewalkRoute(ClueMap map)
    {
        this.map = map;
        sidewalk = new bool[map.SampleCount];
    }

    // One route per survey (a reloaded scene surveys again).
    public static SidewalkRoute For(ClueMap map)
    {
        if (map == null) return null;
        if (shared == null || shared.map != map) shared = new SidewalkRoute(map);
        return shared;
    }

    public bool IsSidewalk(int sample) => sample >= 0 && sample < sidewalk.Length && sidewalk[sample];

    public int Cost(int sample) => IsSidewalk(sample) ? 1 : OffSidewalkCost;

    // Looks at up to budget more spots; true once every spot has been looked at.
    public bool Advance(int budget)
    {
        if (!map.IsReady) return false;
        for (int done = 0; done < budget && surveyed < map.SampleCount; done++, surveyed++)
        {
            if (!map.Walkable[surveyed]) continue;
            float height = map.Heights[surveyed];
            if (float.IsNaN(height)) continue;
            Vector3 spot = map.SamplePosition(surveyed);
            foreach (RaycastHit hit in Physics.RaycastAll(spot + Vector3.up * 3f, Vector3.down, 6f,
                         Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (Mathf.Abs(hit.point.y - height) > SurfaceTolerance || !IsSidewalk(hit.collider)) continue;
                sidewalk[surveyed] = true;
                SidewalkCount++;
                break;
            }
        }
        return IsReady;
    }

    private bool IsSidewalk(Collider collider)
    {
        if (owners.TryGetValue(collider, out bool known)) return known;
        bool found = false;
        for (Transform t = collider.transform; t != null && !found; t = t.parent)
            found = System.Array.IndexOf(SidewalkNames, t.name) >= 0;
        owners[collider] = found;
        return found;
    }

    // The closest spot within reach of a point that the rest of the island can be walked to from,
    // or -1: a door or a statue's own spot can be a walled-in pocket.
    public int NearestOnIsland(Vector3 at, int reach)
    {
        if (!map.IsReady) return -1;
        if (region == null || region.Length != map.SampleCount) FindRegions();
        int sx = Mathf.FloorToInt((at.x - map.Origin.x) / map.SampleSize);
        int sz = Mathf.FloorToInt((at.z - map.Origin.y) / map.SampleSize);
        int best = -1;
        float bestDistance = float.MaxValue;
        for (int dz = -reach; dz <= reach; dz++)
        for (int dx = -reach; dx <= reach; dx++)
        {
            int x = sx + dx, z = sz + dz;
            if (x < 0 || z < 0 || x >= map.SamplesX || z >= map.SamplesZ) continue;
            int sample = z * map.SamplesX + x;
            if (region[sample] != island || !map.IsLand(sample)) continue;
            float distance = dx * dx + dz * dz;
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = sample;
        }
        return best;
    }

    private void FindRegions()
    {
        region = new int[map.SampleCount];
        for (int i = 0; i < region.Length; i++) region[i] = -1;
        var queue = new int[map.SampleCount];
        int regions = 0, biggest = 0;
        for (int start = 0; start < region.Length; start++)
        {
            if (region[start] >= 0 || !map.Walkable[start]) continue;
            int head = 0, tail = 0;
            region[start] = regions;
            queue[tail++] = start;
            while (head < tail)
            {
                int s = queue[head++];
                for (int n = 0; n < 4; n++)
                {
                    int t = map.Neighbour(s, n);
                    if (t < 0 || region[t] >= 0) continue;
                    region[t] = regions;
                    queue[tail++] = t;
                }
            }
            if (tail > biggest)
            {
                biggest = tail;
                island = regions;
            }
            regions++;
        }
    }

    // The cheapest walk from every spot to source (ClueMap.Unreachable where there is none), each
    // step costing what the spot stepped onto costs. Dijkstra over the survey's four-way steps.
    public void Distances(int source, int[] into)
    {
        for (int i = 0; i < into.Length; i++) into[i] = ClueMap.Unreachable;
        if (source < 0 || source >= into.Length) return;
        var heap = new List<(int cost, int sample)>();
        into[source] = 0;
        Push(heap, (0, source));
        while (heap.Count > 0)
        {
            (int cost, int s) = Pop(heap);
            if (cost > into[s]) continue;
            for (int n = 0; n < 4; n++)
            {
                int t = map.Neighbour(s, n);
                if (t < 0) continue;
                // Walking from t to s, the step lands on s.
                int next = cost + Cost(s);
                if (next >= into[t]) continue;
                into[t] = next;
                Push(heap, (next, t));
            }
        }
    }

    private static void Push(List<(int cost, int sample)> heap, (int cost, int sample) item)
    {
        heap.Add(item);
        int i = heap.Count - 1;
        while (i > 0)
        {
            int parent = (i - 1) / 2;
            if (heap[parent].cost <= heap[i].cost) break;
            (heap[parent], heap[i]) = (heap[i], heap[parent]);
            i = parent;
        }
    }

    private static (int cost, int sample) Pop(List<(int cost, int sample)> heap)
    {
        (int cost, int sample) top = heap[0];
        int last = heap.Count - 1;
        heap[0] = heap[last];
        heap.RemoveAt(last);
        int i = 0;
        while (true)
        {
            int left = i * 2 + 1, right = left + 1, smallest = i;
            if (left < heap.Count && heap[left].cost < heap[smallest].cost) smallest = left;
            if (right < heap.Count && heap[right].cost < heap[smallest].cost) smallest = right;
            if (smallest == i) break;
            (heap[smallest], heap[i]) = (heap[i], heap[smallest]);
            i = smallest;
        }
        return top;
    }
}
