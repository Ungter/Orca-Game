using System.Collections.Generic;
using UnityEngine;

// The sea is no place to swim. Step or jump into it and the player sinks as the screen fades to
// black, then wakes on dry land a few steps back from where they went in: on the lighthouse
// island if they went in from there once the anagram has opened it (the ferry runs), otherwise on
// the main island.
//
// The sea's surface is the "ocean" plane's collider, which the player would otherwise walk on; the
// coast is wherever the ground (or a dock, a pier) is the first thing under the player's feet
// rather than that plane. While the player stands on land, where they stand is remembered every so
// often, so the way back is always somewhere they have just been. Nothing happens while the
// player isn't walking themselves (the ferry, the opening voyage, the lighthouse's game, debug
// fly): their character controller is off then.
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerController))]
public sealed class SeaRescue : MonoBehaviour
{
    public enum Phase { None, Sinking, Black, Waking }

    public const float SinkTime = 0.55f;
    public const float BlackTime = 0.3f;
    public const float WakeTime = 0.6f;
    private const float SinkDepth = 1.6f;
    // In the water once the feet are this close to the surface with the sea under them.
    private const float WaterMargin = 0.3f;
    // Below the sea by this much, wherever they are (off the edge of the sea plane), they're in.
    private const float FarBelow = 2f;
    private const float RememberEvery = 0.75f;
    private const int MaxRemembered = 60;
    // Woken at least this far (across) from where they went in, so not on the very edge.
    private const float BackFromWater = 2.5f;

    private static SeaRescue active;
    // A rescue is under way (the screen is dark or fading): nothing else should take input.
    public static bool IsRescuing => active != null && active.phase != Phase.None;

    public Phase CurrentPhase => phase;
    public float Fade => fade;
    // Where the last rescue put the player down, and where they went in.
    public Vector3 WokeAt { get; private set; }
    public Vector3 WentIn { get; private set; }
    // How many places on land are remembered to wake on.
    public int Remembered => mainFootings.Count + lighthouseFootings.Count;

    // Each island's own recent footings, so a long walk on one never forgets the other.
    private readonly List<Vector3> mainFootings = new List<Vector3>();
    private readonly List<Vector3> lighthouseFootings = new List<Vector3>();
    private bool lastOnLighthouse;
    private readonly RaycastHit[] hits = new RaycastHit[16];
    private PlayerController player;
    private CharacterController body;
    private Collider ocean;
    private float seaLevel;
    private Transform lighthouseIsland;
    private LighthouseStacker lighthouse;
    private Phase phase;
    private float phaseTime;
    private float fade;
    private Vector3 sinkFrom;
    private Vector3 wakeAt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => active = null;

    private void Awake()
    {
        player = GetComponent<PlayerController>();
        body = GetComponent<CharacterController>();
    }

    private void OnEnable() => active = this;

    private void OnDisable()
    {
        if (active == this) active = null;
    }

    private void Update()
    {
        if (GameplayDebugMenu.BlocksInput || PauseMenu.IsPaused) return;
        Tick(Time.deltaTime);
    }

    public void Tick(float deltaTime)
    {
        if (phase != Phase.None)
        {
            Rescue(Mathf.Max(0f, deltaTime));
            return;
        }
        if (player == null) Awake();
        if (!player.enabled || body == null || !body.enabled || player.DebugFlyEnabled || TitleScreen.IsOpen) return;
        if (!FindSea()) return;
        Vector3 feet = Feet();
        Collider under = Ground(feet);
        bool overSea = under == ocean;
        if (overSea && feet.y < seaLevel + WaterMargin || feet.y < seaLevel - FarBelow)
        {
            BeginRescue(feet);
            return;
        }
        if (under != null && !overSea && body.isGrounded) Remember(feet, under);
    }

    private bool FindSea()
    {
        if (ocean != null) return true;
        Renderer sea = OceanHorizon.FindOcean(gameObject.scene);
        if (sea == null || !sea.TryGetComponent(out ocean)) return false;
        seaLevel = sea.bounds.max.y;
        foreach (GameObject root in gameObject.scene.GetRootGameObjects())
        {
            if (root.name == "Lighthouse island") lighthouseIsland = root.transform;
            if (lighthouse == null) lighthouse = root.GetComponentInChildren<LighthouseStacker>(true);
        }
        return true;
    }

    private Vector3 Feet()
    {
        Vector3 centre = transform.TransformPoint(body.center);
        return centre - Vector3.up * (body.height * 0.5f * transform.lossyScale.y);
    }

    // The first solid thing under the feet (not the player, not a trigger).
    private Collider Ground(Vector3 feet)
    {
        int count = Physics.RaycastNonAlloc(feet + Vector3.up * 0.6f, Vector3.down, hits, 6f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        Collider nearest = null;
        float distance = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            if (hits[i].collider.transform.IsChildOf(transform) || hits[i].distance >= distance) continue;
            distance = hits[i].distance;
            nearest = hits[i].collider;
        }
        return nearest;
    }

    private void Remember(Vector3 feet, Collider under)
    {
        // Not the ferry's deck: it sails off, and waking where it was would be waking in the sea.
        if (under.GetComponentInParent<LighthouseFerry>() != null) return;
        lastOnLighthouse = lighthouseIsland != null && under.transform.IsChildOf(lighthouseIsland);
        List<Vector3> footings = lastOnLighthouse ? lighthouseFootings : mainFootings;
        if (footings.Count > 0)
        {
            Vector3 last = footings[footings.Count - 1];
            if (new Vector2(feet.x - last.x, feet.z - last.z).sqrMagnitude < RememberEvery * RememberEvery) return;
        }
        footings.Add(feet);
        if (footings.Count > MaxRemembered) footings.RemoveAt(0);
    }

    private void BeginRescue(Vector3 feet)
    {
        active = this;
        WentIn = feet;
        wakeAt = WhereToWake(feet);
        player.enabled = false;
        body.enabled = false;
        sinkFrom = transform.position;
        phase = Phase.Sinking;
        phaseTime = 0f;
    }

    // Somewhere they have just stood, a few steps back from the water: on the lighthouse island if
    // they went in from there and it is open to them, otherwise on the main island.
    public Vector3 WhereToWake(Vector3 wentIn)
    {
        bool unlocked = lighthouse != null && lighthouse.IsUnlocked;
        bool wantLighthouse = unlocked && lastOnLighthouse;
        List<Vector3> footings = wantLighthouse ? lighthouseFootings : mainFootings;
        for (int i = footings.Count - 1; i >= 0; i--)
        {
            Vector3 feet = footings[i];
            if (new Vector2(feet.x - wentIn.x, feet.z - wentIn.z).magnitude >= BackFromWater) return feet;
        }
        if (footings.Count > 0) return footings[footings.Count - 1];
        if (wantLighthouse && lighthouse != null) return lighthouse.Entrance;
        return MainIslandFallback();
    }

    // Never stood on the main island this visit (or only on the lighthouse island before it was
    // open): the top of the ground in its middle.
    private Vector3 MainIslandFallback()
    {
        GameObject ground = GameObject.Find(MouseTrapIsland.GroundName);
        if (ground != null && ground.TryGetComponent(out Collider land))
        {
            Bounds b = land.bounds;
            if (land.Raycast(new Ray(new Vector3(b.center.x, b.max.y + 10f, b.center.z), Vector3.down), out RaycastHit hit, b.size.y + 20f))
                return hit.point;
        }
        return transform.position;
    }

    private void Rescue(float deltaTime)
    {
        phaseTime += deltaTime;
        switch (phase)
        {
            case Phase.Sinking:
            {
                fade = Mathf.Clamp01(phaseTime / SinkTime);
                float t = fade * fade;
                transform.position = sinkFrom - Vector3.up * (SinkDepth * t);
                if (fade >= 1f)
                {
                    player.PlaceOnGround(wakeAt);
                    WokeAt = wakeAt;
                    // Facing inland, the water behind the camera.
                    CameraFollow follow = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
                    Vector3 inland = wakeAt - WentIn;
                    inland.y = 0f;
                    if (follow != null && inland.sqrMagnitude > 0.01f) follow.LookTowards(wakeAt + inland.normalized * 10f);
                    phase = Phase.Black;
                    phaseTime = 0f;
                }
                break;
            }
            case Phase.Black:
                fade = 1f;
                if (phaseTime >= BlackTime)
                {
                    phase = Phase.Waking;
                    phaseTime = 0f;
                }
                break;
            case Phase.Waking:
                fade = 1f - Mathf.Clamp01(phaseTime / WakeTime);
                if (fade <= 0f)
                {
                    body.enabled = true;
                    player.enabled = true;
                    phase = Phase.None;
                }
                break;
        }
    }

    private void OnGUI()
    {
        if (fade <= 0f || Event.current.type != EventType.Repaint) return;
        GUI.depth = -70;
        Color previous = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, fade);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = previous;
    }
}
