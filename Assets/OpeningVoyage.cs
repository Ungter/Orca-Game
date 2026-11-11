using System;
using UnityEngine;

// The game's opening: the player rides the supplied sailboat in from near the
// edge of the sea to the island's dock (IslandDemo:Dock), along the x axis the dock runs out to
// sea on, slowing as it comes up to the dock's end. There SPACE jumps the player across onto the
// dock, which ends the opening; the boat stays moored where it stopped. TitleScreen owns the
// shot, the timing and the input lock; this places the boat, the player on it and the camera.
public sealed class OpeningVoyage : IDisposable
{
    public enum Phase { Drifting, Waiting, Jumping, Landed }

    public const string DockName = "IslandDemo:Dock";
    public const string OceanName = "ocean";
    public const string GroundName = "IslandDemo:pCube1";
    public const float BoatLength = 7f;

    // The crossing starts this far inside the sea's edge, at least MinDistance and at most
    // MaxDistance out from the dock, and takes CruiseSpeed's time within MinCrossing-MaxCrossing.
    private const float EdgeMargin = 20f;
    private const float MinDistance = 60f;
    private const float MaxDistance = 400f;
    private const float CruiseSpeed = 9f;
    private const float MinCrossing = 10f;
    private const float MaxCrossing = 20f;
    // The hull's reference size, how high its deck rides above the water and
    // the gap left between its bow and the dock's end.
    private const float Freeboard = 0.35f;
    private const float BowGap = 0.5f;
    // The jump lands this far along the dock from its end, RightShift (or more, for a clear follow
    // view) to the right of the walkway's middle and at least WalkwayMargin in from its edge.
    private const float LandingInset = 1.6f;
    private const float RightShift = 1f;
    private const float WalkwayMargin = 0.6f;
    private const float WalkwayStep = 0.1f;
    private const float JumpDuration = 0.75f;
    private const float JumpHeight = 1.4f;
    private const float PromptFadeSeconds = 0.4f;
    // The camera rides this far behind the player, this far to their right and this high above
    // their feet, looking at a point this far ahead.
    private const float CameraBehind = 5.5f;
    private const float CameraRight = 1.4f;
    private const float CameraHeight = 2.6f;
    private const float CameraLookAhead = 8f;
    private const string BoatResource = "WorldModels/StartingBoat";

    private readonly PlayerController player;
    private readonly CharacterController controller;
    private readonly bool controllerWasEnabled;
    private readonly GameObject boat;
    private readonly Collider hull;
    private readonly BoatFit fit;
    private readonly Quaternion heading;
    // Out from the dock to sea; the boat travels the other way.
    private readonly Vector3 outward;
    // The boat's centre at the waterline, where the crossing starts and where it moors.
    private readonly Vector3 start;
    private readonly Vector3 moored;
    private readonly float duration;
    private Vector3 deck;
    // Where the boat was last put, on the waterline.
    private Vector3 placedAt;
    // The player's feet as the camera follows them: on the deck, then along the jump without
    // its arc, so the shot doesn't bounce.
    private Vector3 anchor;
    private Vector3 jumpFrom;
    private float time;
    private float waitingSince;
    private float jumpTime;
    private bool released;

    public Phase CurrentPhase { get; private set; }
    public bool IsWaitingForJump => CurrentPhase == Phase.Waiting;
    public bool HasLanded => CurrentPhase == Phase.Landed;
    public float CrossingDuration => duration;
    public GameObject Boat => boat;
    public Vector3 StartPoint => start;
    public Vector3 MooredPoint => moored;
    // Where the player stands on the dock after the jump.
    public Vector3 Landing { get; }
    // A point up the dock towards the island, for the follow camera to face after landing.
    public Vector3 Inland => Landing - outward * 10f;
    public float PromptAlpha => IsWaitingForJump ? Mathf.Clamp01((time - waitingSince) / PromptFadeSeconds) : 0f;
    // Over the player's head on the boat, for the jump prompt.
    public Vector3 JumpPromptAnchor => deck + Vector3.up * 2.1f;

    private OpeningVoyage(PlayerController player, Vector3 outward, Vector3 landing, float seaLevel, Bounds? sea,
        GameObject boatPrefab)
    {
        this.player = player;
        this.outward = outward;
        Landing = landing;
        boat = UnityEngine.Object.Instantiate(boatPrefab);
        boat.name = "Starting boat";
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(boat, player.gameObject.scene);
        fit = BoatFit.Fit(boat, player);
        hull = boat.GetComponent<Collider>();
        // In line with the walkway, its bow just short of the dock's end.
        Vector3 tip = landing + outward * LandingInset;
        moored = new Vector3(tip.x, seaLevel, tip.z) + outward * (BowGap + fit.Length * 0.5f);
        float distance = sea.HasValue ? DistanceToEdge(moored, outward, sea.Value) - EdgeMargin : 160f;
        distance = Mathf.Clamp(distance, MinDistance, MaxDistance);
        start = moored + outward * distance;
        duration = Mathf.Clamp(distance / CruiseSpeed, MinCrossing, MaxCrossing);
        heading = Quaternion.LookRotation(-outward, Vector3.up);

        // Scripted while the opening runs; solid once it is moored.
        hull.enabled = false;

        controller = player.GetComponent<CharacterController>();
        controllerWasEnabled = controller.enabled;
        controller.enabled = false;
        PlaceBoat(start, true);
        player.PlaceOnGround(deck);
        anchor = deck;
    }

    // Finds the dock, the sea and the island in the map; false (touching nothing) without a dock.
    public static bool TryCreate(Transform map, PlayerController player, out OpeningVoyage voyage)
    {
        voyage = null;
        Transform dockObject = map != null ? FindChild(map, DockName) : null;
        if (dockObject == null || player == null || player.GetComponent<CharacterController>() == null) return false;
        if (!TryGetBounds(dockObject, out Bounds dock)) return false;
        GameObject boatPrefab = Resources.Load<GameObject>(BoatResource);
        if (boatPrefab == null || boatPrefab.GetComponent<Collider>() == null) return false;

        // The dock runs out to sea along x (the pier at the bottom of the island seen from above
        // in the editor): the boat comes in along x from the side away from the island's middle.
        Transform groundObject = FindChild(map, GroundName);
        Vector3 island = groundObject != null && TryGetBounds(groundObject, out Bounds ground) ? ground.center : map.position;
        Vector3 outward = dock.center.x >= island.x ? Vector3.right : Vector3.left;

        Transform oceanObject = FindChild(map, OceanName);
        Bounds? sea = oceanObject != null && TryGetBounds(oceanObject, out Bounds water) ? water : (Bounds?)null;
        float seaLevel = sea.HasValue ? sea.Value.max.y : dock.min.y;

        float landingX = dock.center.x + outward.x * (dock.extents.x - LandingInset);
        Camera view = Camera.main;
        CameraFollow follow = view != null ? view.GetComponent<CameraFollow>() : null;
        if (follow == null) follow = UnityEngine.Object.FindAnyObjectByType<CameraFollow>();
        Vector3 landing = PickLanding(landingX, outward, dock, dockObject, player, follow);
        voyage = new OpeningVoyage(player, outward, landing, seaLevel, sea, boatPrefab);
        return true;
    }

    // Moves the crossing on by deltaTime. jump: SPACE this frame; only taken once the boat is in.
    public void Tick(float deltaTime, bool jump)
    {
        if (released || player == null) return;
        time += deltaTime;
        switch (CurrentPhase)
        {
            case Phase.Drifting:
                float progress = Mathf.Clamp01(time / duration);
                // Easing out: the boat slows as it comes up to the dock.
                PlaceBoat(Vector3.Lerp(start, moored, 1f - (1f - progress) * (1f - progress)), false);
                player.PlaceOnGround(deck);
                anchor = deck;
                if (progress >= 1f)
                {
                    CurrentPhase = Phase.Waiting;
                    waitingSince = time;
                }
                break;
            case Phase.Waiting:
                PlaceBoat(moored, false);
                player.PlaceOnGround(deck);
                anchor = deck;
                if (jump)
                {
                    CurrentPhase = Phase.Jumping;
                    jumpTime = 0f;
                    jumpFrom = deck;
                }
                break;
            case Phase.Jumping:
                PlaceBoat(moored, false);
                jumpTime += deltaTime;
                float s = Mathf.Clamp01(jumpTime / JumpDuration);
                anchor = Vector3.Lerp(jumpFrom, Landing, s);
                player.PlaceOnGround(anchor + Vector3.up * (JumpHeight * 4f * s * (1f - s)));
                if (s >= 1f) CurrentPhase = Phase.Landed;
                break;
            default:
                PlaceBoat(moored, false);
                anchor = Landing;
                break;
        }
    }

    // Right behind the player and a little to their right, looking past them the way the boat
    // goes, all the way in and through the jump onto the dock.
    public void GetCameraPose(out Vector3 position, out Quaternion rotation)
    {
        Vector3 forward = -outward;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        position = anchor - forward * Mathf.Max(CameraBehind, fit.Length * 0.45f) +
            right * CameraRight + Vector3.up * CameraHeight;
        Vector3 focus = anchor + forward * CameraLookAhead + Vector3.up * 1.2f;
        rotation = Quaternion.LookRotation(focus - position, Vector3.up);
    }

    // Run after the shot's camera moves, so the billboard and walk direction match its view.
    // The player stands with their back to the camera, looking where the boat goes, and walks
    // away from it through the jump.
    public void AnimatePlayer(float deltaTime)
    {
        if (player == null) return;
        if (!released && CurrentPhase == Phase.Jumping)
        {
            player.AnimateCinematicWalk(-outward, deltaTime);
            return;
        }
        player.FaceAway();
        player.AnimateCinematicWalk(Vector3.zero, deltaTime);
    }

    // Ends the opening wherever it got to: the boat moors, solid, and the player is put on the
    // dock with their controller as it was.
    public void Dispose()
    {
        if (released) return;
        released = true;
        if (boat != null)
        {
            PlaceBoat(moored, true);
            hull.enabled = true;
        }
        if (player != null)
        {
            if (CurrentPhase != Phase.Landed) player.PlaceOnGround(Landing);
            player.AnimateCinematicWalk(Vector3.zero, 0f);
            controller.enabled = controllerWasEnabled;
            LighthouseFerry ferry = boat != null ? boat.GetComponent<LighthouseFerry>() : null;
            if (ferry != null) ferry.Configure(player, Landing);
        }
    }

    // Keeps the boat riding the waves where it is, with the player aboard, before the crossing
    // starts (while the title's camera flies down to it) or whenever the crossing isn't being
    // moved on, so the waves never roll through a boat left still.
    public void Float()
    {
        if (released || boat == null || player == null) return;
        PlaceBoat(placedAt, false);
        if (CurrentPhase == Phase.Drifting || CurrentPhase == Phase.Waiting)
        {
            player.PlaceOnGround(deck);
            anchor = deck;
        }
    }

    // The hull at centre (on the waterline), riding the waves (OceanWaves) unless still.
    private void PlaceBoat(Vector3 centre, bool still)
    {
        placedAt = centre;
        Vector3 rest = centre + Vector3.up * (Freeboard - fit.HullDepth * 0.5f);
        Pose pose = still ? new Pose(rest, heading) : OceanWaves.Ride(rest, heading, fit.Length, fit.Beam);
        boat.transform.SetPositionAndRotation(pose.position, pose.rotation);
        // Stand aft of the mast, so the sail does not hide the player from the riding camera.
        deck = boat.transform.TransformPoint(fit.RidePoint);
    }

    // How far from a point along a flat direction to the edge of an area, in x and z.
    private static float DistanceToEdge(Vector3 from, Vector3 direction, Bounds area)
    {
        float distance = float.MaxValue;
        if (direction.x > 1e-4f) distance = Mathf.Min(distance, (area.max.x - from.x) / direction.x);
        else if (direction.x < -1e-4f) distance = Mathf.Min(distance, (area.min.x - from.x) / direction.x);
        if (direction.z > 1e-4f) distance = Mathf.Min(distance, (area.max.z - from.z) / direction.z);
        else if (direction.z < -1e-4f) distance = Mathf.Min(distance, (area.min.z - from.z) / direction.z);
        return distance == float.MaxValue ? 0f : Mathf.Max(0f, distance);
    }

    // Where the jump lands at x: a bit to the right of the middle of the dock's walkway as the
    // boat comes in, and further right while the follow camera would be blocked there (a post
    // on the dock pulls it in close for the first moments of play), keeping the player's whole
    // width on the walkway. The boat comes in lined up with it.
    private static Vector3 PickLanding(float x, Vector3 outward, Bounds dock, Transform dockObject,
        PlayerController player, CameraFollow follow)
    {
        if (!FindWalkway(x, dock, dockObject, out float zMin, out float zMax))
            return new Vector3(x, dock.max.y, dock.center.z);
        float right = Vector3.Cross(Vector3.up, -outward).z;
        float middle = (zMin + zMax) * 0.5f;
        float farthest = right > 0f ? zMax - WalkwayMargin : zMin + WalkwayMargin;
        CharacterController body = player.GetComponent<CharacterController>();
        float pivot = (body.height * 0.5f - body.center.y) * player.transform.lossyScale.y + 0.02f;
        if (!DeckAt(x, middle, dock, dockObject, out float middleY)) middleY = dock.max.y;
        Vector3 best = new Vector3(x, middleY, middle);
        for (float shift = RightShift; (middle + right * shift - farthest) * right <= 0f; shift += 0.2f)
        {
            float z = middle + right * shift;
            if (!DeckAt(x, z, dock, dockObject, out float y)) break;
            best = new Vector3(x, y, z);
            if (follow == null || follow.HasClearView(best + Vector3.up * pivot, best - outward * 10f)) break;
        }
        return best;
    }

    // The walkway across z at x: working out from the middle of the dock's bounds to the first
    // spot with an upward-facing surface of the dock's own (not a post or lamp on it), then on
    // either way to where that surface ends.
    private static bool FindWalkway(float x, Bounds dock, Transform dockObject, out float zMin, out float zMax)
    {
        zMin = zMax = dock.center.z;
        for (float offset = 0f; offset <= dock.extents.z; offset += WalkwayStep)
        {
            foreach (float z in new[] { dock.center.z + offset, dock.center.z - offset })
            {
                if (!DeckAt(x, z, dock, dockObject, out _)) continue;
                zMin = zMax = z;
                while (zMax + WalkwayStep <= dock.max.z && DeckAt(x, zMax + WalkwayStep, dock, dockObject, out _)) zMax += WalkwayStep;
                while (zMin - WalkwayStep >= dock.min.z && DeckAt(x, zMin - WalkwayStep, dock, dockObject, out _)) zMin -= WalkwayStep;
                return true;
            }
        }
        return false;
    }

    // The height of the dock's own upward-facing surface at x, z, if it has one there.
    private static bool DeckAt(float x, float z, Bounds dock, Transform dockObject, out float height)
    {
        RaycastHit[] hits = Physics.RaycastAll(new Vector3(x, dock.max.y + 2f, z), Vector3.down,
            dock.size.y + 4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        height = float.NegativeInfinity;
        foreach (RaycastHit hit in hits)
        {
            if (!hit.collider.transform.IsChildOf(dockObject) || hit.normal.y < 0.5f) continue;
            height = Mathf.Max(height, hit.point.y);
        }
        return !float.IsNegativeInfinity(height);
    }

    private static bool TryGetBounds(Transform root, out Bounds bounds)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
        bounds = default;
        if (renderers.Length == 0) return false;
        bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return true;
    }

    private static Transform FindChild(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform child in root)
        {
            Transform found = FindChild(child, name);
            if (found != null) return found;
        }
        return null;
    }
}
