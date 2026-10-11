using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Gravel spilling over the sidewalk's edges. The sidewalk slab's sides drop straight down to
// the ground; this builds a ragged gravel slope from every top edge down to the ground below,
// plus small loose heaps just past it, so the path reads as a bed of gravel instead of a box.
// Everything is worked out in scene metres and returned in the model's root space.
public static class SidewalkSpill
{
    // Sampling along the edges; also the size of the ragged steps in the outer edge.
    private const float SampleStep = 0.5f;
    // Slope width: a base plus a share of the height it has to fall (gravel's angle of repose
    // is roughly 35-40 degrees, so about 1.2 m out per metre down), jittered along the edge.
    private const float BaseWidth = 0.35f;
    private const float WidthPerDrop = 1.2f;
    private const float MaxWidth = 2.5f;
    // Taller drops than this are a hole or a cliff, not ground next to the path; skip them.
    private const float MaxDrop = 3f;
    private const float WidthJitter = 0.45f;
    // Where the bend in the slope sits and how far it sags, giving a slightly convex heap.
    private const float BendAt = 0.45f;
    private const float BendDrop = 0.35f;
    private const float Bumpiness = 0.04f;
    // Lift above the ground so the slope's foot doesn't z-fight with it.
    private const float GroundLift = 0.03f;
    // Loose heaps past the foot of the slope.
    private const float HeapSpacing = 1.1f;
    private const float HeapChance = 0.6f;
    private const float HeapRadiusMin = 0.15f;
    private const float HeapRadiusMax = 0.4f;
    private const float HeapHeightMin = 0.04f;
    private const float HeapHeightMax = 0.1f;
    private const int HeapSides = 7;

    private struct Sample
    {
        public bool Valid;
        public Vector3 Inner, Bend, Outer;
        public float Width;
    }

    private class Ground
    {
        private readonly List<Vector3> corners = new List<Vector3>();
        private readonly List<Vector4> bounds = new List<Vector4>();

        public Ground(Mesh mesh, Matrix4x4 toMetres)
        {
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                Vector3 a = toMetres.MultiplyPoint3x4(vertices[triangles[t]]);
                Vector3 b = toMetres.MultiplyPoint3x4(vertices[triangles[t + 1]]);
                Vector3 c = toMetres.MultiplyPoint3x4(vertices[triangles[t + 2]]);
                // Only the upward-facing surface counts, not the block's underside.
                if (Vector3.Cross(b - a, c - a).normalized.y < 0.2f) continue;
                corners.Add(a);
                corners.Add(b);
                corners.Add(c);
                bounds.Add(new Vector4(Mathf.Min(a.x, b.x, c.x), Mathf.Max(a.x, b.x, c.x),
                    Mathf.Min(a.z, b.z, c.z), Mathf.Max(a.z, b.z, c.z)));
            }
        }

        // Highest ground surface under (x, z) that is below `ceiling`, if any.
        public bool HeightAt(float x, float z, float ceiling, out float height)
        {
            height = float.MinValue;
            for (int i = 0; i < bounds.Count; i++)
            {
                Vector4 bb = bounds[i];
                if (x < bb.x || x > bb.y || z < bb.z || z > bb.w) continue;
                Vector3 a = corners[i * 3], b = corners[i * 3 + 1], c = corners[i * 3 + 2];
                float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                if (Mathf.Abs(d) < 1e-9f) continue;
                float l1 = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / d;
                float l2 = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / d;
                float l3 = 1f - l1 - l2;
                if (l1 < -1e-5f || l2 < -1e-5f || l3 < -1e-5f) continue;
                float y = l1 * a.y + l2 * b.y + l3 * c.y;
                if (y < ceiling && y > height) height = y;
            }
            return height > float.MinValue;
        }
    }

    // sidewalkToMetres / groundToMetres map each mesh into root space scaled to metres.
    public static Mesh Build(Mesh sidewalk, Matrix4x4 sidewalkToMetres, Mesh groundMesh, Matrix4x4 groundToMetres, float metresPerUnit)
    {
        var ground = new Ground(groundMesh, groundToMetres);

        // Weld the slab's vertices by position: hard edges split them in the imported mesh.
        Vector3[] raw = sidewalk.vertices;
        var weldIndex = new Dictionary<Vector3Int, int>();
        var points = new List<Vector3>();
        var welded = new int[raw.Length];
        for (int i = 0; i < raw.Length; i++)
        {
            Vector3 p = sidewalkToMetres.MultiplyPoint3x4(raw[i]);
            var key = new Vector3Int(Mathf.RoundToInt(p.x * 1000f), Mathf.RoundToInt(p.y * 1000f), Mathf.RoundToInt(p.z * 1000f));
            if (!weldIndex.TryGetValue(key, out int index))
            {
                index = points.Count;
                points.Add(p);
                weldIndex.Add(key, index);
            }
            welded[i] = index;
        }

        // The top's outline: edges of upward-facing triangles that only one of them uses.
        var edgeUse = new Dictionary<long, int>();
        var edgeThird = new Dictionary<long, int>();
        int[] triangles = sidewalk.triangles;
        for (int t = 0; t + 2 < triangles.Length; t += 3)
        {
            int a = welded[triangles[t]], b = welded[triangles[t + 1]], c = welded[triangles[t + 2]];
            Vector3 normal = Vector3.Cross(points[b] - points[a], points[c] - points[a]);
            if (normal.sqrMagnitude < 1e-12f || normal.normalized.y < 0.6f) continue;
            CountEdge(edgeUse, edgeThird, a, b, c, points.Count);
            CountEdge(edgeUse, edgeThird, b, c, a, points.Count);
            CountEdge(edgeUse, edgeThird, c, a, b, points.Count);
        }

        var outline = new List<Vector2Int>();
        var outward = new Dictionary<int, Vector3>();
        foreach (KeyValuePair<long, int> edge in edgeUse)
        {
            if (edge.Value != 1) continue;
            int a = (int)(edge.Key / points.Count), b = (int)(edge.Key % points.Count);
            Vector3 along = points[b] - points[a];
            Vector3 perp = new Vector3(along.z, 0f, -along.x).normalized;
            Vector3 inside = points[edgeThird[edge.Key]] - (points[a] + points[b]) * 0.5f;
            if (Vector3.Dot(perp, inside) > 0f) perp = -perp;
            outline.Add(new Vector2Int(a, b));
            outward[a] = (outward.TryGetValue(a, out Vector3 oa) ? oa : Vector3.zero) + perp;
            outward[b] = (outward.TryGetValue(b, out Vector3 ob) ? ob : Vector3.zero) + perp;
        }
        if (outline.Count == 0) return null;

        var vertices = new List<Vector3>();
        var indices = new List<int>();
        foreach (Vector2Int edge in outline)
        {
            Vector3 pa = points[edge.x], pb = points[edge.y];
            Vector3 da = Flat(outward[edge.x]), db = Flat(outward[edge.y]);
            float length = Vector3.Distance(pa, pb);
            int segments = Mathf.Max(1, Mathf.CeilToInt(length / SampleStep));
            Vector3 along = (pb - pa) / length;
            var samples = new Sample[segments + 1];
            for (int s = 0; s <= segments; s++)
            {
                float t = s / (float)segments;
                samples[s] = MakeSample(ground, Vector3.Lerp(pa, pb, t), Flat(Vector3.Lerp(da, db, t)));
            }

            for (int s = 0; s < segments; s++)
            {
                Sample s0 = samples[s], s1 = samples[s + 1];
                if (!s0.Valid || !s1.Valid) continue;
                int i = vertices.Count;
                vertices.Add(s0.Inner);
                vertices.Add(s0.Bend);
                vertices.Add(s0.Outer);
                vertices.Add(s1.Inner);
                vertices.Add(s1.Bend);
                vertices.Add(s1.Outer);
                AddQuad(vertices, indices, i, i + 1, i + 4, i + 3);
                AddQuad(vertices, indices, i + 1, i + 2, i + 5, i + 4);
            }

            // Loose heaps just past the foot of the slope.
            int heaps = Mathf.FloorToInt(length / HeapSpacing);
            for (int h = 0; h < heaps; h++)
            {
                Vector3 at = Vector3.Lerp(pa, pb, (h + 0.5f) / heaps);
                if (Hash(at, 1) > HeapChance) continue;
                Sample near = samples[Mathf.Clamp(Mathf.RoundToInt((h + 0.5f) / heaps * segments), 0, segments)];
                if (!near.Valid) continue;
                Vector3 dir = Flat(Vector3.Lerp(da, db, (h + 0.5f) / heaps));
                Vector3 centre = at + dir * near.Width * (1.05f + 0.6f * Hash(at, 2))
                    + along * (Hash(at, 3) - 0.5f) * HeapSpacing * 0.6f;
                AddHeap(ground, vertices, indices, centre, at.y,
                    Mathf.Lerp(HeapRadiusMin, HeapRadiusMax, Hash(at, 4)),
                    Mathf.Lerp(HeapHeightMin, HeapHeightMax, Hash(at, 5)));
            }
        }
        if (indices.Count == 0) return null;

        // Same metre-based UVs as the slab's top, so the pebbles carry on over the edge.
        var uvs = new Vector2[vertices.Count];
        var local = new Vector3[vertices.Count];
        for (int i = 0; i < vertices.Count; i++)
        {
            uvs[i] = new Vector2(vertices[i].x, vertices[i].z);
            local[i] = vertices[i] / metresPerUnit;
        }
        var mesh = new Mesh { name = "Sidewalk Spill" };
        mesh.indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.vertices = local;
        mesh.uv = uvs;
        mesh.SetTriangles(indices, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void CountEdge(Dictionary<long, int> use, Dictionary<long, int> third, int a, int b, int c, int count)
    {
        long key = (long)Mathf.Min(a, b) * count + Mathf.Max(a, b);
        use[key] = use.TryGetValue(key, out int n) ? n + 1 : 1;
        third[key] = c;
    }

    private static Sample MakeSample(Ground ground, Vector3 edge, Vector3 dir)
    {
        var sample = new Sample();
        float ceiling = edge.y + 0.05f;
        Vector3 probe = edge + dir * BaseWidth;
        if (!ground.HeightAt(probe.x, probe.z, ceiling, out float near))
        {
            return sample;
        }
        float drop = Mathf.Max(0f, edge.y - near);
        if (drop > MaxDrop)
        {
            return sample;
        }
        float width = Mathf.Min(BaseWidth + drop * WidthPerDrop, MaxWidth)
            * (1f + WidthJitter * (Noise(edge.x * 0.7f, edge.z * 0.7f) * 2f - 1f));

        Vector3 outer = edge + dir * width;
        float foot = ground.HeightAt(outer.x, outer.z, ceiling, out float f) ? f : near;
        Vector3 bend = edge + dir * width * BendAt;
        float bendGround = ground.HeightAt(bend.x, bend.z, ceiling, out float g) ? g : foot;
        float bendY = edge.y - (edge.y - bendGround) * BendDrop
            + (Noise(bend.x * 1.9f + 31f, bend.z * 1.9f) - 0.5f) * 2f * Bumpiness;

        sample.Valid = true;
        sample.Width = width;
        sample.Inner = edge;
        sample.Bend = new Vector3(bend.x, Mathf.Max(bendY, bendGround + GroundLift), bend.z);
        sample.Outer = new Vector3(outer.x, foot + GroundLift, outer.z);
        return sample;
    }

    // A low cone with a wobbly rim, sitting on the ground.
    private static void AddHeap(Ground ground, List<Vector3> vertices, List<int> indices, Vector3 centre, float ceiling, float radius, float height)
    {
        if (!ground.HeightAt(centre.x, centre.z, ceiling + 0.05f, out float baseY)) return;
        int first = vertices.Count;
        vertices.Add(new Vector3(centre.x, baseY + height, centre.z));
        float turn = Hash(centre, 6) * Mathf.PI * 2f;
        for (int i = 0; i < HeapSides; i++)
        {
            float angle = turn + i * Mathf.PI * 2f / HeapSides;
            float r = radius * Mathf.Lerp(0.7f, 1.2f, Hash(centre, 10 + i));
            float x = centre.x + Mathf.Cos(angle) * r;
            float z = centre.z + Mathf.Sin(angle) * r;
            float y = ground.HeightAt(x, z, ceiling + 0.05f, out float gy) ? gy : baseY;
            vertices.Add(new Vector3(x, y + 0.01f, z));
        }
        for (int i = 0; i < HeapSides; i++)
        {
            AddTriangle(vertices, indices, first, first + 1 + i, first + 1 + (i + 1) % HeapSides);
        }
    }

    private static void AddQuad(List<Vector3> vertices, List<int> indices, int a, int b, int c, int d)
    {
        AddTriangle(vertices, indices, a, b, c);
        AddTriangle(vertices, indices, a, c, d);
    }

    // Winds every triangle to face up, whichever order the corners came in.
    private static void AddTriangle(List<Vector3> vertices, List<int> indices, int a, int b, int c)
    {
        Vector3 normal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
        if (normal.sqrMagnitude < 1e-12f) return;
        indices.Add(a);
        if (normal.y >= 0f)
        {
            indices.Add(b);
            indices.Add(c);
        }
        else
        {
            indices.Add(c);
            indices.Add(b);
        }
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 1e-12f ? v.normalized : Vector3.forward;
    }

    // Deterministic 0..1 from a position, so reimports build the same spill.
    private static float Hash(Vector3 p, int salt)
    {
        return Hash(Mathf.RoundToInt(p.x * 100f), Mathf.RoundToInt(p.z * 100f) + salt * 7919);
    }

    private static float Hash(int x, int z)
    {
        uint h = (uint)(x * 374761393 + z * 668265263);
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;
        return h / (float)uint.MaxValue;
    }

    // Smooth value noise, 0..1, continuous across edges so shared corners match.
    private static float Noise(float x, float z)
    {
        int ix = Mathf.FloorToInt(x), iz = Mathf.FloorToInt(z);
        float fx = x - ix, fz = z - iz;
        fx = fx * fx * (3f - 2f * fx);
        fz = fz * fz * (3f - 2f * fz);
        float a = Hash(ix, iz), b = Hash(ix + 1, iz), c = Hash(ix, iz + 1), d = Hash(ix + 1, iz + 1);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fz);
    }
}
