using UnityEngine;

// A faint warm point light inside a lamp head. Sized from the lamp's own mesh, so it
// reaches the same distance whatever scale the map is placed at.
[ExecuteAlways]
[RequireComponent(typeof(Light))]
public class LampGlow : MonoBehaviour
{
    [SerializeField]
    private Color color = new Color(1f, 0.82f, 0.35f);

    [SerializeField]
    private float intensity = 1.5f;

    [Tooltip("Light reach in multiples of the lamp head's size.")]
    [SerializeField]
    private float rangeInLampSizes = 6f;

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
        lamp.shadows = LightShadows.None;

        float size = 1f;
        MeshFilter head = transform.parent != null ? transform.parent.GetComponent<MeshFilter>() : null;
        if (head != null && head.sharedMesh != null)
        {
            size = Vector3.Scale(head.sharedMesh.bounds.size, head.transform.lossyScale).magnitude;
        }
        lamp.range = Mathf.Max(0.1f, size * rangeInLampSizes);
    }
}
