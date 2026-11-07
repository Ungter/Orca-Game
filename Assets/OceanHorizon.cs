using UnityEngine;
using UnityEngine.SceneManagement;

// Makes the sea look endless. The map's ocean is a square, and from the title's orbit its edges
// showed against the sky. A far bigger copy of the water (FarOcean, "Far Ocean": the same
// material, so the world-tiled texture carries straight on) lies just under the ocean, and the
// skybox's gradient is passed to the water shader, which melts distant water into the sky's own
// colour well before the camera's far plane (Orca/Pixel Water). SampleScene has its Far Ocean
// saved in it; on every scene load this refreshes it, or lays one in a scene without.
public static class OceanHorizon
{
    public const string OceanName = "ocean";
    public const string FarOceanName = "Far Ocean";
    // The far ocean's width, in metres: past any camera's far plane, so its edge is never seen.
    public const float FarOceanSize = 8000f;
    // Just under the ocean, so the ocean itself always wins where they overlap. Both draw the same
    // water, so even where depth can't tell them apart nothing changes.
    public const float FarOceanDrop = 0.02f;

    // Every load, including the pause menu's restart. Unsubscribing first keeps play mode
    // without domain reload to one handler.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Install(scene);

    // Also after the first scene loads, in case sceneLoaded fired before Register.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap() => Install(SceneManager.GetActiveScene());

    // Passes the sky's colours on and makes sure the scene's ocean has its far ocean. Returns the
    // far ocean, or null when the scene has no ocean.
    public static GameObject Install(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded) return null;
        PassSkyColours(RenderSettings.skybox);
        return Lay(FindOcean(scene));
    }

    // The far ocean for this ocean, in its scene: the one already there (refreshed), or a new one.
    public static GameObject Lay(Renderer ocean)
    {
        if (ocean == null) return null;
        Scene scene = ocean.gameObject.scene;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            FarOcean existing = root.GetComponentInChildren<FarOcean>(true);
            if (existing == null) continue;
            existing.Refresh();
            return existing.gameObject;
        }

        // Unity's own plane: 10 m across in 200 triangles, scaled up by FarOcean.
        GameObject far = GameObject.CreatePrimitive(PrimitiveType.Plane);
        far.name = FarOceanName;
        Collider solid = far.GetComponent<Collider>();
        if (Application.isPlaying) Object.Destroy(solid);
        else Object.DestroyImmediate(solid);
        SceneManager.MoveGameObjectToScene(far, scene);
        far.AddComponent<FarOcean>().Refresh();
        Debug.Log("[OceanHorizon] Laid a far ocean under " + ocean.name + " in " + scene.name);
        return far;
    }

    // The skybox's gradient, for the water to fade into. Without the night sky the water fades
    // to black, which a plain dark sky matches well enough.
    public static void PassSkyColours(Material sky)
    {
        Color Colour(string property) => sky != null && sky.HasProperty(property) ? sky.GetColor(property) : Color.black;
        Shader.SetGlobalColor("_SkyHorizonColor", Colour("_HorizonColor"));
        Shader.SetGlobalColor("_SkyZenithColor", Colour("_ZenithColor"));
        Shader.SetGlobalColor("_SkyGroundColor", Colour("_GroundColor"));
        Shader.SetGlobalFloat("_SkyHorizonSharpness",
            sky != null && sky.HasProperty("_HorizonSharpness") ? sky.GetFloat("_HorizonSharpness") : 1f);
    }

    // The scene's ocean: the renderer named "ocean".
    public static Renderer FindOcean(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded) return null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.name == OceanName) return renderer;
            }
        }
        return null;
    }
}
