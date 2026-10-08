using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Dims an unlit sprite by how much light actually reaches it, so a billboard
// character darkens as it walks into shadow and brightens again in light.
//
// Every frame it adds up each light's contribution at a few points on the body
// (feet, middle, head), the same way URP attenuates point and spot lights. For
// lights that cast shadows it casts a ray from each point to the light; anything
// in between that renders with shadows blocks it. The fraction of points that see
// a light gives soft shadow edges. The total is mapped to a brightness and eased.
[DisallowMultipleComponent]
public class SpriteLightTint : MonoBehaviour
{
    [SerializeField]
    private SpriteRenderer target;

    [Tooltip("Brightness in complete darkness.")]
    [SerializeField, Range(0f, 1f)]
    private float minBrightness = 0.12f;

    [Tooltip("Brightness in full light. Above 1 has no effect on unlit sprites.")]
    [SerializeField, Range(0f, 1f)]
    private float maxBrightness = 1f;

    [Tooltip("How quickly incoming light saturates to full brightness. Higher = brighter in dim light.")]
    [SerializeField]
    private float exposure = 1.5f;

    [Tooltip("How much the sprite takes on the colour of the light falling on it.")]
    [SerializeField, Range(0f, 1f)]
    private float lightColorTint = 0.3f;

    [Tooltip("Seconds to ease most of the way to a new brightness.")]
    [SerializeField]
    private float smoothTime = 0.15f;

    [Tooltip("Sample heights as fractions of the body, from feet (0) to head (1).")]
    [SerializeField]
    private float[] sampleHeights = { 0.1f, 0.5f, 0.9f };

    [SerializeField]
    private LayerMask occluders = ~0;

    private const float LightRefreshInterval = 1f;
    private const float DirectionalRayLength = 500f;

    private readonly List<Light> lights = new List<Light>();
    private readonly Dictionary<Collider, bool> castsShadow = new Dictionary<Collider, bool>();
    private readonly RaycastHit[] hits = new RaycastHit[16];
    private Vector3[] samples;
    private CharacterController body;
    private float nextLightRefresh;
    private Color current = Color.white;
    private bool initialised;

    private void Awake()
    {
        if (target == null)
        {
            target = GetComponent<SpriteRenderer>();
        }
        body = GetComponent<CharacterController>();
        samples = new Vector3[Mathf.Max(1, sampleHeights.Length)];
    }

    private void LateUpdate()
    {
        if (target == null)
        {
            return;
        }

        if (Time.time >= nextLightRefresh)
        {
            nextLightRefresh = Time.time + LightRefreshInterval;
            lights.Clear();
            lights.AddRange(FindObjectsByType<Light>(FindObjectsSortMode.None));
        }

        UpdateSamples();
        Color incoming = IncomingLight();
        float energy = incoming.r * 0.2126f + incoming.g * 0.7152f + incoming.b * 0.0722f;
        float brightness = Mathf.Lerp(minBrightness, maxBrightness, 1f - Mathf.Exp(-energy * exposure));

        Color hue = Color.white;
        float peak = Mathf.Max(incoming.r, Mathf.Max(incoming.g, incoming.b));
        if (peak > 1e-4f)
        {
            hue = Color.Lerp(Color.white, incoming / peak, lightColorTint);
        }

        Color goal = hue * brightness;
        float t = initialised ? 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(smoothTime, 1e-4f) * 3f) : 1f;
        initialised = true;
        current = Color.Lerp(current, goal, t);
        target.color = new Color(current.r, current.g, current.b, target.color.a);
    }

    private void UpdateSamples()
    {
        Vector3 bottom;
        float height;
        if (body != null)
        {
            Vector3 centre = transform.TransformPoint(body.center);
            height = body.height * transform.lossyScale.y;
            bottom = centre - Vector3.up * (height * 0.5f);
        }
        else
        {
            Bounds bounds = target.bounds;
            height = bounds.size.y;
            bottom = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        }

        for (int i = 0; i < samples.Length; i++)
        {
            float h = i < sampleHeights.Length ? sampleHeights[i] : 0.5f;
            samples[i] = bottom + Vector3.up * (height * h);
        }
    }

    private Color IncomingLight()
    {
        Color total = RenderSettings.ambientMode == AmbientMode.Flat || RenderSettings.ambientMode == AmbientMode.Trilight
            ? RenderSettings.ambientLight * RenderSettings.ambientIntensity
            : Color.black;

        int layer = gameObject.layer;
        foreach (Light light in lights)
        {
            if (light == null || !light.isActiveAndEnabled || light.intensity <= 0f || (light.cullingMask & (1 << layer)) == 0)
            {
                continue;
            }

            float lit = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                lit += Contribution(light, samples[i]);
            }
            total += light.color * (light.intensity * lit / samples.Length);
        }
        return total;
    }

    private float Contribution(Light light, Vector3 point)
    {
        Vector3 toLight;
        float distance;
        float attenuation;
        switch (light.type)
        {
            case LightType.Directional:
                toLight = -light.transform.forward;
                distance = DirectionalRayLength;
                attenuation = 1f;
                break;

            case LightType.Point:
            case LightType.Spot:
                Vector3 offset = light.transform.position - point;
                float distSqr = offset.sqrMagnitude;
                float rangeSqr = light.range * light.range;
                if (distSqr >= rangeSqr)
                {
                    return 0f;
                }
                distance = Mathf.Sqrt(distSqr);
                toLight = offset / Mathf.Max(distance, 1e-4f);
                // URP's falloff: inverse square with a smooth fade to zero at the range.
                float fade = 1f - (distSqr / rangeSqr) * (distSqr / rangeSqr);
                attenuation = fade * fade / Mathf.Max(distSqr, 1e-4f);
                if (light.type == LightType.Spot)
                {
                    float cosOuter = Mathf.Cos(light.spotAngle * 0.5f * Mathf.Deg2Rad);
                    float cosInner = Mathf.Cos(light.innerSpotAngle * 0.5f * Mathf.Deg2Rad);
                    float cos = Vector3.Dot(light.transform.forward, -toLight);
                    float cone = Mathf.Clamp01((cos - cosOuter) / Mathf.Max(cosInner - cosOuter, 1e-4f));
                    attenuation *= cone * cone;
                }
                break;

            default:
                return 0f;
        }

        if (attenuation <= 0f)
        {
            return 0f;
        }
        if (light.shadows != LightShadows.None && Blocked(point, toLight, distance))
        {
            return 0f;
        }
        return attenuation;
    }

    // True when something that renders with shadows sits between the point and the light.
    private bool Blocked(Vector3 point, Vector3 direction, float distance)
    {
        int count = Physics.RaycastNonAlloc(point, direction, hits, distance - 0.05f, occluders, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider hit = hits[i].collider;
            if (hit.transform.IsChildOf(transform))
            {
                continue;
            }
            if (CastsShadow(hit))
            {
                return true;
            }
        }
        return false;
    }

    // Matches what the renderer does: only geometry that casts shadows makes shade
    // (lamp heads, invisible walls and triggers don't).
    private bool CastsShadow(Collider collider)
    {
        bool result;
        if (!castsShadow.TryGetValue(collider, out result))
        {
            // Children count too: lamp poles cast through a shadows-only child mesh.
            result = false;
            foreach (Renderer renderer in collider.GetComponentsInChildren<Renderer>())
            {
                if (renderer.enabled && renderer.shadowCastingMode != ShadowCastingMode.Off)
                {
                    result = true;
                    break;
                }
            }
            castsShadow[collider] = result;
        }
        return result;
    }
}
