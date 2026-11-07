using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// A soft white column of light rising from the foot of each door the player can knock on
// (alchDoor, magDoor, chudDoor), brightest at the ground and fading out upwards, so the doors
// read as interactable from across the island. It thins out as the player walks up and is gone
// by the time the door's own [E] prompt shows. It also fades away while a conversation or
// cinematic is running, and Al Cachud's goes out once he has come out of his door (from then on
// E talks to him, not the door). Sized from the door, so it fits whatever scale the map is at.
[DisallowMultipleComponent]
public class DoorBeacon : MonoBehaviour
{
    public static readonly string[] DoorNames = { AlchemistEncounter.DoorName, MagicianEncounter.DoorName, CachudDoorway.DoorName };

    public const float WidthPerDoorWidth = 1.2f;
    public const float HeightPerDoorHeight = 4f;
    public const float FadeTime = 0.6f;
    // Ground distance from the player: full beam beyond FadeStartDistance, none within
    // FadeEndDistance (the doors' prompts show within 3 m).
    public const float FadeStartDistance = 14f;
    public const float FadeEndDistance = 4f;
    private const string ShaderPath = "DoorBeacon/DoorBeacon";

    [SerializeField] private Transform door;
    private Transform player;
    private MeshRenderer beam;
    private Material material;
    private Mesh mesh;
    private CachudCallout cachud;
    private bool lookedForCachud;

    public Transform Door => door;
    public float Width { get; private set; }
    public float Height { get; private set; }
    // Fades in and out with whether the beacon is wanted at all.
    public float Opacity { get; private set; }
    // 1 while the player is far away, down to 0 right by the door.
    public float DistanceFactor { get; private set; } = 1f;
    public float Brightness => Opacity * DistanceFactor;

    // Every load, including the pause menu's restart. Unsubscribing first keeps play mode
    // without domain reload to one handler.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Bootstrap();

    // Also runs after the first scene loads, in case sceneLoaded fired before Register.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        DoorBeacon[] existing = FindObjectsByType<DoorBeacon>(FindObjectsSortMode.None);
        foreach (string name in DoorNames)
        {
            GameObject door = GameObject.Find(name);
            if (door == null || System.Array.Exists(existing, b => b.door == door.transform)) continue;
            Install(door);
        }
    }

    // A beacon standing at the bottom centre of what the door renders.
    public static DoorBeacon Install(GameObject door)
    {
        var holder = new GameObject("Door Beacon (" + door.name + ")");
        SceneManager.MoveGameObjectToScene(holder, door.scene);
        DoorBeacon beacon = holder.AddComponent<DoorBeacon>();
        beacon.door = door.transform;
        beacon.Build();
        return beacon;
    }

    private void Build()
    {
        Bounds bounds = DoorBounds(door.gameObject);
        transform.position = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        // A door is a thin slab: its wider level side is its width, capped for odd shapes.
        float doorHeight = Mathf.Max(bounds.size.y, 0.01f);
        float doorWidth = Mathf.Min(Mathf.Max(bounds.size.x, bounds.size.z), doorHeight * 0.75f);
        Width = doorWidth * WidthPerDoorWidth;
        Height = doorHeight * HeightPerDoorHeight;

        mesh = BuildQuad(Width, Height);
        Shader shader = Resources.Load<Shader>(ShaderPath);
        if (shader == null) shader = Shader.Find("Orca/Door Beacon");
        if (shader == null)
        {
            Debug.LogWarning("[DoorBeacon] Missing Resources/" + ShaderPath + ".shader; " + door.name + " gets no beacon.");
            return;
        }
        material = new Material(shader) { name = "Door Beacon (" + door.name + ")" };
        // Out in front of the door's face, so the wall behind doesn't cut the beam.
        material.SetFloat("_TowardCamera", doorWidth * 0.75f);
        material.SetFloat("_Opacity", 0f);

        gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        beam = gameObject.AddComponent<MeshRenderer>();
        beam.sharedMaterial = material;
        beam.shadowCastingMode = ShadowCastingMode.Off;
        beam.receiveShadows = false;
        beam.lightProbeUsage = LightProbeUsage.Off;
        beam.reflectionProbeUsage = ReflectionProbeUsage.Off;
        beam.enabled = false;
    }

    private static Bounds DoorBounds(GameObject door)
    {
        Renderer[] renderers = door.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(door.transform.position + Vector3.up, new Vector3(1f, 2f, 1f));
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    // Base at the origin, x across, y up; the shader turns it to the camera. The bounds cover
    // every way it can turn, so it isn't culled while it faces sideways to its mesh.
    private static Mesh BuildQuad(float width, float height)
    {
        float half = width * 0.5f;
        var quad = new Mesh { name = "Door Beacon" };
        quad.vertices = new[]
        {
            new Vector3(-half, 0f, 0f), new Vector3(half, 0f, 0f),
            new Vector3(half, height, 0f), new Vector3(-half, height, 0f),
        };
        quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
        quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        quad.bounds = new Bounds(new Vector3(0f, height * 0.5f, 0f), new Vector3(width * 3f, height, width * 3f));
        quad.hideFlags = HideFlags.DontSave;
        return quad;
    }

    private void Update() => Tick(Time.unscaledDeltaTime);

    public void Tick(float deltaTime)
    {
        Opacity = Mathf.MoveTowards(Opacity, Wanted() ? 1f : 0f, deltaTime / FadeTime);
        DistanceFactor = DistanceFade();
        if (beam == null) beam = GetComponent<MeshRenderer>();
        if (beam == null) return;
        if (material == null) material = beam.sharedMaterial;
        float brightness = Brightness;
        beam.enabled = brightness > 0f;
        if (material != null) material.SetFloat("_Opacity", brightness);
    }

    // Shown while the door can be knocked on and nothing else holds the screen.
    private bool Wanted()
    {
        if (door == null || !door.gameObject.activeInHierarchy || Interactables.Busy) return false;
        if (door.name != CachudDoorway.DoorName) return true;
        if (!lookedForCachud)
        {
            cachud = FindAnyObjectByType<CachudCallout>();
            lookedForCachud = true;
        }
        return cachud == null || !cachud.HasComeOut;
    }

    // Eased from full at FadeStartDistance to nothing at FadeEndDistance; full without a player.
    private float DistanceFade()
    {
        if (player == null) player = Interactables.FindPlayer();
        if (player == null) return 1f;
        Vector3 offset = player.position - transform.position;
        offset.y = 0f;
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FadeEndDistance, FadeStartDistance, offset.magnitude));
    }

    private void OnDestroy()
    {
        Release(material);
        Release(mesh);
    }

    private static void Release(Object owned)
    {
        if (owned == null) return;
        if (Application.isPlaying) Destroy(owned);
        else DestroyImmediate(owned);
    }
}
