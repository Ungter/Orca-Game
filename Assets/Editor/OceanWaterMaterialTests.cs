using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public class OceanWaterMaterialTests
{
    private const string TexturePath = "Assets/Materials/Water.png";
    private const string MaterialPath = "Assets/Materials/OceanWaterMat.mat";

    [Test]
    public void WaterTexturePreservesPixelArtAndRepeatsWithMipmaps()
    {
        Texture2D water = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        var importer = AssetImporter.GetAtPath(TexturePath) as TextureImporter;
        Assert.IsNotNull(water);
        Assert.IsNotNull(importer);
        Assert.AreEqual(256, water.width);
        Assert.AreEqual(256, water.height);
        Assert.AreEqual(FilterMode.Point, water.filterMode);
        Assert.AreEqual(TextureWrapMode.Repeat, water.wrapModeU);
        Assert.AreEqual(TextureWrapMode.Repeat, water.wrapModeV);
        Assert.Greater(water.mipmapCount, 1);
        Assert.IsTrue(importer.sRGBTexture);
        Assert.AreEqual(TextureImporterType.Default, importer.textureType);
        Assert.AreEqual(TextureImporterCompression.Uncompressed, importer.textureCompression);
    }

    [Test]
    public void OceanMaterialUsesTheWaterTileAtSixteenPixelsPerMetre()
    {
        Texture2D water = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        Material ocean = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        Assert.IsNotNull(water);
        Assert.IsNotNull(ocean);
        Assert.AreEqual("Orca/Pixel Water", ocean.shader.name);
        ShaderUtil.CompilePass(ocean, 0, true);
        Assert.IsFalse(ShaderUtil.ShaderHasError(ocean.shader), "The ocean shader must compile.");
        Assert.AreEqual(water, ocean.GetTexture("_BaseMap"));
        Assert.AreEqual(Vector2.one * 0.0625f, ocean.GetTextureScale("_BaseMap"));
        Assert.AreEqual(Color.white, ocean.GetColor("_BaseColor"));
        Vector4 scroll = ocean.GetVector("_TextureScroll");
        Assert.Greater(new Vector2(scroll.x, scroll.y).sqrMagnitude, 0f);
    }

    [Test]
    public void ReplacementWaterDoesNotUseTheOldProceduralEffect()
    {
        Material ocean = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        Assert.IsNotNull(ocean);
        Assert.AreEqual("Orca/Pixel Water", ocean.shader.name);
        Assert.IsFalse(ocean.HasProperty("_TextureStrength"));
        Assert.IsFalse(ocean.HasProperty("_DeepColor"));
        Assert.IsFalse(ocean.HasProperty("_WaveHeight"));
        Assert.IsFalse(ocean.HasProperty("_RippleStrength"));
        Assert.IsFalse(ocean.HasProperty("_SkyReflection"));
        Assert.IsFalse(ocean.HasProperty("_ShoreFoamAmount"));
        Assert.AreEqual(0, ocean.FindPass("Unlit"));
    }

    [Test]
    public void ReplacementWaterRendersTheTextureWithoutTheOldEffect()
    {
        Material ocean = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        Assert.IsNotNull(ocean);
        var material = new Material(ocean);
        material.SetTexture("_BaseMap", Texture2D.redTexture);
        var mesh = new Mesh
        {
            vertices = new[]
            {
                new Vector3(-1f, 0f, -1f), new Vector3(1f, 0f, -1f),
                new Vector3(-1f, 0f, 1f), new Vector3(1f, 0f, 1f)
            },
            triangles = new[] { 0, 2, 1, 2, 3, 1 }
        };
        var holder = new GameObject("Water render test", typeof(Camera));
        Camera camera = holder.GetComponent<Camera>();
        camera.enabled = false;
        camera.orthographic = true;
        camera.orthographicSize = 1f;
        camera.aspect = 1f;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 10f;
        camera.transform.SetPositionAndRotation(Vector3.up * 5f, Quaternion.Euler(90f, 0f, 0f));
        RenderTexture target = RenderTexture.GetTemporary(32, 32, 24);
        RenderTexture previous = RenderTexture.active;
        var pixel = new Texture2D(1, 1, TextureFormat.RGB24, false);
        var commands = new CommandBuffer();
        string[] fogKeywords = { "FOG_LINEAR", "FOG_EXP", "FOG_EXP2" };
        try
        {
            commands.SetRenderTarget(target);
            commands.ClearRenderTarget(true, true, Color.magenta);
            commands.SetViewProjectionMatrices(camera.worldToCameraMatrix,
                GL.GetGPUProjectionMatrix(camera.projectionMatrix, true));
            foreach (string keyword in fogKeywords) commands.DisableShaderKeyword(keyword);
            commands.DrawMesh(mesh, Matrix4x4.identity, material);
            foreach (string keyword in fogKeywords)
            {
                if (Shader.IsKeywordEnabled(keyword)) commands.EnableShaderKeyword(keyword);
            }
            Graphics.ExecuteCommandBuffer(commands);
            RenderTexture.active = target;
            pixel.ReadPixels(new Rect(16, 16, 1, 1), 0, 0);
            pixel.Apply();
            Color color = pixel.GetPixel(0, 0);
            Assert.Greater(color.r, 0.99f);
            Assert.Less(color.g, 0.01f);
            Assert.Less(color.b, 0.01f, "The texture should not be mixed with procedural water.");
        }
        finally
        {
            RenderTexture.active = previous;
            commands.Release();
            RenderTexture.ReleaseTemporary(target);
            Object.DestroyImmediate(pixel);
            Object.DestroyImmediate(holder);
            Object.DestroyImmediate(mesh);
            Object.DestroyImmediate(material);
        }
    }

    [Test]
    public void PixelWaterRipplesAnimateWithoutScrollingAndStayContinuousAcrossTheFarOcean()
    {
        Material ocean = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        Assert.IsNotNull(ocean);
        Assert.Greater(ocean.GetFloat("_RippleAmplitude"), 0f);
        Assert.Greater(ocean.GetFloat("_RippleSpeed"), 0f);
        var material = new Material(ocean);
        material.SetVector("_TextureScroll", Vector4.zero);
        var mesh = new Mesh
        {
            vertices = new[]
            {
                new Vector3(-4f, 0f, -4f), new Vector3(4f, 0f, -4f),
                new Vector3(-4f, 0f, 4f), new Vector3(4f, 0f, 4f)
            },
            triangles = new[] { 0, 2, 1, 2, 3, 1 }
        };
        var holder = new GameObject("Animated water render test", typeof(Camera));
        Camera camera = holder.GetComponent<Camera>();
        camera.enabled = false;
        camera.orthographic = true;
        camera.orthographicSize = 4f;
        camera.aspect = 1f;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 10f;
        camera.transform.SetPositionAndRotation(Vector3.up * 5f, Quaternion.Euler(90f, 0f, 0f));
        RenderTexture target = RenderTexture.GetTemporary(64, 64, 24);
        RenderTexture previous = RenderTexture.active;
        var pixels = new Texture2D(64, 64, TextureFormat.RGB24, false);
        var commands = new CommandBuffer();
        string[] fogKeywords = { "FOG_LINEAR", "FOG_EXP", "FOG_EXP2" };
        Vector4 savedTime = Shader.GetGlobalVector("_Time");
        Vector4 savedCamera = Shader.GetGlobalVector("_WorldSpaceCameraPos");
        Vector4 savedProjection = Shader.GetGlobalVector("_ProjectionParams");
        try
        {
            Color32[] Frame(float seconds, float drop = 0f)
            {
                commands.Clear();
                commands.SetRenderTarget(target);
                commands.ClearRenderTarget(true, true, Color.magenta);
                commands.SetViewProjectionMatrices(camera.worldToCameraMatrix,
                    GL.GetGPUProjectionMatrix(camera.projectionMatrix, true));
                commands.SetGlobalVector("_Time", new Vector4(seconds / 20f, seconds, seconds * 2f, seconds * 3f));
                commands.SetGlobalVector("_WorldSpaceCameraPos", camera.transform.position);
                commands.SetGlobalVector("_ProjectionParams", new Vector4(1f, 0.1f, 10f, 0.1f));
                foreach (string keyword in fogKeywords) commands.DisableShaderKeyword(keyword);
                commands.DrawMesh(mesh, Matrix4x4.Translate(Vector3.down * drop), material);
                foreach (string keyword in fogKeywords)
                    if (Shader.IsKeywordEnabled(keyword)) commands.EnableShaderKeyword(keyword);
                commands.SetGlobalVector("_Time", savedTime);
                commands.SetGlobalVector("_WorldSpaceCameraPos", savedCamera);
                commands.SetGlobalVector("_ProjectionParams", savedProjection);
                Graphics.ExecuteCommandBuffer(commands);
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 64, 64), 0, 0);
                pixels.Apply();
                return pixels.GetPixels32();
            }

            Color32[] first = Frame(0f);
            foreach (float seconds in new[] { 0.25f, 0.5f, 1f })
            {
                Color32[] next = Frame(seconds);
                int changed = 0;
                for (int i = 0; i < first.Length; i++)
                    if (!first[i].Equals(next[i])) changed++;
                Assert.Greater(changed, 0, $"Ripples must move the water pixels after {seconds} seconds.");
                CollectionAssert.AreEqual(next, Frame(seconds, OceanHorizon.FarOceanDrop),
                    "The ocean and its lower extension must draw matching ripples.");
            }
            material.SetFloat("_RippleSpeed", 0f);
            CollectionAssert.AreEqual(Frame(0f), Frame(2f), "Zero ripple speed and drift must freeze the artwork.");
        }
        finally
        {
            RenderTexture.active = previous;
            commands.Release();
            RenderTexture.ReleaseTemporary(target);
            Object.DestroyImmediate(pixels);
            Object.DestroyImmediate(holder);
            Object.DestroyImmediate(mesh);
            Object.DestroyImmediate(material);
        }
    }

    [Test]
    public void SceneOceanUsesTheTexturedWaterMaterial()
    {
        Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Additive);
        try
        {
            Material ocean = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            Assert.IsNotNull(ocean);
            Renderer water = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer.name == "ocean")
                    {
                        water = renderer;
                        break;
                    }
                }
                if (water != null) break;
            }
            Assert.IsNotNull(water);
            Assert.AreEqual(ocean, water.sharedMaterial);
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }
}
