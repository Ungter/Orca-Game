using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// The warm point light inside a lamp head. It owns the Light's settings: edit them
// here, since they are re-applied whenever the lamp is enabled or changed.
[ExecuteAlways]
[RequireComponent(typeof(Light))]
public class LampGlow : MonoBehaviour
{
    // Rendering layer of the lamp poles' shadows-only copies ("Lamp Pole Shadows" in Tags
    // and Layers). Lamp lights leave it out of their shadows; the moon includes it.
    public const int PoleShadowLayer = 8;

    // A shadowed point light renders the map six times a frame, which is most of the
    // cost of the island on a weak GPU. In play mode only the lamps nearest the camera keep
    // their shadows (enough for every lamp lighting the area round the player); the rest
    // fade theirs out, and back in as the camera comes closer, so nothing pops. All of them
    // keep their light. In the editor every lamp keeps its shadow.
    private const int ShadowedLamps = 6;
    private const float ShadowFadeTime = 0.6f;
    private const float RankInterval = 0.25f;

    private static readonly List<LampGlow> Lamps = new List<LampGlow>();
    private static int rankedFrame = -1;
    private static float nextRank;

    [SerializeField]
    private Color color = new Color(1f, 0.82f, 0.35f);

    [SerializeField]
    private float intensity = 300f;

    [SerializeField]
    private float range = 70f;

    [SerializeField]
    private bool castShadows = true;

    private Light lamp;
    private bool nearCamera = true;

    private void OnEnable()
    {
        Lamps.Add(this);
        Apply();
    }

    private void OnDisable()
    {
        Lamps.Remove(this);
    }

    private void Update()
    {
        if (!Application.isPlaying || !castShadows)
        {
            return;
        }
        RankLamps();
        lamp.shadowStrength = Mathf.MoveTowards(lamp.shadowStrength, nearCamera ? 1f : 0f, Time.unscaledDeltaTime / ShadowFadeTime);
        LightShadows shadows = lamp.shadowStrength > 0f ? LightShadows.Soft : LightShadows.None;
        if (lamp.shadows != shadows)
        {
            lamp.shadows = shadows;
        }
    }

    // Once per frame across all lamps, a few times a second: the nearest ShadowedLamps to
    // the camera are marked to keep their shadows.
    private static void RankLamps()
    {
        if (rankedFrame == Time.frameCount || Time.unscaledTime < nextRank)
        {
            return;
        }
        rankedFrame = Time.frameCount;
        nextRank = Time.unscaledTime + RankInterval;
        Camera view = Camera.main;
        if (view == null)
        {
            return;
        }
        Vector3 eye = view.transform.position;
        Lamps.Sort((a, b) => (a.transform.position - eye).sqrMagnitude.CompareTo((b.transform.position - eye).sqrMagnitude));
        for (int i = 0; i < Lamps.Count; i++)
        {
            Lamps[i].nearCamera = i < ShadowedLamps;
        }
    }

    private void OnValidate()
    {
        Apply();
    }

    private void Apply()
    {
        lamp = GetComponent<Light>();
        lamp.type = LightType.Point;
        lamp.color = color;
        lamp.intensity = intensity;
        lamp.range = Mathf.Max(0.1f, range);
        lamp.shadows = castShadows ? LightShadows.Soft : LightShadows.None;

        // The light sits inside the lamp head, so the head must not cast shadows
        // or it would block its own light in every direction.
        Renderer head = transform.parent != null ? transform.parent.GetComponent<Renderer>() : null;
        if (head != null)
        {
            head.shadowCastingMode = ShadowCastingMode.Off;
        }

        // Likewise the pole under it, which would shade a disc of ground round its foot.
        // Only lights whose shadow layers include PoleShadowLayer see the pole's shadow.
        if (TryGetComponent(out UniversalAdditionalLightData urp))
        {
            urp.customShadowLayers = true;
            urp.shadowRenderingLayers = urp.renderingLayers.value & ~(1u << PoleShadowLayer);
        }
    }
}
