using UnityEngine;
using UnityEngine.InputSystem;

// The opening boat becomes a two-way ferry once the player has jumped onto the main dock.
[DefaultExecutionOrder(-50)]
public sealed class LighthouseFerry : MonoBehaviour, IInteractable
{
    public enum Phase { Moored, Boarding, Sailing, Landing }
    private static LighthouseFerry active;
    public static bool IsSailing => active != null;
    public Phase CurrentPhase { get; private set; }
    public bool AtLighthouse { get; private set; }
    public bool IsReady => lighthouse != null && player != null;
    public Vector3 MainLanding { get; private set; }
    public Vector3 IslandLanding => lighthouse.PierLanding;
    public Vector3[] Route { get; private set; }
    public float CrossingDuration { get; private set; }
    public BoatFit Fit { get; private set; }
    private LighthouseStacker lighthouse;
    private PlayerController player;
    private CharacterController controller;
    private BoxCollider hull;
    private Camera cameraView;
    private CameraFollow follow;
    private bool playerWasEnabled;
    private bool controllerWasEnabled;
    private bool followWasEnabled;
    private bool hullWasEnabled;
    private Vector3 mainBoatPosition;
    private Quaternion mainBoatRotation;
    private Vector3 islandBoatPosition;
    private Vector3 departureBoatPosition;
    private Quaternion departureBoatRotation;
    private Vector3 cameraPosition;
    private Quaternion cameraRotation;
    private Vector3 departureLanding;
    private Vector3 destinationLanding;
    private Vector3 boardFrom;
    private Vector3 jumpFrom;
    private bool destinationIsLighthouse;
    private float phaseTime;
    private float routeLength;
    private float prompt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => active = null;
    private void OnEnable() => Interactables.Add(this);
    private void OnDisable() { Interactables.Remove(this); Cancel(); }

    public void Configure(PlayerController visitor, Vector3 mainLanding)
    {
        if (IsReady || visitor == null) return;
        foreach (GameObject root in gameObject.scene.GetRootGameObjects())
            if (root.TryGetComponent(out LighthouseStacker found)) { lighthouse = found; break; }
        if (lighthouse == null) return;
        player = visitor;
        MainLanding = mainLanding;
        hull = GetComponent<BoxCollider>();
        Fit = BoatFit.Fit(gameObject, player);
        mainBoatPosition = transform.position;
        mainBoatRotation = transform.rotation;
        islandBoatPosition = lighthouse.Berth + Vector3.left * (Fit.Length * 0.5f + 0.6f) +
            Vector3.up * (0.35f - Fit.HullDepth * 0.5f);
        Bounds land = default;
        Bounds sea = default;
        foreach (GameObject root in gameObject.scene.GetRootGameObjects())
        {
            if (root.name != "IslanddemoV3") continue;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>())
            {
                if (renderer.name == OpeningVoyage.GroundName) land = renderer.bounds;
                if (renderer.name == OpeningVoyage.OceanName) sea = renderer.bounds;
            }
        }
        Route = WaterRoute(mainBoatPosition, islandBoatPosition, land, sea, Fit.Length);
        routeLength = 0f;
        for (int i = 1; i < Route.Length; i++) routeLength += Vector3.Distance(Route[i - 1], Route[i]);
        CrossingDuration = Mathf.Clamp(routeLength / 30f, 6f, 16f);
    }

    // Take open water directly when possible; otherwise go around the southern shore.
    public static Vector3[] WaterRoute(Vector3 start, Vector3 end, Bounds land, Bounds ocean, float boatLength)
    {
        float margin = boatLength * 0.6f + 5f;
        Bounds keepOut = land;
        keepOut.Expand(new Vector3(margin * 2f, 1000f, margin * 2f));
        Vector3 direction = end - start;
        if (!keepOut.IntersectRay(new Ray(start, direction.normalized), out float hit) || hit > direction.magnitude)
            return new[] { start, end };
        float south = land.min.z - margin;
        if (ocean.size.z > 0f) south = Mathf.Max(south, ocean.min.z + margin);
        return new[] { start, new Vector3(start.x, start.y, south), new Vector3(end.x, end.y, south), end };
    }

    public float InteractDistanceSqr()
    {
        return IsReady && lighthouse.IsUnlocked ? DockDistanceSqr() : float.MaxValue;
    }

    private float DockDistanceSqr()
    {
        if (!IsReady || !isActiveAndEnabled) return float.MaxValue;
        Vector3 delta = player.transform.position - (AtLighthouse ? IslandLanding : MainLanding);
        float distance = delta.x * delta.x + delta.z * delta.z;
        return Mathf.Abs(delta.y) <= 3f && distance <= 3.5f * 3.5f ? distance : float.MaxValue;
    }

    private void Update()
    {
        if (GameplayDebugMenu.BlocksInput) return;
        if (PauseMenu.IsPaused) return;
        Keyboard keyboard = Keyboard.current;
        if (active == this)
        {
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) { Cancel(); return; }
            Tick(Time.deltaTime);
            return;
        }
        bool usable = IsReady && !TitleScreen.IsOpen && !InventoryInspection.IsOpen &&
            !Interactables.Busy && (lighthouse.IsUnlocked ? Interactables.IsNearest(this) : DockDistanceSqr() != float.MaxValue);
        prompt = Mathf.MoveTowards(prompt, usable ? 1f : 0f, Time.unscaledDeltaTime * 8f);
        if (usable && lighthouse.IsUnlocked && keyboard != null && keyboard.eKey.wasPressedThisFrame) Begin();
    }

    public bool Begin(Camera view = null)
    {
        if (!IsReady || !isActiveAndEnabled || InteractDistanceSqr() == float.MaxValue ||
            Interactables.Busy || InventoryInspection.IsOpen || TitleScreen.IsOpen || PauseMenu.IsPaused)
            return false;
        cameraView = view != null ? view : Camera.main;
        if (cameraView == null) return false;
        cameraPosition = cameraView.transform.position;
        cameraRotation = cameraView.transform.rotation;
        follow = cameraView.GetComponent<CameraFollow>();
        followWasEnabled = follow != null && follow.enabled;
        if (follow != null) follow.enabled = false;
        playerWasEnabled = player.enabled;
        controller = player.GetComponent<CharacterController>();
        controllerWasEnabled = controller.enabled;
        hullWasEnabled = hull.enabled;
        player.enabled = false;
        controller.enabled = false;
        hull.enabled = false;
        departureBoatPosition = transform.position;
        departureBoatRotation = transform.rotation;
        departureLanding = AtLighthouse ? IslandLanding : MainLanding;
        destinationLanding = AtLighthouse ? MainLanding : IslandLanding;
        destinationIsLighthouse = !AtLighthouse;
        boardFrom = Feet();
        active = this;
        CurrentPhase = Phase.Boarding;
        phaseTime = 0f;
        prompt = 0f;
        return true;
    }

    public void Tick(float deltaTime)
    {
        if (active != this || PauseMenu.IsPaused) return;
        if (player == null || lighthouse == null) { Cancel(); return; }
        phaseTime += Mathf.Max(0f, deltaTime);
        if (CurrentPhase == Phase.Boarding)
        {
            float t = Mathf.Clamp01(phaseTime / 0.7f);
            Vector3 seat = transform.TransformPoint(Fit.RidePoint);
            player.PlaceOnGround(Vector3.Lerp(boardFrom, seat, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * 1.5f));
            if (t >= 1f) { CurrentPhase = Phase.Sailing; phaseTime = 0f; }
        }
        else if (CurrentPhase == Phase.Sailing)
        {
            float t = Mathf.Clamp01(phaseTime / CrossingDuration);
            float eased = t * t * (3f - 2f * t);
            float progress = destinationIsLighthouse ? eased : 1f - eased;
            Vector3 position = PointOnRoute(progress);
            float ahead = destinationIsLighthouse ? Mathf.Min(1f, progress + 0.01f) : Mathf.Max(0f, progress - 0.01f);
            Vector3 direction = PointOnRoute(ahead) - position;
            Quaternion rotation = direction.sqrMagnitude > 0.001f ? Quaternion.LookRotation(direction, Vector3.up) :
                destinationIsLighthouse ? Quaternion.LookRotation(Vector3.right) : mainBoatRotation;
            transform.SetPositionAndRotation(position, Quaternion.Slerp(transform.rotation, rotation, Mathf.Min(1f, deltaTime * 5f)));
            player.PlaceOnGround(transform.TransformPoint(Fit.RidePoint));
            if (t >= 1f)
            {
                transform.SetPositionAndRotation(destinationIsLighthouse ? islandBoatPosition : mainBoatPosition,
                    destinationIsLighthouse ? Quaternion.LookRotation(Vector3.right) : mainBoatRotation);
                jumpFrom = transform.TransformPoint(Fit.RidePoint);
                CurrentPhase = Phase.Landing;
                phaseTime = 0f;
            }
        }
        else if (CurrentPhase == Phase.Landing)
        {
            float t = Mathf.Clamp01(phaseTime / 0.8f);
            player.PlaceOnGround(Vector3.Lerp(jumpFrom, destinationLanding, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * 1.5f));
            if (t >= 1f)
            {
                AtLighthouse = destinationIsLighthouse;
                Release(destinationLanding, false);
                return;
            }
        }
        if (cameraView != null)
        {
            Vector3 seat = Feet();
            Vector3 forward = transform.forward;
            Vector3 position = seat - forward * Mathf.Max(5.5f, Fit.Length * 0.45f) +
                transform.right * 1.4f + Vector3.up * 2.6f;
            cameraView.transform.SetPositionAndRotation(position, Quaternion.LookRotation(seat + forward * 8f + Vector3.up * 1.2f - position));
            player.AnimateCinematicWalk(Vector3.zero, deltaTime);
        }
    }

    private Vector3 Feet()
    {
        CharacterController body = player.GetComponent<CharacterController>();
        return player.transform.position + Vector3.up * ((body.center.y - body.height * 0.5f) * player.transform.lossyScale.y);
    }

    public Vector3 PointOnRoute(float progress)
    {
        float distance = Mathf.Clamp01(progress) * routeLength;
        for (int i = 1; i < Route.Length; i++)
        {
            float length = Vector3.Distance(Route[i - 1], Route[i]);
            if (distance <= length || i == Route.Length - 1)
                return Vector3.Lerp(Route[i - 1], Route[i], length > 0f ? distance / length : 1f);
            distance -= length;
        }
        return Route[0];
    }

    public void Cancel()
    {
        if (active != this) return;
        transform.SetPositionAndRotation(departureBoatPosition, departureBoatRotation);
        Release(departureLanding, true);
    }

    private void Release(Vector3 landing, bool cancelled)
    {
        if (player != null)
        {
            player.PlaceOnGround(landing);
            player.enabled = playerWasEnabled;
            if (controller != null) controller.enabled = controllerWasEnabled;
        }
        if (hull != null) hull.enabled = hullWasEnabled;
        if (cameraView != null && (cancelled || !followWasEnabled))
            cameraView.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
        if (follow != null)
        {
            follow.enabled = followWasEnabled;
            if (!cancelled && followWasEnabled)
            {
                Vector3 inland = landing + (AtLighthouse ? Vector3.right : mainBoatRotation * Vector3.forward) * 10f;
                follow.LookTowards(inland);
            }
        }
        CurrentPhase = Phase.Moored;
        active = null;
    }

    private void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;
        if (active != this)
        {
            if (IsReady) InteractPrompt.Draw((AtLighthouse ? IslandLanding : MainLanding) + Vector3.up * 2f,
                10f, lighthouse.IsUnlocked ? "E" : "LOCKED",
                !lighthouse.IsUnlocked ? "Solve anagram to unlock ferry" :
                AtLighthouse ? "Ferry to main island" : "Ferry to lighthouse", 3, prompt);
            return;
        }
        int p = Mathf.Max(1, Mathf.RoundToInt(3f * InteractPrompt.UIScale()));
        string text = destinationIsLighthouse ? "SAILING TO THE LIGHTHOUSE" : "RETURNING TO THE MAIN ISLAND";
        PixelFont.Draw(new Vector2((Screen.width - PixelFont.Measure(text) * p) * 0.5f, 24f),
            text, p, new Color32(242, 211, 107, 255), Color.black);
        const string cancel = "[ESC] CANCEL CROSSING";
        PixelFont.Draw(new Vector2((Screen.width - PixelFont.Measure(cancel) * p) * 0.5f, Screen.height - 48f),
            cancel, p, Color.white, Color.black);
    }
}
