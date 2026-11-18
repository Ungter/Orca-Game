using UnityEngine;
using UnityEngine.SceneManagement;

// Orca's spirit in the world: the title screen's orca sprite, floating and bobbing a little above
// the ground with a soft blue glow, always facing the camera. It waits where it is put, leads the
// player to a destination (finding the way over the island's walkable survey, ClueMap, and waiting
// whenever the player falls behind), trails the player when there is nowhere to lead, or fades away.
[DisallowMultipleComponent]
public class OrcaSpirit : MonoBehaviour
{
    public enum Mode { Waiting, Following, Leading, Vanishing }

    public const string SpriteResource = "TitleScreen/Orca Sprite";
    private const float Length = 1.6f;
    private const float Hover = 0.75f;
    private const float FollowDistance = 2.2f;
    private const float TeleportDistance = 30f;
    private const float FrameTime = 0.5f;
    // Leading: as fast as the player walks (PlayerController.WalkSpeed); this without one.
    public const float LeadSpeed = 5f;
    // Further behind than this and Orca stops for the player; closer than ResumeDistance he goes on.
    public const float WaitDistance = 7f;
    public const float ResumeDistance = 4.5f;
    // This close to the destination he has arrived and hovers there.
    public const float ArriveRadius = 2.5f;
    // How many survey spots ahead along the way he aims for, which smooths the corners.
    private const int Lookahead = 2;

    private static Sprite[] frames;

    private Transform player;
    private PlayerController walker;
    private SpriteRenderer body;
    private Light glow;
    private Vector3 anchor;
    private Vector3 velocity;
    private float alpha;
    private float time;
    private bool facingRight;
    private Vector3 goal;
    private int[] field;
    private int fieldGoal = -1;
    private ClueMap fieldMap;
    private bool fieldAlongSidewalks;

    public Mode CurrentMode { get; private set; }
    public Vector3 Anchor => anchor;
    public Vector3 Goal => goal;
    // Leading, but stopped until the player catches up.
    public bool IsWaitingForPlayer { get; private set; }
    // Leading, and at the destination.
    public bool HasArrived { get; private set; }

    public static OrcaSpirit Create(Scene scene, Vector3 at, Transform player)
    {
        var spirit = new GameObject("Orca spirit");
        if (scene.IsValid()) SceneManager.MoveGameObjectToScene(spirit, scene);
        OrcaSpirit orca = spirit.AddComponent<OrcaSpirit>();
        orca.player = player;
        orca.WaitAt(at);
        return orca;
    }

    private void Awake()
    {
        body = gameObject.AddComponent<SpriteRenderer>();
        body.sprite = Frames()?[0];
        body.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        body.receiveShadows = false;
        Transform found = Interactables.FindPlayer();
        if (found != null && found.TryGetComponent(out SpriteRenderer playerSprite) && playerSprite.sharedMaterial != null)
            body.sharedMaterial = playerSprite.sharedMaterial;
        var light = new GameObject("Spirit glow");
        light.transform.SetParent(transform, false);
        light.transform.localPosition = new Vector3(0f, 0.6f, -0.3f);
        glow = light.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = new Color(0.55f, 0.82f, 1f);
        glow.range = 4f;
        glow.intensity = 0f;
        glow.shadows = LightShadows.None;
    }

    private static Sprite[] Frames()
    {
        if (frames != null && frames[0] != null) return frames;
        Texture2D texture = Resources.Load<Texture2D>(SpriteResource);
        if (texture == null) return null;
        texture.filterMode = FilterMode.Point;
        float half = texture.height * 0.5f;
        float ppu = texture.width / Length;
        frames = new[]
        {
            Sprite.Create(texture, new Rect(0f, half, texture.width, half), new Vector2(0.5f, 0.25f), ppu),
            Sprite.Create(texture, new Rect(0f, 0f, texture.width, half), new Vector2(0.5f, 0.25f), ppu),
        };
        foreach (Sprite frame in frames) frame.hideFlags = HideFlags.HideAndDontSave;
        return frames;
    }

    public void WaitAt(Vector3 at)
    {
        gameObject.SetActive(true);
        anchor = at;
        velocity = Vector3.zero;
        CurrentMode = Mode.Waiting;
        Place();
    }

    public void Follow()
    {
        gameObject.SetActive(true);
        CurrentMode = Mode.Following;
    }

    // Shows the player the way to destination; the player follows.
    public void Lead(Vector3 destination)
    {
        gameObject.SetActive(true);
        if (CurrentMode != Mode.Leading)
        {
            CurrentMode = Mode.Leading;
            IsWaitingForPlayer = false;
        }
        if ((destination - goal).sqrMagnitude > 0.01f) HasArrived = false;
        goal = destination;
    }

    public void Vanish() => CurrentMode = Mode.Vanishing;

    private void Update()
    {
        float dt = Time.deltaTime;
        time += dt;
        if (player == null) player = Interactables.FindPlayer();
        alpha = Mathf.MoveTowards(alpha, CurrentMode == Mode.Vanishing ? 0f : 1f, dt / 0.8f);
        if (CurrentMode == Mode.Vanishing && alpha <= 0f)
        {
            gameObject.SetActive(false);
            return;
        }

        if (CurrentMode == Mode.Leading && player != null) StepLead(dt);
        else if (CurrentMode == Mode.Following && player != null)
        {
            Vector3 away = anchor - player.position;
            away.y = 0f;
            if (away.magnitude > TeleportDistance)
            {
                Camera view = Camera.main;
                Vector3 behind = view != null ? -Vector3.ProjectOnPlane(view.transform.forward, Vector3.up).normalized : Vector3.back;
                anchor = player.position + behind * FollowDistance;
                velocity = Vector3.zero;
            }
            else
            {
                if (away.sqrMagnitude < 0.01f) away = Vector3.back;
                Vector3 target = player.position + away.normalized * FollowDistance;
                Vector3 before = anchor;
                anchor = Vector3.SmoothDamp(anchor, target, ref velocity, 0.35f, 12f, dt);
                Face(anchor - before);
            }
        }
        float ground = Place();
        // Leading over hills, the anchor rides the ground so the next ray still finds it.
        if (CurrentMode == Mode.Leading) anchor.y = ground;
    }

    // One step of leading the player towards the goal. Public so tests can drive it.
    public void StepLead(float deltaTime)
    {
        if (player == null) return;
        Vector3 toPlayer = Flat(player.position - anchor);
        if (toPlayer.magnitude > TeleportDistance)
        {
            // Lost the player (a ride, a rescue): back to their side, on the way to the goal.
            Vector3 way = Flat(goal - player.position);
            anchor = player.position + (way.sqrMagnitude > 0.01f ? way.normalized : Vector3.forward) * FollowDistance;
            IsWaitingForPlayer = false;
            return;
        }
        // Conversations and games hold him where he is.
        if (Interactables.Busy) return;

        float orcaToGoal = Flat(goal - anchor).magnitude;
        Vector3 next = Waypoint(out bool there);
        // There: close enough, or as close as walking gets (a door in a wall, a statue's plinth).
        if (orcaToGoal <= ArriveRadius || there)
        {
            HasArrived = true;
            IsWaitingForPlayer = false;
            Face(toPlayer);
            return;
        }
        HasArrived = false;
        float playerToGoal = Flat(goal - player.position).magnitude;
        bool behind = playerToGoal > orcaToGoal;
        bool ahead = playerToGoal < orcaToGoal - 1f;
        if (IsWaitingForPlayer) IsWaitingForPlayer = behind && toPlayer.magnitude > ResumeDistance;
        else IsWaitingForPlayer = behind && toPlayer.magnitude >= WaitDistance;
        if (IsWaitingForPlayer)
        {
            Face(toPlayer);
            return;
        }

        // The player's own pace, hurrying a little when they have gone on ahead.
        if (walker == null || walker.transform != player) walker = player.GetComponent<PlayerController>();
        float pace = walker != null ? walker.WalkSpeed : LeadSpeed;
        float speed = pace * (ahead ? 1.25f : 1f);
        Vector3 step = Flat(next - anchor);
        float move = speed * Mathf.Max(0f, deltaTime);
        Vector3 before = anchor;
        anchor += step.magnitude <= move ? step : step.normalized * move;
        Face(anchor - before);
    }

    // Where to head next: a couple of walkable spots further along the cheapest walk to the goal
    // over the island's survey, which keeps to the sidewalks (SidewalkRoute); the plain shortest walk
    // until the sidewalks have been found; straight at the goal with no survey (yet) or no way over it.
    private Vector3 Waypoint(out bool there)
    {
        there = false;
        ClueMap map = ClueMinigame.Current != null ? ClueMinigame.Current.Map : null;
        if (map == null || !map.IsReady) return goal;
        SidewalkRoute route = SidewalkRoute.For(map);
        int target = route.NearestOnIsland(goal, 25);
        if (target < 0) return goal;
        bool alongSidewalks = route.Advance(2500);
        if (fieldMap != map || fieldGoal != target || field == null || field.Length != map.SampleCount ||
            fieldAlongSidewalks != alongSidewalks)
        {
            field = new int[map.SampleCount];
            if (alongSidewalks) route.Distances(target, field);
            else map.StepDistances(target, field);
            fieldMap = map;
            fieldGoal = target;
            fieldAlongSidewalks = alongSidewalks;
        }
        int s = map.SampleAt(anchor);
        if (s < 0 || field[s] == ClueMap.Unreachable)
        {
            // Past the island's last spot on the way out to a goal beyond it: straight on.
            if (Flat(goal - anchor).magnitude < Flat(goal - map.SamplePosition(target)).magnitude) return goal;
            // Off the way (cutting a corner, or not yet on the island): back to the nearest spot
            // from which the goal can be walked to.
            int near = NearestReachable(map, anchor, 15);
            return near < 0 ? goal : map.SamplePosition(near);
        }
        // One step from the goal's spot: there, unless the goal lies well beyond the island's
        // walkable ground (the boat at the end of the dock), which he crosses straight to.
        if (field[s] <= (alongSidewalks ? SidewalkRoute.OffSidewalkCost : 1))
        {
            if (Flat(goal - map.SamplePosition(target)).magnitude > 6f) return goal;
            there = true;
            return anchor;
        }
        for (int i = 0; i < Lookahead; i++)
        {
            int best = s;
            for (int n = 0; n < 4; n++)
            {
                int t = map.Neighbour(s, n);
                if (t >= 0 && field[t] < field[best]) best = t;
            }
            if (best == s) break;
            s = best;
        }
        return field[s] == 0 ? goal : map.SamplePosition(s);
    }

    private int NearestReachable(ClueMap map, Vector3 at, int reach)
    {
        int sx = Mathf.FloorToInt((at.x - map.Origin.x) / map.SampleSize);
        int sz = Mathf.FloorToInt((at.z - map.Origin.y) / map.SampleSize);
        int best = -1;
        float bestDistance = float.MaxValue;
        for (int dz = -reach; dz <= reach; dz++)
        for (int dx = -reach; dx <= reach; dx++)
        {
            int x = sx + dx, z = sz + dz;
            if (x < 0 || z < 0 || x >= map.SamplesX || z >= map.SamplesZ) continue;
            int sample = z * map.SamplesX + x;
            if (field[sample] == ClueMap.Unreachable) continue;
            float distance = dx * dx + dz * dz;
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = sample;
        }
        return best;
    }

    // The sprite looks left; it turns while moving right across the screen.
    private void Face(Vector3 direction)
    {
        Camera view = Camera.main;
        if (view == null) return;
        float sideways = Vector3.Dot(direction, view.transform.right);
        if (Mathf.Abs(sideways) > 0.002f) facingRight = sideways > 0f;
    }

    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    // Floats over the ground under the anchor, bobbing. Returns the ground's height.
    private float Place()
    {
        // The highest surface no more than a metre over the anchor: the ground, not a roof or a tree.
        float ground = float.NegativeInfinity;
        foreach (RaycastHit hit in Physics.RaycastAll(anchor + Vector3.up * 3f, Vector3.down, 12f,
                     Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (player != null && hit.collider.transform.IsChildOf(player)) continue;
            if (hit.collider.transform.IsChildOf(transform)) continue;
            if (hit.point.y <= anchor.y + 1f && hit.point.y > ground) ground = hit.point.y;
        }
        if (float.IsNegativeInfinity(ground)) ground = anchor.y;
        transform.position = new Vector3(anchor.x, ground + Hover + Mathf.Sin(time * 2.2f) * 0.12f, anchor.z);
        return ground;
    }

    private void LateUpdate()
    {
        Camera view = Camera.main;
        if (view != null) transform.rotation = Quaternion.Euler(0f, view.transform.eulerAngles.y, 0f);
        if (body == null) return;
        Sprite[] sheet = Frames();
        if (sheet != null) body.sprite = sheet[Mathf.FloorToInt(time / FrameTime) % 2];
        // The sprite looks left; flipped while it drifts right across the screen.
        body.flipX = facingRight;
        body.color = new Color(0.9f, 0.97f, 1f, 0.88f * alpha);
        glow.intensity = 1.4f * alpha;
    }
}
