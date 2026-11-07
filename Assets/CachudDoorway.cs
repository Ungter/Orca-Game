using System;
using UnityEngine;
using UnityEngine.Rendering;

// Al Cachud's entrance when the player knocks. His house's imported door
// (pasted__building2/pasted__pCube54) has a centre pivot, so it swings around a world-space
// hinge without reparenting or changing the model, with a dark interior over the opening
// behind it. He walks out from just inside the door to where he stops to talk, and the door
// closes behind him. The visitor who knocked is set down in front of the door, a little to one
// side, facing it. CachudCallout owns the timing, the shot and the input lock.
public sealed class CachudDoorway : IDisposable
{
    public const string MapName = "IslanddemoV3";
    public const string DoorPath = "world/group/pasted__building2/pasted__pCube54";
    public const float Duration = 3.8f;
    private const float WalkStart = 0.65f;
    private const float WalkDuration = 2.6f;
    // He starts this far inside the door and stops this far out of it; the visitor waits this
    // far out and this far to the side.
    private const float InsideDistance = 0.9f;
    private const float StopDistance = 2f;
    private const float VisitorDistance = 4.2f;
    private const float VisitorSide = 1.4f;
    // A little lift on each step, as he has no walking frames.
    private const float StepBob = 0.05f;
    private const float StepsPerSecond = 1.8f;

    private readonly Transform actor;
    private readonly Transform door;
    private readonly Vector3 closedPosition;
    private readonly Quaternion closedRotation;
    private readonly Vector3 hinge;
    private readonly Vector3 side;
    private readonly Vector3 startGround;
    private readonly Vector3 endGround;
    private readonly GameObject doorway;
    private readonly Mesh doorwayMesh;
    private readonly Material doorwayMaterial;
    private Vector3 ground;
    private bool released;

    public Vector3 DoorGround { get; }
    public Vector3 Outward { get; }
    // Where he stops outside the door to talk.
    public Vector3 StopPoint => endGround;
    public bool IsWalking { get; private set; }

    private CachudDoorway(Transform door, Vector3 outward, float width, float thickness, Transform actor, Transform visitor)
    {
        this.door = door;
        this.actor = actor;
        Outward = outward;
        side = Vector3.Cross(Vector3.up, outward);
        closedPosition = door.position;
        closedRotation = door.rotation;
        Bounds bounds = door.GetComponent<Renderer>().bounds;
        DoorGround = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        hinge = bounds.center - side * (width * 0.5f);
        endGround = GroundAt(DoorGround + outward * StopDistance, visitor);
        startGround = DoorGround - outward * InsideDistance;
        startGround.y = Mathf.Max(startGround.y, endGround.y);
        ground = startGround;
        // This model has a solid facade behind its door. Cover just the opening with
        // a dark, unlit interior while the panel swings out, rather than revealing a wall.
        doorway = new GameObject("Cachud doorway");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(doorway, door.gameObject.scene);
        doorway.transform.SetPositionAndRotation(bounds.center + outward * (thickness * 0.5f + 0.01f),
            Quaternion.LookRotation(outward));
        doorway.transform.localScale = new Vector3(width * 0.98f, bounds.size.y * 0.98f, 1f);
        doorwayMesh = new Mesh { name = "Cachud doorway" };
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

        PlaceActor(0f);
        Vector3 waiting = GroundAt(DoorGround + outward * VisitorDistance + side * VisitorSide, visitor);
        if (visitor != null && IsClear(waiting, visitor))
        {
            if (visitor.TryGetComponent(out PlayerController player)) player.PlaceOnGround(waiting);
            else visitor.position = waiting;
        }
    }

    // The door in the map; false (touching nothing) without it or without someone to come out.
    // visitor: whoever knocked, which also says which face of the door is the outside.
    public static bool TryCreate(Transform map, Transform actor, Transform visitor, out CachudDoorway doorway)
    {
        doorway = null;
        Transform door = map != null ? map.Find(DoorPath) : null;
        if (door == null || actor == null || visitor == null || door.GetComponent<Renderer>() == null) return false;
        MeshFilter filter = door.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null) return false;

        Bounds local = filter.sharedMesh.bounds;
        Vector3 x = door.TransformVector(Vector3.right * local.size.x);
        Vector3 z = door.TransformVector(Vector3.forward * local.size.z);
        Vector3 normal = Vector3.ProjectOnPlane(x.sqrMagnitude < z.sqrMagnitude ? x : z, Vector3.up).normalized;
        if (normal.sqrMagnitude < 0.5f) return false;
        if (Vector3.Dot(normal, visitor.position - door.GetComponent<Renderer>().bounds.center) < 0f)
            normal = -normal;
        float width = Mathf.Max(x.magnitude, z.magnitude);
        doorway = new CachudDoorway(door, normal, width, Mathf.Min(x.magnitude, z.magnitude), actor, visitor);
        return true;
    }

    // time: seconds since the door started to open.
    public void Tick(float time)
    {
        if (released || door == null) return;
        float open = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(time / 0.8f));
        float close = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((time - 2.8f) / 0.8f));
        float amount = open * (1f - close);
        Quaternion swing = Quaternion.AngleAxis(-105f * amount, Vector3.up);
        door.SetPositionAndRotation(hinge + swing * (closedPosition - hinge), swing * closedRotation);
        doorway.SetActive(amount > 0.01f);

        float progress = Mathf.Clamp01((time - WalkStart) / WalkDuration);
        IsWalking = time >= WalkStart && progress < 1f;
        ground = Vector3.Lerp(startGround, endGround, Mathf.SmoothStep(0f, 1f, progress));
        // Keep level with the raised path across the narrow gap before its first step.
        if (Vector3.Dot(ground - DoorGround, Outward) > 0.6f)
            ground.y = Mathf.Max(ground.y, GroundAt(ground, null).y);
        PlaceActor(IsWalking ? Mathf.Abs(Mathf.Sin(time * Mathf.PI * StepsPerSecond)) * StepBob : 0f);
    }

    // From out in front, a little to the side: the door, him coming out and the visitor waiting.
    public void GetCameraPose(float progress, out Vector3 position, out Quaternion rotation)
    {
        progress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress));
        position = DoorGround + Outward * Mathf.Lerp(10.5f, 11.5f, progress) +
            side * Mathf.Lerp(4.5f, 3.3f, progress) + Vector3.up * Mathf.Lerp(3.5f, 3.9f, progress);
        Vector3 focus = Vector3.Lerp(DoorGround, ground, 0.4f) +
            Vector3.up * Mathf.Lerp(2.3f, 1.3f, progress);
        rotation = Quaternion.LookRotation(focus - position, Vector3.up);
    }

    // Ends the entrance wherever it got to: the door closed and him outside where he stops.
    public void Dispose()
    {
        if (released) return;
        released = true;
        IsWalking = false;
        if (door != null) door.SetPositionAndRotation(closedPosition, closedRotation);
        if (doorway != null) doorway.SetActive(false);
        Discard(doorway);
        Discard(doorwayMesh);
        Discard(doorwayMaterial);
        ground = endGround;
        if (actor != null) PlaceActor(0f);
    }

    private void PlaceActor(float lift)
    {
        actor.position = ground + Vector3.up * (0.02f + lift);
    }

    // The highest walkable surface just about a point, ignoring the door, the one coming out
    // and the visitor.
    private Vector3 GroundAt(Vector3 point, Transform visitor)
    {
        RaycastHit[] hits = Physics.RaycastAll(point + Vector3.up * 2f, Vector3.down, 6f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float highest = float.NegativeInfinity;
        foreach (RaycastHit hit in hits)
        {
            Transform t = hit.collider.transform;
            if (t.IsChildOf(door) || t.IsChildOf(actor) || (visitor != null && t.IsChildOf(visitor)) ||
                hit.normal.y < 0.5f) continue;
            highest = Mathf.Max(highest, hit.point.y);
        }
        if (!float.IsNegativeInfinity(highest)) point.y = highest;
        return point;
    }

    // Room for the visitor to stand at a point: nothing but themselves (and the one coming out) there.
    private bool IsClear(Vector3 feet, Transform visitor)
    {
        foreach (Collider collider in Physics.OverlapCapsule(feet + Vector3.up * 0.58f, feet + Vector3.up * 1.35f, 0.42f,
                     Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (!collider.transform.IsChildOf(visitor) && !collider.transform.IsChildOf(actor)) return false;
        }
        return true;
    }

    private static void Discard(UnityEngine.Object item)
    {
        if (item == null) return;
        if (Application.isPlaying) UnityEngine.Object.Destroy(item);
        else UnityEngine.Object.DestroyImmediate(item);
    }
}
