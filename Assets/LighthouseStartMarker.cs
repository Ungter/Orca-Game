using UnityEngine;
using UnityEngine.Rendering;

// A physical start pad and a distant label, rather than a prompt that only appears at four metres.
public sealed class LighthouseStartMarker : MonoBehaviour
{
    private Material material;
    private Mesh ringMesh;
    private Mesh arrowMesh;
    private Transform arrow;
    private static readonly Color Gold = new Color32(255, 206, 55, 255);

    public static LighthouseStartMarker Create(Transform entrance)
    {
        var holder = new GameObject("Stacking start - gold ring");
        holder.transform.SetParent(entrance, false);
        holder.hideFlags = HideFlags.DontSave;
        var marker = holder.AddComponent<LighthouseStartMarker>();
        marker.Build();
        return marker;
    }

    private void Build()
    {
        material = PlaceholderMaterials.Unlit(Gold);
        material.name = "Stacking start gold";
        const int segments = 64;
        var vertices = new Vector3[segments * 2];
        var triangles = new int[segments * 6];
        for (int i = 0; i < segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            vertices[i * 2] = direction * 2.15f + Vector3.up * 0.09f;
            vertices[i * 2 + 1] = direction * 2.45f + Vector3.up * 0.09f;
            int next = (i + 1) % segments * 2;
            int t = i * 6;
            triangles[t] = i * 2; triangles[t + 1] = next; triangles[t + 2] = i * 2 + 1;
            triangles[t + 3] = i * 2 + 1; triangles[t + 4] = next; triangles[t + 5] = next + 1;
        }
        ringMesh = new Mesh { name = "Stacking start ring", hideFlags = HideFlags.DontSave };
        ringMesh.vertices = vertices;
        ringMesh.triangles = triangles;
        ringMesh.RecalculateBounds();
        AddMesh(gameObject, ringMesh);

        arrow = new GameObject("Start arrow").transform;
        arrow.SetParent(transform, false);
        arrowMesh = new Mesh { name = "Stacking start arrow", hideFlags = HideFlags.DontSave };
        arrowMesh.vertices = new[]
        {
            new Vector3(0f, 2.2f), new Vector3(-0.9f, 3.2f), new Vector3(0.9f, 3.2f),
            new Vector3(-0.25f, 3.2f), new Vector3(0.25f, 3.2f),
            new Vector3(-0.25f, 4.3f), new Vector3(0.25f, 4.3f)
        };
        arrowMesh.triangles = new[] { 0, 1, 2, 3, 5, 4, 4, 5, 6 };
        arrowMesh.RecalculateBounds();
        AddMesh(arrow.gameObject, arrowMesh);
    }

    private void AddMesh(GameObject holder, Mesh mesh)
    {
        holder.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = holder.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    private void LateUpdate()
    {
        Camera view = Camera.main;
        if (view != null) arrow.rotation = Quaternion.Euler(0f, view.transform.eulerAngles.y, 0f);
        arrow.localPosition = Vector3.up * (Mathf.Sin(Time.unscaledTime * 2.5f) * 0.15f);
    }

    private void OnGUI()
    {
        if (Event.current.type != EventType.Repaint || Interactables.Busy || TitleScreen.IsOpen || PauseMenu.IsPaused) return;
        Transform player = Interactables.FindPlayer();
        Camera view = Camera.main;
        if (player == null || view == null || player.gameObject.scene != gameObject.scene) return;
        float distance = Vector3.ProjectOnPlane(player.position - transform.position, Vector3.up).magnitude;
        if (distance <= 4f || distance > 90f) return;
        Vector3 screen = view.WorldToScreenPoint(transform.position + Vector3.up * 4.8f);
        int p = Mathf.Max(1, Mathf.RoundToInt(2f * InteractPrompt.UIScale()));
        string label = "STACKING START  " + Mathf.CeilToInt(distance) + "M";
        float width = PixelFont.Measure(label) * p;
        float x = screen.z > 0f ? screen.x - width * 0.5f : (Screen.width - width) * 0.5f;
        float y = screen.z > 0f ? Screen.height - screen.y : Screen.height - 100f;
        x = Mathf.Clamp(x, 12f, Mathf.Max(12f, Screen.width - width - 12f));
        y = Mathf.Clamp(y, 70f, Screen.height - 100f);
        PixelFont.Draw(new Vector2(x, y), label, p, Gold, Color.black);
        const string hint = "STAND IN THE GOLD RING";
        PixelFont.Draw(new Vector2(x + (width - PixelFont.Measure(hint) * p) * 0.5f, y + 12f * p),
            hint, p, Color.white, Color.black);
    }

    private void OnDestroy()
    {
        Release(material);
        Release(ringMesh);
        Release(arrowMesh);
    }

    private static void Release(Object owned)
    {
        if (owned == null) return;
        if (Application.isPlaying) Destroy(owned);
        else DestroyImmediate(owned);
    }
}
