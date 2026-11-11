using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class OceanWavesTests
{
    private readonly List<Object> created = new List<Object>();
    private OceanWaves previous;

    [SetUp]
    public void SetUp()
    {
        previous = OceanWaves.Active;
    }

    [TearDown]
    public void TearDown()
    {
        foreach (Object item in created)
        {
            if (item != null) Object.DestroyImmediate(item);
        }
        created.Clear();
        // The scene's own waves (if any) take over again on their next update.
        if (previous != null) previous.enabled = true;
    }

    private OceanWaves Waves()
    {
        var holder = new GameObject("Ocean waves test");
        created.Add(holder);
        var waves = holder.AddComponent<OceanWaves>();
        Assert.AreSame(waves, OceanWaves.Active);
        return waves;
    }

    [Test]
    public void TheWavesRiseAndFallSmoothlyWithinTheirHeightAndGoFlatFarOut()
    {
        OceanWaves waves = Waves();
        var centre = new Vector2(10f, -30f);
        bool rose = false, sank = false;
        for (float x = -60f; x <= 60f; x += 3.7f)
        {
            for (float z = -60f; z <= 60f; z += 4.3f)
            {
                float h = waves.HeightAt(centre.x + x, centre.y + z, centre, 12.5f);
                Assert.LessOrEqual(Mathf.Abs(h), waves.Amplitude + 1e-4f);
                rose |= h > 0.2f;
                sank |= h < -0.2f;
                // A fiftieth of a second on, it has only moved a little: no jumps.
                float next = waves.HeightAt(centre.x + x, centre.y + z, centre, 12.52f);
                Assert.Less(Mathf.Abs(next - h), 0.05f, "The water moves smoothly.");
            }
        }
        Assert.IsTrue(rose && sank, "The waves both rise above the sea and sink below it.");

        Assert.AreEqual(waves.HeightAt(3.3f, 7.9f, centre, 12.5f), waves.SurfaceHeightAt(3.3f, 7.9f, centre, 12.5f), 1e-6f,
            "With no hull about, the surface is the waves.");

        Assert.AreEqual(0f, waves.HeightAt(centre.x + waves.FlatBeyond + 1f, centre.y, centre), 1e-5f, "Flat sea beyond the waves' reach.");
        Assert.Less(waves.FlatBeyond, waves.FlatCutRadius, "The flat sea is only cut away where the waves have died down.");
        Assert.Less(waves.FlatCutRadius, waves.HalfWidth, "The cut stays inside the square of the surface.");
    }

    [Test]
    public void TheWaterStaysCalmerByTheDocks()
    {
        OceanWaves waves = Waves();
        GameObject dock = GameObject.CreatePrimitive(PrimitiveType.Cube);
        created.Add(dock);
        dock.transform.localScale = new Vector3(20f, 1f, 6f);
        var serialized = new UnityEditor.SerializedObject(waves);
        UnityEditor.SerializedProperty calm = serialized.FindProperty("calmAround");
        calm.arraySize = 1;
        calm.GetArrayElementAtIndex(0).objectReferenceValue = dock.transform;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        waves.Refresh();
        Assert.AreEqual(1, waves.CalmAreaCount);

        float share = serialized.FindProperty("calmShare").floatValue;
        var centre = Vector2.zero;
        for (float x = -10f; x <= 10f; x += 1.3f)
        {
            Assert.LessOrEqual(Mathf.Abs(waves.HeightAt(x, 0f, centre)), waves.Amplitude * share + 1e-4f, "Right by the dock the waves are low.");
        }
    }

    [Test]
    public void TheSurfaceIsOneConnectedGridOfSharedPoints()
    {
        Mesh mesh = OceanWaves.SeaGrid(4, 2f);
        Assert.AreEqual(5 * 5, mesh.vertexCount, "Neighbouring facets share their points, so the surface has no gaps.");
        Assert.AreEqual(4 * 4 * 2 * 3, mesh.GetIndexCount(0), "Two triangles a square.");
        var kinds = new List<Vector4>();
        mesh.GetUVs(5, kinds);
        Assert.IsTrue(kinds.TrueForAll(k => k.x > 0.5f), "Every vertex says it is part of the wave surface.");
        Assert.AreSame(mesh, OceanWaves.SeaGrid(4, 2f), "Made once per size.");
        Assert.AreEqual(7 * 7, OceanWaves.SeaGrid(5, 2f).vertexCount, "Rounded up to an even number of squares across (6, so 7 x 7 points), so the grid can move two at a time.");
    }

    [Test]
    public void AHullRidesOnTopOfTheWavesNeverDippingUnderThemAndLiesStillWithoutThem()
    {
        var rest = new Vector3(5f, -1.5f, 12f);
        Quaternion heading = Quaternion.Euler(0f, 30f, 0f);
        const float Length = 7f, Width = 2.5f;
        if (OceanWaves.Active != null) OceanWaves.Active.enabled = false;
        Pose still = OceanWaves.Ride(rest, heading, Length, Width);
        Assert.AreEqual(rest, still.position);
        Assert.AreEqual(heading, still.rotation);
        if (previous != null) previous.enabled = true;

        OceanWaves waves = Waves();
        // One moment throughout: in the editor the waves run on real time.
        var centre = new Vector2(rest.x, rest.z);
        const float Time = 21.7f;
        Pose riding = OceanWaves.Ride(rest, heading, Length, Width, centre, Time);
        float heave = riding.position.y - rest.y;
        Assert.AreEqual(rest.x, riding.position.x, 1e-5f);
        Assert.AreEqual(rest.z, riding.position.z, 1e-5f);

        // Nowhere it feels the waves does the water stand above where it rides.
        Vector3 ahead = heading * Vector3.forward, side = heading * Vector3.right;
        Vector3 up = riding.rotation * Vector3.up;
        float bowRow = 0f, sternRow = 0f, highest = float.MinValue;
        for (int i = -1; i <= 1; i++)
        {
            for (int j = -1; j <= 1; j++)
            {
                Vector3 at = ahead * (i * Length * OceanWaves.FeltAlong) + side * (j * Width * OceanWaves.FeltAcross);
                float water = waves.HeightAt(rest.x + at.x, rest.z + at.z, centre, Time);
                float hull = heave - (up.x * at.x + up.z * at.z) / up.y;
                Assert.GreaterOrEqual(hull, water - 1e-4f, "The hull never dips under the water.");
                if (i == 1) bowRow += water;
                if (i == -1) sternRow += water;
                highest = Mathf.Max(highest, water);
            }
        }
        Assert.LessOrEqual(heave, highest + 0.8f, "It rides on the waves, not above them.");
        Assert.LessOrEqual(waves.SurfaceHeightAt(rest.x, rest.z, centre, Time), heave - waves.HullClearance + 1e-4f,
            "The water under it is held down below it.");

        Vector3 nose = riding.rotation * Vector3.forward;
        if (Mathf.Abs(bowRow - sternRow) > 0.1f)
            Assert.AreEqual(Mathf.Sign(bowRow - sternRow), Mathf.Sign(nose.y), "The bow rises when the waves under it are higher.");
        Assert.Less(Quaternion.Angle(heading, riding.rotation), 15f, "It rocks; it does not capsize.");
    }
}
