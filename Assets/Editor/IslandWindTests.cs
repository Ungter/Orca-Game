using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class IslandWindTests
{
    private readonly List<Object> created = new List<Object>();
    private GameObject player;

    private static Vector2 Palette(int ramp, float along) => new Vector2(along, (ramp + 0.5f) / IslandWind.RampCount);

    [SetUp]
    public void SetUp()
    {
        player = new GameObject("Player") { tag = "Player" };
        created.Add(player);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (Object item in created)
            if (item != null) Object.DestroyImmediate(item);
        created.Clear();
    }

    [Test]
    public void OnlyGrassFernsAndFlowersSwayAndBladesBendMoreTowardsTheirTips()
    {
        Assert.AreEqual(0f, IslandWind.SwayWeight(Palette(2, 0.9f)), "Tree leaves stay put.");
        Assert.AreEqual(0f, IslandWind.SwayWeight(Palette(12, 0.9f)), "So do rocks.");
        Assert.AreEqual(0f, IslandWind.SwayWeight(Palette(6, 0.12f)), 0.001f, "A blade's foot stays in the ground.");
        float middle = IslandWind.SwayWeight(Palette(6, 0.55f));
        Assert.That(middle, Is.InRange(0.1f, 0.6f));
        Assert.Greater(IslandWind.SwayWeight(Palette(7, 0.9f)), middle, "A fern frond's tip moves most.");
        Assert.AreEqual(1f, IslandWind.SwayWeight(Palette(9, 0.15f)), "A flower's head moves with its stem's tip.");
    }

    [Test]
    public void AGustPushesTipsDownwindAndDipsThem()
    {
        Vector3 rest = new Vector3(1f, 0.5f, 2f);
        Assert.AreEqual(rest, IslandWind.Bend(rest, 0f, Vector3.right, 1f, 3f, 0f));
        Vector3 bent = IslandWind.Bend(rest, 1f, Vector3.right, 1f, 0f, 0f);
        Assert.AreEqual(rest.x + IslandWind.SwayAmplitude, bent.x, 0.001f);
        Assert.AreEqual(rest.z, bent.z, 0.001f);
        Assert.Less(bent.y, rest.y);
        Vector3 calm = IslandWind.Bend(rest, 1f, Vector3.right, IslandWind.Breeze, 0f, 0f);
        Assert.Less(calm.x - rest.x, (bent.x - rest.x) * 0.25f, "The breeze barely stirs them.");
    }

    [Test]
    public void AGustRisesHoldsAndDiesAway()
    {
        Assert.AreEqual(0f, IslandWind.Gust(-0.1f, 5f, 1f));
        Assert.AreEqual(0f, IslandWind.Gust(0f, 5f, 1f));
        Assert.AreEqual(0.8f, IslandWind.Gust(2.5f, 5f, 0.8f), 0.001f);
        Assert.Less(IslandWind.Gust(4.5f, 5f, 1f), 1f);
        Assert.AreEqual(0f, IslandWind.Gust(5.1f, 5f, 1f));
    }

    [Test]
    public void NearThePlayerTheGroundCoverBendsLeavesFallAndPetalsFly()
    {
        Mesh cover = Mesh("Cover",
            new[]
            {
                new Vector3(0f, 0f, 0f), new Vector3(0.1f, 0f, 0f), new Vector3(0f, 0.5f, 0f), new Vector3(0.1f, 0.5f, 0f),
                new Vector3(0f, 0.5f, 0.1f), new Vector3(0.1f, 0.5f, 0.1f), new Vector3(0.05f, 0.6f, 0.05f), new Vector3(0.05f, 0.4f, 0.05f),
                new Vector3(3f, 0.2f, 0f), new Vector3(3.2f, 0.2f, 0f), new Vector3(3.1f, 0.6f, 0f), new Vector3(3.1f, 0.6f, 0.1f),
            },
            new[]
            {
                Palette(6, 0.12f), Palette(6, 0.12f), Palette(6, 0.95f), Palette(6, 0.95f),
                Palette(9, 0.95f), Palette(9, 0.95f), Palette(9, 0.95f), Palette(9, 0.15f),
                Palette(12, 0.5f), Palette(12, 0.5f), Palette(12, 0.9f), Palette(12, 0.9f),
            });
        MeshFilter coverFilter = Drawn("Ground Cover 0_0", cover);
        var leafUvs = new Vector2[24];
        var leafPositions = new Vector3[24];
        for (int i = 0; i < 24; i++)
        {
            leafUvs[i] = Palette(2, 0.7f);
            leafPositions[i] = new Vector3(2f + i * 0.1f, 6f, 1f);
        }
        Drawn("Trees 0_0", Mesh("Crown", leafPositions, leafUvs));

        var host = new GameObject("Island wind");
        created.Add(host);
        IslandWind wind = host.AddComponent<IslandWind>();
        wind.Start();
        wind.StartGust(6f, 1f);
        for (int i = 0; i < 20; i++) wind.Step(0.1f);

        Assert.Greater(wind.Strength, 0.9f, "In the thick of the gust.");
        Assert.AreEqual(1, wind.SwayingPatches);
        Mesh bent = coverFilter.sharedMesh;
        created.Add(bent);
        Assert.AreNotSame(cover, bent, "It bends its own copy, not the scenery asset.");
        Vector3[] now = bent.vertices;
        Assert.AreEqual(cover.vertices[0], now[0], "The blade's foot stays put.");
        Vector3 moved = now[2] - cover.vertices[2];
        Assert.Greater(Vector3.Dot(moved, wind.Direction), IslandWind.SwayAmplitude * 0.6f, "Its tip leans downwind.");
        Assert.Greater(Vector3.Dot(now[6] - cover.vertices[6], wind.Direction), IslandWind.SwayAmplitude * 0.6f, "So does the flower's head.");
        Assert.AreEqual(cover.vertices[10], now[10], "A boulder doesn't.");
        Assert.Greater(wind.Leaves.particleCount, 3, "Leaves fall from the crown.");
        Assert.Greater(wind.Petals.particleCount, 1, "Petals fly off the flower.");

        player.transform.position = new Vector3(500f, 0f, 500f);
        wind.Step(0.6f);
        Assert.AreEqual(0, wind.SwayingPatches);
        Assert.AreSame(cover, coverFilter.sharedMesh, "Out of reach, the patch stands straight again.");
    }

    private Mesh Mesh(string name, Vector3[] positions, Vector2[] uvs)
    {
        var mesh = new Mesh { name = name, vertices = positions, uv = uvs };
        var triangles = new List<int>();
        for (int i = 0; i + 2 < positions.Length; i += 3) triangles.AddRange(new[] { i, i + 1, i + 2 });
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        created.Add(mesh);
        return mesh;
    }

    private MeshFilter Drawn(string name, Mesh mesh)
    {
        var holder = new GameObject(name);
        created.Add(holder);
        MeshFilter filter = holder.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        holder.AddComponent<MeshRenderer>();
        return filter;
    }
}
