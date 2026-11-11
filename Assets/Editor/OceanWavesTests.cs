using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

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
        Assert.AreSame(mesh, OceanWaves.SeaGrid(4, 2f), "Made once per size.");
        Assert.AreEqual(7 * 7, OceanWaves.SeaGrid(5, 2f).vertexCount, "Rounded up to an even number of squares across (6, so 7 x 7 points), so the grid can move two at a time.");
    }

    // The wave surface is marked on its renderer, not in its mesh: the other meshes drawn with the
    // water (the map's ocean, the Far Ocean) have no channel that could say so, and a missing one
    // reads as garbage, which once flung their points about and left black holes in the sea.
    [Test]
    public void OnlyTheWaveSurfaceIsLiftedAndOtherWaterMeshesDrawWhereTheyAre()
    {
        Material water = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/OceanWaterMat.mat");
        Assert.IsNotNull(water);
        var flatSea = GameObject.CreatePrimitive(PrimitiveType.Plane);
        created.Add(flatSea);
        flatSea.name = "ocean";
        flatSea.GetComponent<Renderer>().sharedMaterial = water;
        OceanWaves waves = Waves();

        var block = new MaterialPropertyBlock();
        MeshRenderer surface = waves.GetComponentInChildren<MeshRenderer>(true);
        Assert.IsNotNull(surface, "The waves draw their surface.");
        // Each block says both outright: Unity carries a block's value over to the next draw of
        // the same material whose block leaves it out, so the flat sea drawn after the surface
        // would be taken for it.
        int cutId = Shader.PropertyToID("_OceanFlatCut");
        surface.GetPropertyBlock(block);
        Assert.IsTrue(block.HasFloat(OceanWaves.SurfaceId) && block.HasFloat(cutId), "The surface's block says both.");
        Assert.AreEqual(1f, block.GetFloat(OceanWaves.SurfaceId), "The surface's renderer says it is the wave surface.");
        Assert.AreEqual(0f, block.GetFloat(cutId), "The surface is not cut away.");
        Renderer ocean = OceanHorizon.FindOcean(waves.gameObject.scene);
        Assert.IsNotNull(ocean);
        ocean.GetPropertyBlock(block);
        Assert.IsTrue(block.HasFloat(OceanWaves.SurfaceId) && block.HasFloat(cutId), "The flat sea's block says both.");
        Assert.AreEqual(0f, block.GetFloat(OceanWaves.SurfaceId), "The flat sea is not the wave surface.");
        Assert.AreEqual(1f, block.GetFloat(cutId), "The flat sea is cut away under the waves.");

        // Drawn with the waves running, a plain mesh (positions only, like the flat sea's) stays
        // exactly where it is and fills the view; marked as the surface, the same mesh is lifted
        // and its facets shaded. And the mark never carries over: a plain mesh drawn straight
        // after the surface (over it: the depth is cleared between them) is still left alone.
        var material = new Material(water);
        created.Add(material);
        material.SetTexture("_BaseMap", Texture2D.redTexture);
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_RippleAmplitude", 0f);
        var positions = new List<Vector3>();
        var indices = new List<int>();
        const int Squares = 8;
        for (int j = 0; j <= Squares; j++)
            for (int i = 0; i <= Squares; i++)
                positions.Add(new Vector3(i * 2f - Squares, 0f, j * 2f - Squares));
        for (int j = 0; j < Squares; j++)
        {
            for (int i = 0; i < Squares; i++)
            {
                int a = j * (Squares + 1) + i, b = a + 1, c = a + Squares + 1, d = c + 1;
                indices.AddRange(new[] { a, c, b, b, c, d });
            }
        }
        var mesh = new Mesh();
        created.Add(mesh);
        mesh.SetVertices(positions);
        mesh.SetIndices(indices, MeshTopology.Triangles, 0);
        var view = new GameObject("Wave marker render test", typeof(Camera));
        created.Add(view);
        Camera camera = view.GetComponent<Camera>();
        camera.enabled = false;
        camera.orthographic = true;
        camera.orthographicSize = 3f;
        camera.aspect = 1f;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 20f;
        camera.transform.SetPositionAndRotation(new Vector3(0f, 10f, 0f), Quaternion.Euler(90f, 0f, 0f));
        RenderTexture target = RenderTexture.GetTemporary(48, 48, 24);
        RenderTexture previous = RenderTexture.active;
        var pixels = new Texture2D(48, 48, TextureFormat.RGB24, false);
        created.Add(pixels);
        var commands = new CommandBuffer();
        string[] fogKeywords = { "FOG_LINEAR", "FOG_EXP", "FOG_EXP2" };
        Vector4 savedCamera = Shader.GetGlobalVector("_WorldSpaceCameraPos");
        Vector4 savedProjection = Shader.GetGlobalVector("_ProjectionParams");
        try
        {
            Color32[] Frame(params MaterialPropertyBlock[] draws)
            {
                commands.Clear();
                commands.SetRenderTarget(target);
                commands.ClearRenderTarget(true, true, Color.magenta);
                commands.SetViewProjectionMatrices(camera.worldToCameraMatrix,
                    GL.GetGPUProjectionMatrix(camera.projectionMatrix, true));
                commands.SetGlobalVector("_WorldSpaceCameraPos", camera.transform.position);
                commands.SetGlobalVector("_ProjectionParams", new Vector4(1f, 0.1f, 20f, 0.05f));
                foreach (string keyword in fogKeywords) commands.DisableShaderKeyword(keyword);
                for (int n = 0; n < draws.Length; n++)
                {
                    if (n > 0) commands.ClearRenderTarget(true, false, Color.clear);
                    commands.DrawMesh(mesh, Matrix4x4.Translate(new Vector3(0f, waves.Level, 0f)), material, 0, 0, draws[n]);
                }
                foreach (string keyword in fogKeywords)
                    if (Shader.IsKeywordEnabled(keyword)) commands.EnableShaderKeyword(keyword);
                commands.SetGlobalVector("_WorldSpaceCameraPos", savedCamera);
                commands.SetGlobalVector("_ProjectionParams", savedProjection);
                Graphics.ExecuteCommandBuffer(commands);
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 48, 48), 0, 0);
                pixels.Apply();
                return pixels.GetPixels32();
            }

            var red = new Color32(255, 0, 0, 255);
            string Moved(Color32[] frame)
            {
                int off = 0, darkest = 255;
                foreach (Color32 pixel in frame)
                {
                    if (pixel.Equals(red)) continue;
                    off++;
                    darkest = Mathf.Min(darkest, pixel.r);
                }
                return off == 0 ? "" : off + " of " + frame.Length + " pixels off, darkest red " + darkest;
            }
            Color32[] plain = Frame(new MaterialPropertyBlock());
            Assert.AreEqual("", Moved(plain), "Every pixel is the flat water, unmoved and unshaded: no holes.");
            var marked = new MaterialPropertyBlock();
            marked.SetFloat(OceanWaves.SurfaceId, 1f);
            Assert.AreNotEqual("", Moved(Frame(marked)), "Marked as the wave surface, the same mesh is lifted and shaded.");
            Assert.AreEqual("", Moved(Frame(marked, new MaterialPropertyBlock())),
                "Drawn after the surface, the plain mesh is still unmoved: the mark does not carry over.");
        }
        finally
        {
            RenderTexture.active = previous;
            commands.Release();
            RenderTexture.ReleaseTemporary(target);
        }
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
