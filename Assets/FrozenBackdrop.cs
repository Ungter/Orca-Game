using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

// While the pause menu or the inventory's inspection view covers the game, the world only
// shows through a dark tint (and under the pause menu it's paused), so re-rendering all of
// it every frame is wasted work. Instead the camera renders it once into a texture, a
// full-screen image shows that texture beneath every other UI, and the camera draws
// nothing (no scene, shadows, post-processing or skybox) until the overlay closes.
// Installs itself when the game starts.
public class FrozenBackdrop : MonoBehaviour
{
    public static bool IsFrozen { get; private set; }

    private struct CameraState
    {
        public int cullingMask;
        public CameraClearFlags clearFlags;
        public Color background;
        public bool shadows;
        public bool postProcessing;
        public CameraOverrideOption depthTexture;
        public CameraOverrideOption opaqueTexture;
    }

    private Canvas canvas;
    private RawImage picture;
    private RenderTexture frame;
    private Camera frozenCamera;
    private CameraState saved;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        var holder = new GameObject("Frozen Backdrop");
        DontDestroyOnLoad(holder);
        holder.AddComponent<FrozenBackdrop>();
    }

    private void Awake()
    {
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Beneath every other canvas; IMGUI menus draw over canvases anyway.
        canvas.sortingOrder = short.MinValue;
        canvas.enabled = false;

        picture = new GameObject("Frozen Frame", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        picture.transform.SetParent(transform, false);
        picture.raycastTarget = false;
        picture.rectTransform.anchorMin = Vector2.zero;
        picture.rectTransform.anchorMax = Vector2.one;
        picture.rectTransform.offsetMin = Vector2.zero;
        picture.rectTransform.offsetMax = Vector2.zero;
    }

    // After the menus' Update has opened or closed them this frame, before rendering.
    private void LateUpdate()
    {
        bool covered = PauseMenu.IsPaused || InventoryInspection.IsOpen;
        if (covered && !IsFrozen)
        {
            Freeze();
        }
        else if (!covered && IsFrozen)
        {
            Thaw();
        }
    }

    private void Freeze()
    {
        Camera view = Camera.main;
        if (view == null)
        {
            return;
        }
        var descriptor = new RenderTextureDescriptor(Screen.width, Screen.height, RenderTextureFormat.ARGB32, 24) { sRGB = true };
        frame = new RenderTexture(descriptor) { name = "Frozen Frame" };
        var request = new RenderPipeline.StandardRequest { destination = frame };
        if (!RenderPipeline.SupportsRenderRequest(view, request))
        {
            ReleaseFrame();
            return;
        }
        // The full camera, post-processing and all, exactly as it would reach the screen.
        RenderPipeline.SubmitRenderRequest(view, request);

        UniversalAdditionalCameraData data = view.GetUniversalAdditionalCameraData();
        saved = new CameraState
        {
            cullingMask = view.cullingMask,
            clearFlags = view.clearFlags,
            background = view.backgroundColor,
            shadows = data.renderShadows,
            postProcessing = data.renderPostProcessing,
            depthTexture = data.requiresDepthOption,
            opaqueTexture = data.requiresColorOption,
        };
        view.cullingMask = 0;
        view.clearFlags = CameraClearFlags.SolidColor;
        view.backgroundColor = Color.black;
        data.renderShadows = false;
        data.renderPostProcessing = false;
        data.requiresDepthOption = CameraOverrideOption.Off;
        data.requiresColorOption = CameraOverrideOption.Off;

        frozenCamera = view;
        picture.texture = frame;
        canvas.enabled = true;
        IsFrozen = true;
    }

    private void Thaw()
    {
        // The camera can be gone already, e.g. when HOME reloads the scene from the pause menu.
        if (frozenCamera != null)
        {
            UniversalAdditionalCameraData data = frozenCamera.GetUniversalAdditionalCameraData();
            frozenCamera.cullingMask = saved.cullingMask;
            frozenCamera.clearFlags = saved.clearFlags;
            frozenCamera.backgroundColor = saved.background;
            data.renderShadows = saved.shadows;
            data.renderPostProcessing = saved.postProcessing;
            data.requiresDepthOption = saved.depthTexture;
            data.requiresColorOption = saved.opaqueTexture;
        }
        frozenCamera = null;
        canvas.enabled = false;
        picture.texture = null;
        ReleaseFrame();
        IsFrozen = false;
    }

    private void ReleaseFrame()
    {
        if (frame != null)
        {
            frame.Release();
            Destroy(frame);
            frame = null;
        }
    }

    private void OnDestroy()
    {
        if (IsFrozen)
        {
            Thaw();
        }
    }
}
