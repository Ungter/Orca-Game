using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

// The lighthouse's final game is watched from the lantern, and the villain is small and far off.
// When something on the island (a house, a tree, a lamp) stands between the eye and him, a cone
// of view opens from the eye to him: inside it whatever is in the way is cleared, so he and the
// ground round him show through.
//
// A second camera, posed like the player's, draws only the screen square round him, with the
// things in the cone moved for that frame to a layer it doesn't draw (the GPU Resident Drawer
// doesn't take per-camera renderer switches, but it does take camera culling masks). Its picture
// is laid over the screen in a soft circle (Orca/Sight Cone).
public sealed class MouseTrapSightline : IDisposable
{
    // An unused layer: things in the cone sit on it for a frame.
    public const int HiddenLayer = 30;
    // Radius of the cleared circle at the villain, in metres.
    public const float WorldRadius = 8f;

    private readonly Camera main;
    private readonly Camera view;
    private readonly Material material;
    private readonly IReadOnlyList<Renderer> occluders;
    private readonly List<(GameObject owner, int layer)> lifted = new List<(GameObject, int)>();
    private readonly int mainMask;
    private RenderTexture texture;
    private Rect pixels;

    public float Opacity { get; private set; }
    public bool Blocked { get; private set; }
    public int Cleared => lifted.Count;
    public Camera View => view;

    public MouseTrapSightline(Camera main, IReadOnlyList<Renderer> occluders, Material material)
    {
        this.main = main;
        this.occluders = occluders;
        this.material = material;
        mainMask = main.cullingMask;
        main.cullingMask |= 1 << HiddenLayer;
        // In the game's scene, so it goes with it when the ending reloads the scene.
        var holder = new GameObject("Sightline camera");
        if (main.gameObject.scene.IsValid() && main.gameObject.scene.isLoaded)
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(holder, main.gameObject.scene);
        view = holder.AddComponent<Camera>();
        view.CopyFrom(main);
        view.targetTexture = null;
        view.enabled = false;
        view.depth = main.depth - 1f;
        view.cullingMask = mainMask & ~(1 << HiddenLayer);
        UniversalAdditionalCameraData from = main.GetUniversalAdditionalCameraData();
        UniversalAdditionalCameraData to = view.GetUniversalAdditionalCameraData();
        if (from != null && to != null)
        {
            to.renderPostProcessing = from.renderPostProcessing;
            to.antialiasing = from.antialiasing;
            to.renderShadows = from.renderShadows;
            to.volumeLayerMask = from.volumeLayerMask;
            to.requiresColorOption = from.requiresColorOption;
            to.requiresDepthOption = from.requiresDepthOption;
        }
    }

    // Each frame after the player's camera has moved: is he hidden, and if so clear the cone and
    // set the second camera on the square round him.
    public void Update(Vector3 target, float size, float deltaTime)
    {
        Restore();
        if (main == null) return;
        Vector3 eye = main.transform.position;
        Vector3 toTarget = target - eye;
        float distance = toTarget.magnitude;
        Blocked = distance > 1f && IsHidden(eye, target, size, distance);
        Opacity = Mathf.MoveTowards(Opacity, Blocked ? 1f : 0f, Mathf.Max(0f, deltaTime) * 4f);
        Vector3 screen = main.WorldToScreenPoint(target);
        if (Opacity <= 0f || screen.z <= 0f)
        {
            view.enabled = false;
            return;
        }

        float halfHeight = Mathf.Tan(main.fieldOfView * 0.5f * Mathf.Deg2Rad) * distance;
        float radius = Mathf.Clamp(WorldRadius / halfHeight * main.pixelHeight * 0.5f, 48f, main.pixelHeight * 0.3f);
        pixels = new Rect(screen.x - radius, screen.y - radius, radius * 2f, radius * 2f);
        int side = Mathf.Clamp(Mathf.CeilToInt(radius * 2f / 32f) * 32, 64, 1024);
        if (texture == null || texture.width != side)
        {
            if (texture != null)
            {
                texture.Release();
                if (Application.isPlaying) Object.Destroy(texture);
                else Object.DestroyImmediate(texture);
            }
            texture = new RenderTexture(side, side, 24) { name = "Sightline view" };
            texture.Create();
        }
        view.transform.SetPositionAndRotation(main.transform.position, main.transform.rotation);
        view.fieldOfView = main.fieldOfView;
        view.nearClipPlane = main.nearClipPlane;
        view.farClipPlane = main.farClipPlane;
        view.projectionMatrix = SubRect(main.projectionMatrix, pixels, main.pixelWidth, main.pixelHeight);
        view.targetTexture = texture;
        view.enabled = true;

        // Clear everything in the cone from the eye to him (apex at the eye), in front of him.
        Vector3 axis = toTarget / distance;
        float reach = distance - size;
        foreach (Renderer r in occluders)
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
            Bounds b = r.bounds;
            Vector3 offset = b.center - eye;
            float along = Vector3.Dot(offset, axis);
            float spread = b.extents.magnitude;
            if (along + spread < 0f || along - spread > reach) continue;
            float across = (offset - axis * along).magnitude;
            float coneAt = WorldRadius * 1.15f * Mathf.Clamp(along, 0f, distance) / distance;
            if (across > coneAt + spread) continue;
            lifted.Add((r.gameObject, r.gameObject.layer));
            r.gameObject.layer = HiddenLayer;
        }
    }

    // Something stands across the line from the eye to his middle or his top.
    private bool IsHidden(Vector3 eye, Vector3 target, float size, float distance)
    {
        foreach (Vector3 point in new[] { target, target + Vector3.up * size * 0.45f })
        {
            Vector3 toPoint = point - eye;
            float reach = toPoint.magnitude - size * 0.8f;
            var ray = new Ray(eye, toPoint.normalized);
            bool clear = true;
            foreach (Renderer r in occluders)
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                Bounds b = r.bounds;
                b.extents *= 0.85f;
                if (b.IntersectRay(ray, out float hit) && hit < reach) { clear = false; break; }
            }
            if (clear) return false;
        }
        return true;
    }

    // The part of a projection that falls in a rectangle of the screen, stretched to fill it.
    private static Matrix4x4 SubRect(Matrix4x4 projection, Rect rect, float width, float height)
    {
        float x0 = rect.xMin / width * 2f - 1f, x1 = rect.xMax / width * 2f - 1f;
        float y0 = rect.yMin / height * 2f - 1f, y1 = rect.yMax / height * 2f - 1f;
        float sx = 2f / (x1 - x0), sy = 2f / (y1 - y0);
        Matrix4x4 crop = Matrix4x4.identity;
        crop.m00 = sx;
        crop.m03 = -sx * (x0 + x1) * 0.5f;
        crop.m11 = sy;
        crop.m13 = -sy * (y0 + y1) * 0.5f;
        return crop * projection;
    }

    // Puts what the cone cleared back on its own layer.
    public void Restore()
    {
        foreach (var (owner, layer) in lifted)
            if (owner != null) owner.layer = layer;
        lifted.Clear();
    }

    public void Suspend()
    {
        Restore();
        if (view != null) view.enabled = false;
    }

    // Lays the cleared view over the screen (call from OnGUI, on Repaint).
    public void Draw()
    {
        if (Opacity <= 0f || texture == null || material == null || view == null || !view.enabled) return;
        material.SetFloat("_Opacity", Opacity);
        // OnGUI counts y from the top; the camera's pixels count from the bottom.
        var rect = new Rect(pixels.x, main.pixelHeight - pixels.yMax, pixels.width, pixels.height);
        Graphics.DrawTexture(rect, texture, material);
    }

    public void Dispose()
    {
        Restore();
        if (main != null) main.cullingMask = mainMask;
        if (view != null)
        {
            view.targetTexture = null;
            if (Application.isPlaying) Object.Destroy(view.gameObject);
            else Object.DestroyImmediate(view.gameObject);
        }
        if (texture != null)
        {
            texture.Release();
            if (Application.isPlaying) Object.Destroy(texture);
            else Object.DestroyImmediate(texture);
        }
    }
}
