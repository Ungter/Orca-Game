using UnityEngine;
using UnityEngine.Rendering;

// The sea past the map's square ocean: a flat plane OceanHorizon.FarOceanSize across, lying just
// under the scene's "ocean" and drawn with its material, so the world-tiled water carries straight
// on past the old edges and the water shader melts it into the sky far out (Orca/Pixel Water).
// It sits in SampleScene ("Far Ocean") and keeps itself in place in the editor too, so the
// endless sea shows in the Scene view; OceanHorizon adds one at runtime to scenes without it.
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class FarOcean : MonoBehaviour
{
    private void OnEnable()
    {
        Refresh();
    }

    // Centres it under the ocean, sizes it and gives it the ocean's water. False without an ocean.
    public bool Refresh()
    {
        OceanHorizon.PassSkyColours(RenderSettings.skybox);
        Renderer ocean = OceanHorizon.FindOcean(gameObject.scene);
        if (ocean == null) return false;

        Bounds sea = ocean.bounds;
        var position = new Vector3(sea.center.x, sea.max.y - OceanHorizon.FarOceanDrop, sea.center.z);
        if ((transform.position - position).sqrMagnitude > 1e-6f) transform.position = position;
        if (transform.rotation != Quaternion.identity) transform.rotation = Quaternion.identity;
        Mesh mesh = GetComponent<MeshFilter>().sharedMesh;
        float meshSize = mesh != null ? Mathf.Max(mesh.bounds.size.x, mesh.bounds.size.z) : 0f;
        if (meshSize > 0f)
        {
            float across = OceanHorizon.FarOceanSize / meshSize;
            var scale = new Vector3(across, 1f, across);
            if ((transform.localScale - scale).sqrMagnitude > 1e-4f) transform.localScale = scale;
        }

        MeshRenderer water = GetComponent<MeshRenderer>();
        if (water.sharedMaterial != ocean.sharedMaterial) water.sharedMaterial = ocean.sharedMaterial;
        water.shadowCastingMode = ShadowCastingMode.Off;
        water.receiveShadows = false;
        water.lightProbeUsage = LightProbeUsage.Off;
        water.reflectionProbeUsage = ReflectionProbeUsage.Off;
        water.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        return true;
    }
}
