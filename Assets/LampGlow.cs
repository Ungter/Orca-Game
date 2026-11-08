using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// The sodium-orange light inside a lamp head: a tight pool of light with hard-edged
// shadows, dark between lamps, against the cold moonlight. It's a wide spot light aimed
// straight down rather than a point light: a shadowed point light renders the map six
// times a frame (once per cube face), a spot once, and with the plaza's four lamps in view
// that was most of the frame. Nothing above the lamp heads is lit. It owns the Light's
// settings: edit them here, since they are re-applied whenever the lamp is enabled or changed.
[ExecuteAlways]
[RequireComponent(typeof(Light))]
public class LampGlow : MonoBehaviour
{
    // Rendering layer of the lamp poles' shadows-only copies ("Lamp Pole Shadows" in Tags
    // and Layers). Lamp lights leave it out of their shadows; the moon includes it.
    public const int PoleShadowLayer = 8;

    // A shadowed lamp renders the map again every frame, which is most of the cost of the
    // island on a weak GPU. In play mode only the two lamps nearest the camera keep their
    // shadows; the rest fade theirs out, and back in as the camera comes closer, so nothing
    // pops. All of them keep their light. In the editor every lamp keeps its shadow.
    private const int ShadowedLamps = 2;
    private const float ShadowFadeTime = 0.6f;
    private const float RankInterval = 0.25f;
    // Crisp shadow edges, for the noir look.
    private const LightShadows ShadowType = LightShadows.Hard;
    // Cone, in degrees: everything below 15 degrees under the horizon, the edge softened from
    // InnerSpotAngle out so the pool fades rather than ending in a ring.
    private const float SpotAngle = 150f;
    private const float InnerSpotAngle = 110f;

    private static readonly List<LampGlow> Lamps = new List<LampGlow>();
    private static int rankedFrame = -1;
    private static float nextRank;

    [SerializeField]
    private Color color = new Color(1f, 0.66f, 0.3f);

    [SerializeField]
    private float intensity = 100f;

    // Short enough that the pools end and the ground between lamps falls dark.
    [SerializeField]
    private float range = 50f;

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
        LightShadows shadows = lamp.shadowStrength > 0f ? ShadowType : LightShadows.None;
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
        lamp.type = LightType.Spot;
        lamp.spotAngle = SpotAngle;
        lamp.innerSpotAngle = InnerSpotAngle;
        // Straight down in the world, whatever the head's own rotation.
        Quaternion down = Quaternion.LookRotation(Vector3.down, Vector3.forward);
        if (Quaternion.Angle(transform.rotation, down) > 0.01f)
        {
            transform.rotation = down;
        }
        lamp.color = color;
        lamp.intensity = intensity;
        lamp.range = Mathf.Max(0.1f, range);
        lamp.shadows = castShadows ? ShadowType : LightShadows.None;

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
