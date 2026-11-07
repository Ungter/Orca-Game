using UnityEngine;

// The player's lantern, in the bag from the start of the game (startWithLantern). Pressing its
// slot number (or clicking its slot) in the inventory toggles holding it: PlayerController
// switches to the lantern sprites and a warm, softly flickering point light hangs where the
// hand holds it. Losing the item puts the light out.
[DisallowMultipleComponent]
public class PlayerLantern : MonoBehaviour
{
    public const string ItemName = "Lantern";

    // The player's lantern, for the inventory to toggle.
    public static PlayerLantern Current { get; private set; }

    [SerializeField]
    private Color color = new Color(1f, 0.72f, 0.36f);

    [SerializeField]
    private float intensity = 4f;

    [SerializeField]
    private float range = 9f;

    // Share of the intensity a candle-like waver takes away at most.
    [SerializeField, Range(0f, 0.5f)]
    private float flicker = 0.15f;

    // Where the lantern hangs from the player's pivot, in metres: x out to the hand holding
    // it (mirrored by SetHand), y up, z towards the camera (away from it when facing away).
    [SerializeField]
    private Vector3 handOffset = new Vector3(0.5f, -0.26f, 0.15f);

    [Header("Starting with it")]
    // Put the lantern in the bag when the game starts, so nobody has to fetch it.
    [SerializeField]
    private bool startWithLantern = true;

    // Its picture in the bag.
    [SerializeField]
    private Sprite itemSprite;

    private Light glow;
    private float side = -1f;
    private bool behind;
    private float seed;

    public bool Equipped { get; private set; }
    public Light Glow => glow;

    public static bool Handles(string itemName) => itemName == ItemName;

    // Puts the lantern up or away. False when there is no lantern to hold.
    public bool Toggle() => SetEquipped(!Equipped);

    public bool SetEquipped(bool equipped)
    {
        if (equipped && !Owned()) return false;
        if (Equipped != equipped)
        {
            Equipped = equipped;
            Apply();
            if (InventorySystem.instance != null) InventorySystem.instance.NotifyInteraction();
        }
        return true;
    }

    // handSide: -1 when the lantern is on the left of the sprite as seen, +1 on the right.
    // facingAway: the player's back is to the camera, so the lantern hangs in front of them.
    public void SetHand(float handSide, bool facingAway)
    {
        side = handSide;
        behind = facingAway;
        Place();
    }

    private static bool Owned() => InventorySystem.instance != null && InventorySystem.instance.Contains(ItemName);

    private void Awake()
    {
        seed = Random.value * 100f;
    }

    // After every Awake, so the bag is ready; again on every reload, as the bag starts empty.
    private void Start()
    {
        GiveStartingLantern();
    }

    // Puts the lantern in the bag unless it is there already or starting with it is off.
    public bool GiveStartingLantern()
    {
        InventorySystem inventory = InventorySystem.instance;
        if (!startWithLantern || inventory == null || inventory.Contains(ItemName)) return false;
        return inventory.TryAdd(ItemName, itemSprite);
    }

    private void OnEnable()
    {
        Current = this;
        Apply();
    }

    private void OnDisable()
    {
        if (Current == this) Current = null;
        if (glow != null) glow.enabled = false;
    }

    private void OnDestroy()
    {
        if (glow == null) return;
        if (Application.isPlaying) Destroy(glow.gameObject);
        else DestroyImmediate(glow.gameObject);
    }

    private void Update()
    {
        if (Equipped && !Owned())
        {
            SetEquipped(false);
        }
        if (glow == null || !glow.enabled) return;
        float waver = Mathf.PerlinNoise(Time.time * 2.3f, seed) * 0.65f + Mathf.PerlinNoise(Time.time * 9.7f, seed + 3.1f) * 0.35f;
        glow.intensity = intensity * (1f - flicker * waver);
    }

    private void Apply()
    {
        if (!Equipped && glow == null) return;
        if (glow == null)
        {
            var lightObject = new GameObject("Lantern Light");
            lightObject.transform.SetParent(transform, false);
            glow = lightObject.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.shadows = LightShadows.None;
        }
        glow.color = color;
        glow.intensity = intensity;
        glow.range = Mathf.Max(0.1f, range);
        glow.enabled = Equipped && enabled && gameObject.activeInHierarchy;
        Place();
    }

    private void Place()
    {
        if (glow == null) return;
        glow.transform.localPosition = new Vector3(handOffset.x * side, handOffset.y, behind ? handOffset.z : -handOffset.z);
    }
}
