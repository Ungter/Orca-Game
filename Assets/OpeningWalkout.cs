using System;
using UnityEngine;
using UnityEngine.Rendering;

// The starting house's imported door has a centre pivot, so swing it around a world-space
// hinge without reparenting or changing the model. TitleScreen owns the shot and input lock.
public sealed class OpeningWalkout : IDisposable
{
    public const string DoorPath = "world/group/pasted__building2/pasted__pCube54";
    public const float Duration = 3.8f;
    private const float WalkStart = 0.65f;
    private const float WalkDuration = 2.6f;

    private readonly PlayerController player;
    private readonly CharacterController controller;
    private readonly bool controllerWasEnabled;
    private readonly Transform door;
    private readonly Vector3 closedPosition;
    private readonly Quaternion closedRotation;
    private readonly Vector3 hinge;
    private readonly Vector3 outward;
    private readonly Vector3 side;
    private readonly Vector3 startGround;
    private readonly Vector3 endGround;
    private readonly GameObject doorway;
    private readonly Mesh doorwayMesh;
    private readonly Material doorwayMaterial;
    private Vector3 ground;
    private bool walking;
    private bool released;

    public Vector3 DoorGround { get; }

    private OpeningWalkout(PlayerController player, Transform door, Vector3 outward, float width, float thickness)
    {
        this.player = player;
        this.door = door;
        this.outward = outward;
        side = Vector3.Cross(Vector3.up, outward);
        closedPosition = door.position;
        closedRotation = door.rotation;
        Bounds bounds = door.GetComponent<Renderer>().bounds;
        DoorGround = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        hinge = bounds.center - side * (width * 0.5f);
        endGround = GroundAt(DoorGround + outward * 4.5f);
        startGround = DoorGround - outward * 0.9f;
        startGround.y = Mathf.Max(startGround.y, endGround.y);
        ground = startGround;
        // This model has a solid facade behind its door. Cover just the opening with
        // a dark, unlit interior while the panel swings out, rather than revealing a wall.
        doorway = new GameObject("Opening doorway");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(doorway, door.gameObject.scene);
        doorway.transform.SetPositionAndRotation(bounds.center + outward * (thickness * 0.5f + 0.01f),
            Quaternion.LookRotation(outward));
        doorway.transform.localScale = new Vector3(width * 0.98f, bounds.size.y * 0.98f, 1f);
        doorwayMesh = new Mesh { name = "Opening doorway" };
        doorwayMesh.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f),
            new Vector3(0.5f, 0.5f), new Vector3(-0.5f, 0.5f),
        };
        doorwayMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        doorwayMesh.RecalculateNormals();
        doorway.AddComponent<MeshFilter>().sharedMesh = doorwayMesh;
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
        doorwayMaterial = new Material(shader) { color = new Color(0.012f, 0.009f, 0.006f) };
        if (doorwayMaterial.HasProperty("_Cull")) doorwayMaterial.SetFloat("_Cull", 0f);
        MeshRenderer interior = doorway.AddComponent<MeshRenderer>();
        interior.sharedMaterial = doorwayMaterial;
        interior.shadowCastingMode = ShadowCastingMode.Off;
        interior.receiveShadows = false;
        doorway.SetActive(false);
        controller = player.GetComponent<CharacterController>();
        controllerWasEnabled = controller.enabled;
        controller.enabled = false;
        player.PlaceOnGround(startGround);
    }

    public static bool TryCreate(Transform map, PlayerController player, out OpeningWalkout opening)
    {
        opening = null;
        Transform door = map != null ? map.Find(DoorPath) : null;
        if (door == null || player == null || door.GetComponent<Renderer>() == null) return false;
        MeshFilter filter = door.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null) return false;

        Bounds local = filter.sharedMesh.bounds;
        Vector3 x = door.TransformVector(Vector3.right * local.size.x);
        Vector3 z = door.TransformVector(Vector3.forward * local.size.z);
        Vector3 normal = Vector3.ProjectOnPlane(x.sqrMagnitude < z.sqrMagnitude ? x : z, Vector3.up).normalized;
        if (normal.sqrMagnitude < 0.5f) return false;
        // The scene's authored spawn is outside this facade, and fixes which face is the exterior.
        if (Vector3.Dot(normal, player.transform.position - door.GetComponent<Renderer>().bounds.center) < 0f)
            normal = -normal;
        float width = Mathf.Max(x.magnitude, z.magnitude);
        opening = new OpeningWalkout(player, door, normal, width, Mathf.Min(x.magnitude, z.magnitude));
        return true;
    }

    public void Tick(float time)
    {
        if (released || player == null || door == null) return;
        float open = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(time / 0.8f));
        float close = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((time - 2.8f) / 0.8f));
        float amount = open * (1f - close);
        Quaternion swing = Quaternion.AngleAxis(-105f * amount, Vector3.up);
        door.SetPositionAndRotation(hinge + swing * (closedPosition - hinge), swing * closedRotation);
        doorway.SetActive(amount > 0.01f);

        float progress = Mathf.Clamp01((time - WalkStart) / WalkDuration);
        walking = time >= WalkStart && progress < 1f;
        ground = Vector3.Lerp(startGround, endGround, Mathf.SmoothStep(0f, 1f, progress));
        // Keep level with the raised path across the narrow gap before its first step.
        if (Vector3.Dot(ground - DoorGround, outward) > 0.6f)
            ground.y = Mathf.Max(ground.y, GroundAt(ground).y);
        player.PlaceOnGround(ground);
    }

    private Vector3 GroundAt(Vector3 point)
    {
        RaycastHit[] hits = Physics.RaycastAll(point + Vector3.up * 2f, Vector3.down, 6f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float highest = float.NegativeInfinity;
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.transform.IsChildOf(player.transform) ||
                hit.collider.transform.IsChildOf(door) || hit.normal.y < 0.5f) continue;
            highest = Mathf.Max(highest, hit.point.y);
        }
        if (!float.IsNegativeInfinity(highest)) point.y = highest;
        return point;
    }

    public void GetCameraPose(float progress, out Vector3 position, out Quaternion rotation)
    {
        progress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress));
        position = DoorGround + outward * Mathf.Lerp(10.5f, 11.5f, progress) +
            side * Mathf.Lerp(4.5f, 3.3f, progress) + Vector3.up * Mathf.Lerp(3.5f, 3.9f, progress);
        Vector3 focus = Vector3.Lerp(DoorGround, ground, 0.4f) +
            Vector3.up * Mathf.Lerp(2.3f, 1.3f, progress);
        rotation = Quaternion.LookRotation(focus - position, Vector3.up);
    }

    // Run after the shot's camera moves, so the billboard and walk direction match its view.
    public void AnimatePlayer(float deltaTime)
    {
        if (player != null)
            player.AnimateCinematicWalk(!released && walking ? outward : Vector3.zero, deltaTime);
    }

    public void Dispose()
    {
        if (released) return;
        released = true;
        if (door != null) door.SetPositionAndRotation(closedPosition, closedRotation);
        if (doorway != null) doorway.SetActive(false);
        Discard(doorway);
        Discard(doorwayMesh);
        Discard(doorwayMaterial);
        if (player != null)
        {
            player.AnimateCinematicWalk(Vector3.zero, 0f);
            controller.enabled = controllerWasEnabled;
        }
    }

    private static void Discard(UnityEngine.Object item)
    {
        if (item == null) return;
        if (Application.isPlaying) UnityEngine.Object.Destroy(item);
        else UnityEngine.Object.DestroyImmediate(item);
    }
}
