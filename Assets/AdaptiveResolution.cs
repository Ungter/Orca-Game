using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Keeps the game smooth on weaker GPUs (integrated graphics, high-DPI laptop screens) by
// rendering the 3D view at a lower resolution only while the GPU can't keep up, and raising
// it again when there's room. The UI is always drawn at full resolution; the 3D image is
// upscaled with FSR (the pipeline asset's upscaling filter). A GPU that keeps up never
// leaves full resolution. Installs itself when the game starts.
public class AdaptiveResolution : MonoBehaviour
{
    // GPU time per frame to stay under (60 fps with some room), and the clear headroom
    // needed before resolution goes back up, in milliseconds.
    private const float TargetGpuMs = 14.5f;
    private const float RaiseBelowMs = 11f;
    private const float Step = 0.05f;
    // Seconds of frames averaged per decision; changes are gradual and rare.
    private const float Window = 1f;
    // The 3D view is never rendered shorter than this many pixels.
    private const int MinHeight = 720;

    private readonly FrameTiming[] timings = new FrameTiming[1];
    private UniversalRenderPipelineAsset pipeline;
    private float originalScale;
    private float scale = 1f;
    private double gpuSum;
    private int gpuFrames;
    private float frameSum;
    private int frames;
    private float windowEnd;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        var holder = new GameObject("Adaptive Resolution");
        DontDestroyOnLoad(holder);
        holder.AddComponent<AdaptiveResolution>();
    }

    private void Start()
    {
        pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (pipeline == null)
        {
            Destroy(this);
            return;
        }
        // The asset is shared with the editor; put its value back when play stops.
        originalScale = pipeline.renderScale;
        scale = originalScale;
        windowEnd = Time.unscaledTime + Window;
    }

    private void Update()
    {
        FrameTimingManager.CaptureFrameTimings();
        if (FrameTimingManager.GetLatestTimings(1, timings) > 0 && timings[0].gpuFrameTime > 0.0)
        {
            gpuSum += timings[0].gpuFrameTime;
            gpuFrames++;
        }
        frameSum += Time.unscaledDeltaTime;
        frames++;
        if (Time.unscaledTime < windowEnd)
        {
            return;
        }

        // Frames behind a frozen backdrop, or around a load hitch, say nothing about the game.
        bool representative = !FrozenBackdrop.IsFrozen && frameSum / frames < 0.25f;
        if (representative)
        {
            Decide();
        }
        gpuSum = 0.0;
        gpuFrames = 0;
        frameSum = 0f;
        frames = 0;
        windowEnd = Time.unscaledTime + Window;
    }

    private void Decide()
    {
        float lowest = Mathf.Min(originalScale, MinHeight / (float)Mathf.Max(1, Screen.height));
        float next = scale;
        if (gpuFrames > 0)
        {
            float gpuMs = (float)(gpuSum / gpuFrames);
            if (gpuMs > TargetGpuMs)
            {
                // Pixel cost goes with the square of the scale; step most of the way there.
                float wanted = scale * Mathf.Sqrt(TargetGpuMs / gpuMs);
                next = Mathf.Min(scale - Step, Mathf.Lerp(scale, wanted, 0.7f));
            }
            else if (gpuMs < RaiseBelowMs)
            {
                next = scale + Step;
            }
        }
        else
        {
            // No GPU timings on this device: with vSync a frame that keeps up takes one
            // refresh, so only lower on frames clearly missing it, and never raise.
            double refresh = Screen.currentResolution.refreshRateRatio.value;
            float refreshInterval = refresh > 1.0 ? (float)(1.0 / refresh) : 1f / 60f;
            float target = Mathf.Max(refreshInterval, TargetGpuMs / 1000f);
            if (frameSum / frames > target * 1.3f)
            {
                next = scale - Step;
            }
        }
        next = Mathf.Clamp(next, lowest, originalScale);
        if (Mathf.Abs(next - scale) >= Step * 0.5f)
        {
            scale = next;
            pipeline.renderScale = scale;
        }
    }

    private void OnDestroy()
    {
        if (pipeline != null)
        {
            pipeline.renderScale = originalScale;
        }
    }
}
