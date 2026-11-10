using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Turns the model's dock (two plain slabs joined in an L) into a timber one, keeping its shape
// and size: a deck of separate planks on long stringers and cross-beams, round pilings driven
// alongside it into the sea, every other one standing tall as a mooring post with a rope
// between them, cross-braces under the deck and a ladder down to the water. All one matte
// brown material for now (DockMat).
//
// What the game measures is left exactly as it was. The dock keeps the model's slab as its
// collider, so the player walks the same flat deck, and the new deck mesh is given the slab's
// own bounds, so OpeningVoyage finds the same walkway and landing. Everything that reaches
// outside the slab (pilings, posts, rope, ladder) is a separate object, Dock Timbers, which is
// not under the dock, has no colliders and stands just off the deck's edge, never on it.
//
// Tools > Orca > Rebuild Dock builds it again from the model's slab, the same every time.
public static class IslandDock
{
    public const string DockName = "IslandDemo:Dock";
    public const string TimbersName = "Dock Timbers";
    private const string GroundName = "IslandDemo:pCube1";
    private const string Folder = "Assets/Generated/IslandDock";
    private const string DeckPath = Folder + "/Dock Deck.asset";
    private const string TimbersPath = Folder + "/Dock Timbers.asset";
    private const string MaterialPath = "Assets/Materials/DockMat.mat";
    private static readonly Color Brown = new Color(0.33f, 0.22f, 0.14f);
    private const int Seed = 911;

    // Deck. Planks run across an arm; each is a little short at either end and rolled a touch,
    // so the edge is ragged and the boards catch the light differently.
    private const float PlankPitch = 0.42f;
    private const float PlankGap = 0.035f;
    private const float PlankThickness = 0.11f;
    private const float PlankRagged = 0.09f;
    private const float PlankRoll = 1.2f;
    // The boards stand this much above the slab the player walks on, so the lawn the dock
    // lies level with never shows through them; and run this far into the other arm.
    private const float PlankProud = 0.02f;
    private const float PlankJoin = 0.03f;
    private const float StringerWidth = 0.2f;
    private const float StringerHeight = 0.28f;
    private const float StringerInset = 0.5f;

    // Pilings, at each cross-beam, this far outside the deck's edge.
    private const float StationSpacing = 4.2f;
    private const float StationInset = 0.9f;
    private const float PileOut = 0.36f;
    private const float PileDepth = 2.6f;
    private const float PostHeightMin = 1f, PostHeightMax = 1.3f;
    private const float RopeHeight = 0.82f;
    private const float RopeRadius = 0.045f;

    private readonly struct Arm
    {
        public readonly Vector2 Centre, Along, Across;
        public readonly float Length, Width;

        public Arm(Vector2 centre, Vector2 along, Vector2 across, float length, float width)
        {
            Centre = centre;
            Along = along;
            Across = across;
            Length = length;
            Width = width;
        }
    }

    [MenuItem("Tools/Orca/Rebuild Dock")]
    public static void Rebuild()
    {
        Debug.Log(Build());
    }

    public static string Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "[IslandDock] Not in play mode: nothing would be kept.";
        GameObject dock = GameObject.Find(DockName);
        if (dock == null || !dock.TryGetComponent(out MeshFilter filter) || !dock.TryGetComponent(out MeshRenderer renderer))
        {
            return "[IslandDock] No " + DockName + " in the open scene.";
        }
        Mesh slab = ModelMesh(dock, filter);
        if (slab == null) return "[IslandDock] The dock has no mesh to take its shape from.";
        if (!TryMeasure(slab, dock.transform, out List<Arm> arms, out List<Vector2> outline, out float top, out float bottom))
        {
            return "[IslandDock] Couldn't read the dock's outline from " + slab.name + ".";
        }

        float seaLevel = bottom - 0.9f;
        foreach (Renderer candidate in dock.transform.root.GetComponentsInChildren<Renderer>(true))
        {
            if (candidate.name.ToLowerInvariant() == "ocean") seaLevel = candidate.bounds.max.y;
        }
        GameObject groundObject = GameObject.Find(GroundName);
        Collider ground = groundObject != null ? groundObject.GetComponent<Collider>() : null;

        var random = new System.Random(Seed);
        var deck = new Builder();
        var timbers = new Builder();
        int planks = 0, piles = 0, posts = 0, ropes = 0;
        // The ladder goes on whichever arm stands most in the sea (the pier).
        int ladderPiles = 0;
        Vector3 ladderAt = default, ladderAlong = default, ladderOut = default;
        float stringerTop = top + PlankProud - PlankThickness - 0.02f;
        float beamTop = stringerTop - StringerHeight;
        float beamHeight = Mathf.Max(0.12f, beamTop - bottom);

        for (int a = 0; a < arms.Count; a++)
        {
            Arm arm = arms[a];
            Vector3 along = new Vector3(arm.Along.x, 0f, arm.Along.y), across = new Vector3(arm.Across.x, 0f, arm.Across.y);

            // Planks.
            int count = Mathf.Max(1, Mathf.RoundToInt(arm.Length / PlankPitch));
            float pitch = arm.Length / count;
            for (int i = 0; i < count; i++)
            {
                float shortA = Range(random, 0f, PlankRagged), shortB = Range(random, 0f, PlankRagged);
                // An end that meets the other arm's deck runs right up to it, with no ragged gap.
                Vector2 row = arm.Centre + arm.Along * (-arm.Length * 0.5f + (i + 0.5f) * pitch);
                if (Inside(outline, row - arm.Across * (arm.Width * 0.5f + 0.1f))) shortA = -PlankJoin;
                if (Inside(outline, row + arm.Across * (arm.Width * 0.5f + 0.1f))) shortB = -PlankJoin;
                Vector2 middle = row + arm.Across * ((shortA - shortB) * 0.5f);
                Quaternion roll = Quaternion.AngleAxis(Range(random, -PlankRoll, PlankRoll), across);
                var centre = new Vector3(middle.x, top + PlankProud - PlankThickness * 0.5f - Range(random, 0f, 0.012f), middle.y);
                Box(deck, centre, across, roll * Vector3.up, roll * along,
                    new Vector3((arm.Width - shortA - shortB) * 0.5f, PlankThickness * 0.5f, (pitch - PlankGap) * 0.5f));
                planks++;
            }

            // Stringers under them, the length of the arm.
            foreach (float offset in new[] { -(arm.Width * 0.5f - StringerInset), 0f, arm.Width * 0.5f - StringerInset })
            {
                Vector2 middle = arm.Centre + arm.Across * offset;
                Box(deck, new Vector3(middle.x, stringerTop - StringerHeight * 0.5f, middle.y), along, Vector3.up, across,
                    new Vector3(arm.Length * 0.5f - 0.05f, StringerHeight * 0.5f, StringerWidth * 0.5f));
            }

            // Cross-beams, each carried by a piling either side where there is sea to stand it in.
            int stations = Mathf.Max(2, Mathf.RoundToInt(arm.Length / StationSpacing) + 1);
            int inSea = 0;
            var standing = new bool[stations, 2];
            var heads = new Vector3[stations, 2];
            var tall = new bool[stations, 2];
            for (int k = 0; k < stations; k++)
            {
                float s = -arm.Length * 0.5f + StationInset + k * (arm.Length - 2f * StationInset) / (stations - 1);
                Vector2 station = arm.Centre + arm.Along * s;
                Box(deck, new Vector3(station.x, beamTop - beamHeight * 0.5f, station.y), across, Vector3.up, along,
                    new Vector3(arm.Width * 0.5f - 0.02f, beamHeight * 0.5f, 0.15f));

                for (int side = 0; side < 2; side++)
                {
                    float sign = side == 0 ? -1f : 1f;
                    Vector2 edge = station + arm.Across * (sign * arm.Width * 0.5f);
                    Vector2 foot = edge + arm.Across * (sign * PileOut);
                    // Not where the other arm's deck is, and not on the shore.
                    if (Inside(outline, foot) || OnLand(ground, foot, seaLevel)) continue;

                    standing[k, side] = true;
                    tall[k, side] = k % 2 == 0;
                    float radius = Range(random, 0.24f, 0.3f);
                    float height = tall[k, side] ? Range(random, PostHeightMin, PostHeightMax) : 0.14f;
                    Vector2 lean = RandomInCircle(random) * 0.07f;
                    var bed = new Vector3(foot.x - lean.x, seaLevel - PileDepth, foot.y - lean.y);
                    var head = new Vector3(foot.x + lean.x * 0.4f, top + height, foot.y + lean.y * 0.4f);
                    Pile(timbers, bed, head, radius);
                    heads[k, side] = head;
                    piles++;
                    inSea++;

                    // The beam carries on out to the piling.
                    Vector2 stub = edge + arm.Across * (sign * (PileOut + 0.2f) * 0.5f);
                    Box(timbers, new Vector3(stub.x, beamTop - beamHeight * 0.5f, stub.y), across, Vector3.up, along,
                        new Vector3((PileOut + 0.2f) * 0.5f, beamHeight * 0.5f, 0.15f));

                    if (!tall[k, side]) continue;
                    posts++;
                    // A few turns of rope round the post, where the rail ties on.
                    Vector3 axis = (head - bed).normalized;
                    Vector3 tie = head - axis * (height - RopeHeight);
                    Tube(timbers, new[] { tie - axis * 0.11f, tie - axis * 0.07f, tie + axis * 0.07f, tie + axis * 0.11f },
                        new[] { radius, radius + 0.05f, radius + 0.05f, radius }, 8, false);
                }

                // Braces crossing under the deck between a pair of pilings.
                if (standing[k, 0] && standing[k, 1])
                {
                    Vector2 left = station - arm.Across * (arm.Width * 0.5f + PileOut), right = station + arm.Across * (arm.Width * 0.5f + PileOut);
                    float high = bottom - 0.06f, low = seaLevel + 0.14f;
                    Brace(timbers, new Vector3(left.x, high, left.y) + along * 0.2f, new Vector3(right.x, low, right.y) + along * 0.2f, along);
                    Brace(timbers, new Vector3(left.x, low, left.y) - along * 0.2f, new Vector3(right.x, high, right.y) - along * 0.2f, along);
                }
            }

            // A sagging rope from mooring post to mooring post along each side, where no piling
            // is missing between them. An end of the arm that looks out to sea is left open for
            // a boat to come alongside; one that meets the shore or the other arm is not.
            bool openStart = OpenToSea(arm, -1f, outline, ground, seaLevel), openEnd = OpenToSea(arm, 1f, outline, ground, seaLevel);
            for (int side = 0; side < 2; side++)
            {
                for (int k = 0; k + 2 < stations; k += 2)
                {
                    if ((k == 0 && openStart) || (k + 2 >= stations - 1 && openEnd)) continue;
                    if (!standing[k, side] || !standing[k + 1, side] || !standing[k + 2, side]) continue;
                    Rope(timbers, RopeTie(heads[k, side], top), RopeTie(heads[k + 2, side], top));
                    ropes++;
                }
            }

            // A ladder would go between this arm's second and third pilings, on a side in the sea.
            if (inSea > ladderPiles && stations >= 4)
            {
                for (int side = 0; side < 2; side++)
                {
                    if (!standing[1, side] || !standing[2, side]) continue;
                    float sign = side == 0 ? -1f : 1f;
                    float s = -arm.Length * 0.5f + StationInset + 1.5f * (arm.Length - 2f * StationInset) / (stations - 1);
                    Vector2 at = arm.Centre + arm.Along * s + arm.Across * (sign * (arm.Width * 0.5f + 0.1f));
                    ladderAt = new Vector3(at.x, 0f, at.y);
                    ladderAlong = along;
                    ladderOut = across * sign;
                    ladderPiles = inSea;
                    break;
                }
            }
        }
        bool ladder = ladderPiles > 0;
        if (ladder) Ladder(timbers, ladderAt, ladderAlong, ladderOut, top, seaLevel);

        // Save, and put them in the scene.
        Directory.CreateDirectory(Folder);
        AssetDatabase.Refresh();
        Material material = DockMaterial();

        Undo.SetCurrentGroupName("Rebuild Dock");
        int undo = Undo.GetCurrentGroup();
        Mesh deckMesh = deck.ToMesh("Dock Deck", dock.transform);
        // The slab's own bounds: the deck lies inside them, and the opening measures the dock by them.
        deckMesh.bounds = slab.bounds;
        deckMesh = Store(deckMesh, DeckPath);
        Undo.RecordObject(filter, "Rebuild Dock");
        Undo.RecordObject(renderer, "Rebuild Dock");
        filter.sharedMesh = deckMesh;
        renderer.sharedMaterial = material;

        GameObject old = GameObject.Find(TimbersName);
        if (old != null) Undo.DestroyObjectImmediate(old);
        var holder = new GameObject(TimbersName) { layer = dock.layer };
        Undo.RegisterCreatedObjectUndo(holder, "Rebuild Dock");
        holder.AddComponent<MeshFilter>().sharedMesh = Store(timbers.ToMesh(TimbersName, holder.transform), TimbersPath);
        var drawn = holder.AddComponent<MeshRenderer>();
        drawn.sharedMaterial = material;
        drawn.shadowCastingMode = renderer.shadowCastingMode;
        drawn.renderingLayerMask = renderer.renderingLayerMask;
        Undo.CollapseUndoOperations(undo);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(dock.scene);

        return "[IslandDock] " + arms.Count + " arms, " + planks + " planks, " + piles + " pilings (" + posts + " mooring posts), " + ropes +
            " ropes" + (ladder ? ", a ladder" : "") + ". Deck " + deckMesh.vertexCount + " vertices, timbers " + timbers.Positions.Count + ".";
    }

    // The slab the model gave the dock, whatever mesh the scene shows now.
    private static Mesh ModelMesh(GameObject dock, MeshFilter filter)
    {
        MeshFilter source = PrefabUtility.GetCorrespondingObjectFromSource(filter);
        if (source != null && source.sharedMesh != null) return source.sharedMesh;
        if (dock.TryGetComponent(out MeshCollider collider) && collider.sharedMesh != null) return collider.sharedMesh;
        return filter.sharedMesh;
    }

    private static bool OnLand(Collider ground, Vector2 at, float seaLevel)
    {
        return ground != null && ground.Raycast(new Ray(new Vector3(at.x, seaLevel + 200f, at.y), Vector3.down), out RaycastHit hit, 400f) &&
            hit.point.y > seaLevel + 0.25f;
    }

    // Whether the water carries on past an end of an arm (sign -1 its start, 1 its end).
    private static bool OpenToSea(Arm arm, float sign, List<Vector2> outline, Collider ground, float seaLevel)
    {
        Vector2 beyond = arm.Centre + arm.Along * (sign * (arm.Length * 0.5f + 0.5f));
        return !Inside(outline, beyond) && !OnLand(ground, beyond, seaLevel);
    }

    private static Vector3 RopeTie(Vector3 head, float top)
    {
        return new Vector3(head.x, top + RopeHeight, head.z);
    }

    // ---- Reading the slab -------------------------------------------------------------

    // The deck's outline seen from above, and the rectangles (arms) it is made of: one for a
    // plain pier, two for an L, cut where the shorter edge at the inside corner runs on.
    private static bool TryMeasure(Mesh slab, Transform transform, out List<Arm> arms, out List<Vector2> outline, out float top, out float bottom)
    {
        arms = new List<Arm>();
        outline = new List<Vector2>();
        top = float.MinValue;
        bottom = float.MaxValue;
        Matrix4x4 toWorld = transform.localToWorldMatrix;
        Vector3[] local = slab.vertices;
        var points = new Vector3[local.Length];
        for (int i = 0; i < local.Length; i++)
        {
            points[i] = toWorld.MultiplyPoint3x4(local[i]);
            top = Mathf.Max(top, points[i].y);
            bottom = Mathf.Min(bottom, points[i].y);
        }

        // The edges only one upward face uses, chained into a loop.
        var open = new Dictionary<(Vector2Int, Vector2Int), (Vector2 from, Vector2 to)>();
        int[] triangles = slab.triangles;
        for (int t = 0; t < triangles.Length; t += 3)
        {
            Vector3 a = points[triangles[t]], b = points[triangles[t + 1]], c = points[triangles[t + 2]];
            if (Vector3.Cross(b - a, c - a).normalized.y < 0.7f) continue;
            Edge(a, b);
            Edge(b, c);
            Edge(c, a);
        }

        void Edge(Vector3 from, Vector3 to)
        {
            (Vector2Int, Vector2Int) key = (Key(from), Key(to));
            if (!open.Remove((key.Item2, key.Item1))) open[key] = (new Vector2(from.x, from.z), new Vector2(to.x, to.z));
        }

        var next = new Dictionary<Vector2Int, (Vector2 from, Vector2 to)>();
        foreach (KeyValuePair<(Vector2Int, Vector2Int), (Vector2 from, Vector2 to)> edge in open) next[edge.Key.Item1] = edge.Value;
        if (next.Count < 4) return false;
        Vector2Int start = default;
        foreach (Vector2Int key in next.Keys) { start = key; break; }
        var loop = new List<Vector2>();
        Vector2Int at = start;
        while (next.TryGetValue(at, out (Vector2 from, Vector2 to) edge) && loop.Count <= open.Count)
        {
            next.Remove(at);
            loop.Add(edge.from);
            at = new Vector2Int(Mathf.RoundToInt(edge.to.x * 50f), Mathf.RoundToInt(edge.to.y * 50f));
        }
        if (at != start || loop.Count < 4) return false;

        outline = Corners(loop);
        if (outline.Count < 4) return false;
        float turning = 0f;
        for (int i = 0; i < outline.Count; i++) turning += Turn(outline, i);
        var inside = new List<int>();
        for (int i = 0; i < outline.Count; i++)
        {
            if (Turn(outline, i) * turning < 0f) inside.Add(i);
        }

        if (inside.Count == 1)
        {
            // Run the shorter of the two edges at the inside corner on until it meets the far side.
            int r = inside[0], n = outline.Count;
            Vector2 corner = outline[r], before = outline[(r + n - 1) % n], after = outline[(r + 1) % n];
            bool fromBefore = (corner - before).sqrMagnitude <= (after - corner).sqrMagnitude;
            Vector2 direction = (corner - (fromBefore ? before : after)).normalized;
            float nearest = float.MaxValue;
            int hitEdge = -1;
            for (int i = 0; i < n; i++)
            {
                if (i == r || (i + 1) % n == r) continue;
                if (RayMeetsSegment(corner, direction, outline[i], outline[(i + 1) % n], out float distance) && distance > 0.01f && distance < nearest)
                {
                    nearest = distance;
                    hitEdge = i;
                }
            }
            if (hitEdge >= 0)
            {
                Vector2 hit = corner + direction * nearest;
                var first = new List<Vector2> { corner };
                for (int i = (r + 1) % n; i != (hitEdge + 1) % n; i = (i + 1) % n) first.Add(outline[i]);
                first.Add(hit);
                var second = new List<Vector2> { hit };
                for (int i = (hitEdge + 1) % n; i != r; i = (i + 1) % n) second.Add(outline[i]);
                second.Add(corner);
                arms.Add(Fit(first));
                arms.Add(Fit(second));
                return true;
            }
        }
        arms.Add(Fit(outline));
        return true;
    }

    private static Vector2Int Key(Vector3 point)
    {
        return new Vector2Int(Mathf.RoundToInt(point.x * 50f), Mathf.RoundToInt(point.z * 50f));
    }

    // The loop without the points that sit along a straight edge.
    private static List<Vector2> Corners(List<Vector2> loop)
    {
        var corners = new List<Vector2>();
        for (int i = 0; i < loop.Count; i++)
        {
            Vector2 into = (loop[i] - loop[(i + loop.Count - 1) % loop.Count]).normalized, away = (loop[(i + 1) % loop.Count] - loop[i]).normalized;
            if (Mathf.Abs(into.x * away.y - into.y * away.x) > 0.05f) corners.Add(loop[i]);
        }
        return corners;
    }

    private static float Turn(List<Vector2> outline, int i)
    {
        int n = outline.Count;
        Vector2 into = outline[i] - outline[(i + n - 1) % n], away = outline[(i + 1) % n] - outline[i];
        return into.x * away.y - into.y * away.x;
    }

    private static bool RayMeetsSegment(Vector2 origin, Vector2 direction, Vector2 a, Vector2 b, out float distance)
    {
        distance = 0f;
        Vector2 side = b - a;
        float cross = direction.x * side.y - direction.y * side.x;
        if (Mathf.Abs(cross) < 0.0001f) return false;
        Vector2 offset = a - origin;
        distance = (offset.x * side.y - offset.y * side.x) / cross;
        float share = (offset.x * direction.y - offset.y * direction.x) / cross;
        return share >= -0.001f && share <= 1.001f;
    }

    // The rectangle a four-cornered piece of the outline makes, its length along its longest edge.
    private static Arm Fit(List<Vector2> piece)
    {
        List<Vector2> corners = Corners(piece);
        if (corners.Count < 3) corners = piece;
        Vector2 along = Vector2.right;
        float longest = 0f;
        for (int i = 0; i < corners.Count; i++)
        {
            Vector2 edge = corners[(i + 1) % corners.Count] - corners[i];
            if (edge.sqrMagnitude > longest)
            {
                longest = edge.sqrMagnitude;
                along = edge.normalized;
            }
        }
        var across = new Vector2(along.y, -along.x);
        float minAlong = float.MaxValue, maxAlong = float.MinValue, minAcross = float.MaxValue, maxAcross = float.MinValue;
        foreach (Vector2 corner in corners)
        {
            float u = Vector2.Dot(corner, along), v = Vector2.Dot(corner, across);
            minAlong = Mathf.Min(minAlong, u);
            maxAlong = Mathf.Max(maxAlong, u);
            minAcross = Mathf.Min(minAcross, v);
            maxAcross = Mathf.Max(maxAcross, v);
        }
        Vector2 centre = along * ((minAlong + maxAlong) * 0.5f) + across * ((minAcross + maxAcross) * 0.5f);
        return new Arm(centre, along, across, maxAlong - minAlong, maxAcross - minAcross);
    }

    private static bool Inside(List<Vector2> outline, Vector2 point)
    {
        bool inside = false;
        for (int i = 0, j = outline.Count - 1; i < outline.Count; j = i++)
        {
            Vector2 a = outline[i], b = outline[j];
            if ((a.y > point.y) != (b.y > point.y) && point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
        }
        return inside;
    }

    // ---- Timbers ----------------------------------------------------------------------

    // A box with flat faces: half sizes along right, up and forward.
    private static void Box(Builder b, Vector3 centre, Vector3 right, Vector3 up, Vector3 forward, Vector3 half)
    {
        Vector3 x = right.normalized * half.x, y = up.normalized * half.y, z = forward.normalized * half.z;
        b.Quad(centre + y - x - z, centre + y - x + z, centre + y + x + z, centre + y + x - z, up, half.x * 2f, half.z * 2f);
        b.Quad(centre - y - x - z, centre - y + x - z, centre - y + x + z, centre - y - x + z, -up, half.x * 2f, half.z * 2f);
        b.Quad(centre + x - y - z, centre + x + y - z, centre + x + y + z, centre + x - y + z, right, half.z * 2f, half.y * 2f);
        b.Quad(centre - x - y - z, centre - x - y + z, centre - x + y + z, centre - x + y - z, -right, half.z * 2f, half.y * 2f);
        b.Quad(centre + z - x - y, centre + z + x - y, centre + z + x + y, centre + z - x + y, forward, half.x * 2f, half.y * 2f);
        b.Quad(centre - z - x - y, centre - z - x + y, centre - z + x + y, centre - z + x - y, -forward, half.x * 2f, half.y * 2f);
    }

    // A round piling from the sea bed to its head, the head chamfered.
    private static void Pile(Builder b, Vector3 bed, Vector3 head, float radius)
    {
        Vector3 axis = (head - bed).normalized;
        Tube(b, new[] { bed, head - axis * 0.1f, head }, new[] { radius, radius, radius * 0.72f }, 8, true);
    }

    // A thin board on edge between two points.
    private static void Brace(Builder b, Vector3 from, Vector3 to, Vector3 thickness)
    {
        Vector3 length = to - from;
        Vector3 forward = length.normalized;
        Vector3 up = Vector3.Cross(thickness, forward).normalized;
        Box(b, (from + to) * 0.5f, forward, up, Vector3.Cross(forward, up), new Vector3(length.magnitude * 0.5f, 0.1f, 0.035f));
    }

    // A rope hanging between two ties.
    private static void Rope(Builder b, Vector3 from, Vector3 to)
    {
        const int segments = 10;
        float sag = 0.12f + 0.035f * Vector3.Distance(from, to);
        var points = new Vector3[segments + 1];
        var radii = new float[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            float share = i / (float)segments;
            points[i] = Vector3.Lerp(from, to, share) + Vector3.down * (sag * 4f * share * (1f - share));
            radii[i] = RopeRadius;
        }
        Tube(b, points, radii, 6, false);
    }

    // Two rails and their rungs, from above the deck down into the water.
    private static void Ladder(Builder b, Vector3 at, Vector3 along, Vector3 outward, float top, float seaLevel)
    {
        const float spread = 0.28f;
        float high = top + 0.95f, low = seaLevel - 0.5f;
        foreach (float side in new[] { -1f, 1f })
        {
            Vector3 rail = at + along * (side * spread);
            Box(b, new Vector3(rail.x, (high + low) * 0.5f, rail.z), along, Vector3.up, outward, new Vector3(0.04f, (high - low) * 0.5f, 0.05f));
        }
        for (float y = seaLevel - 0.2f; y < top - 0.05f; y += 0.32f)
        {
            Box(b, new Vector3(at.x, y, at.z), along, Vector3.up, outward, new Vector3(spread, 0.03f, 0.035f));
        }
    }

    // Rings round a line of points, joined into a tube; cap closes the far end.
    private static void Tube(Builder b, Vector3[] centres, float[] radii, int sides, bool cap)
    {
        int first = b.Positions.Count;
        int ring = sides + 1;
        float travelled = 0f;
        Vector3 sideways = Vector3.zero, upward = Vector3.zero;
        for (int k = 0; k < centres.Length; k++)
        {
            Vector3 tangent = (centres[Mathf.Min(k + 1, centres.Length - 1)] - centres[Mathf.Max(k - 1, 0)]).normalized;
            // Carry the frame along the tube so it never twists.
            Vector3 reference = k == 0 ? (Mathf.Abs(tangent.y) > 0.9f ? Vector3.right : Vector3.up) : upward;
            sideways = Vector3.Cross(reference, tangent).normalized;
            upward = Vector3.Cross(tangent, sideways).normalized;
            if (k > 0) travelled += Vector3.Distance(centres[k], centres[k - 1]);
            for (int j = 0; j <= sides; j++)
            {
                float around = j * Mathf.PI * 2f / sides;
                Vector3 outward = sideways * Mathf.Cos(around) + upward * Mathf.Sin(around);
                b.Add(centres[k] + outward * radii[k], outward, new Vector2(j / (float)sides * Mathf.PI * 2f * radii[k], travelled));
            }
        }
        for (int k = 0; k + 1 < centres.Length; k++)
        {
            for (int j = 0; j < sides; j++)
            {
                int lower = first + k * ring + j, upper = lower + ring;
                b.Facing(lower, upper, lower + 1, b.Normals[lower]);
                b.Facing(lower + 1, upper, upper + 1, b.Normals[lower]);
            }
        }
        if (!cap) return;
        int last = centres.Length - 1;
        Vector3 end = (centres[last] - centres[last - 1]).normalized;
        int hub = b.Add(centres[last], end, Vector2.zero);
        int rim = b.Positions.Count;
        for (int j = 0; j <= sides; j++)
        {
            Vector3 point = b.Positions[first + last * ring + j];
            b.Add(point, end, new Vector2(point.x, point.z));
        }
        for (int j = 0; j < sides; j++) b.Facing(hub, rim + j, rim + j + 1, end);
    }

    // ---- Mesh, material ---------------------------------------------------------------

    private sealed class Builder
    {
        public readonly List<Vector3> Positions = new List<Vector3>();
        public readonly List<Vector3> Normals = new List<Vector3>();
        public readonly List<Vector2> Uvs = new List<Vector2>();
        public readonly List<int> Triangles = new List<int>();

        public int Add(Vector3 position, Vector3 normal, Vector2 uv)
        {
            Positions.Add(position);
            Normals.Add(normal.normalized);
            Uvs.Add(uv);
            return Positions.Count - 1;
        }

        // A triangle wound to face the way given.
        public void Facing(int a, int b, int c, Vector3 facing)
        {
            bool turned = Vector3.Dot(Vector3.Cross(Positions[b] - Positions[a], Positions[c] - Positions[a]), facing) < 0f;
            Triangles.Add(a);
            Triangles.Add(turned ? c : b);
            Triangles.Add(turned ? b : c);
        }

        // A flat face with corners of its own; UVs in metres, for a wood grain later.
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, float width, float height)
        {
            int first = Add(a, normal, new Vector2(0f, 0f));
            Add(b, normal, new Vector2(0f, height));
            Add(c, normal, new Vector2(width, height));
            Add(d, normal, new Vector2(width, 0f));
            Facing(first, first + 1, first + 2, normal);
            Facing(first, first + 2, first + 3, normal);
        }

        // In the space of the object that will draw it.
        public Mesh ToMesh(string name, Transform space)
        {
            Matrix4x4 toLocal = space.worldToLocalMatrix;
            Matrix4x4 normalToLocal = space.localToWorldMatrix.transpose;
            var positions = new List<Vector3>(Positions.Count);
            var normals = new List<Vector3>(Normals.Count);
            for (int i = 0; i < Positions.Count; i++)
            {
                positions.Add(toLocal.MultiplyPoint3x4(Positions[i]));
                normals.Add(normalToLocal.MultiplyVector(Normals[i]).normalized);
            }
            var mesh = new Mesh { name = name };
            if (positions.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, Uvs);
            mesh.SetTriangles(Triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    // A new file each time, not written over the old one: the editor goes on drawing the old
    // shape of a mesh rewritten in place until it restarts.
    private static Mesh Store(Mesh mesh, string path)
    {
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null) AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    // Matte brown, made once; after that it is the project's to tune.
    private static Material DockMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material != null) return material;
        material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "DockMat" };
        material.SetColor("_BaseColor", Brown);
        material.SetColor("_Color", Brown);
        material.SetFloat("_Smoothness", 0f);
        material.SetFloat("_Metallic", 0f);
        material.SetFloat("_SpecularHighlights", 0f);
        material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        material.SetFloat("_EnvironmentReflections", 0f);
        material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        AssetDatabase.CreateAsset(material, MaterialPath);
        return material;
    }

    private static float Range(System.Random random, float low, float high)
    {
        return low + (high - low) * (float)random.NextDouble();
    }

    private static Vector2 RandomInCircle(System.Random random)
    {
        float around = Range(random, 0f, Mathf.PI * 2f), reach = Mathf.Sqrt((float)random.NextDouble());
        return new Vector2(Mathf.Cos(around), Mathf.Sin(around)) * reach;
    }
}
