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

    [SerializeField]
    private Color color = new Color(1f, 0.82f, 0.35f);

    [SerializeField]
    private float intensity = 300f;

    [SerializeField]
    private float range = 70f;

    [SerializeField]
    private bool castShadows = true;

    private void OnEnable()
    {
        Apply();
    }

    private void OnValidate()
    {
        Apply();
    }

    private void Apply()
    {
        Light lamp = GetComponent<Light>();
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
