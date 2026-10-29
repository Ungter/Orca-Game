using UnityEngine;
using UnityEngine.InputSystem;

// A world object the player can walk up to and pick up by pressing E.
// While in range, a prompt is shown near the object. The prompt is drawn in
// screen space at the object's projected position, so it always faces the
// camera - the same visual effect the billboarded grass and tree sprites use.
public class PickupItem : MonoBehaviour, IInteractable
{
    [SerializeField]
    private string itemName = "Item";

    [SerializeField]
    private float pickupRadius = 2.5f;

    // Screen-space gap (reference pixels at 720p) between the top of the item's
    // sprite and the bottom of the prompt. Constant on screen, so the prompt
    // sits at the same spot just above the item at any camera distance.
    [SerializeField]
    private float hintOffsetY = 20f;

    // Key shown in brackets, e.g. "[E] PICK UP".
    [SerializeField]
    private string hintText = "E";

    [SerializeField]
    private string actionText = "Pick up";

    // Screen pixels per font pixel at 1280x720; rounded to a whole number at any resolution.
    [SerializeField]
    private int promptPixelSize = 3;

    // Optional faint point light that marks the item while it lies on the ground.
    // It lives on a child object, so it goes out with the item when picked up.
    [Header("Ground glow")]
    [SerializeField]
    private bool glow = false;

    [SerializeField]
    private Color glowColor = new Color(1f, 0.85f, 0.55f);

    [SerializeField]
    private float glowIntensity = 1.2f;

    [SerializeField]
    private float glowRange = 2.5f;

    [SerializeField]
    private float glowHeight = 0.4f;

    // Fraction of the intensity the glow breathes by; 0 holds it steady.
    [SerializeField]
    [Range(0f, 1f)]
    private float glowPulse = 0.25f;

    [SerializeField]
    private float glowPulseSpeed = 1.5f;

    // Gentle hover while the item lies on the ground. Bobs around the position
    // the item was placed at in the scene, so it never sinks below it by more than bobHeight.
    [Header("Ground bob")]
    [SerializeField]
    private bool bob = false;

    [SerializeField]
    private float bobHeight = 0.08f;

    // Full up-and-down cycles per second.
    [SerializeField]
    private float bobSpeed = 0.5f;

    private Light glowLight;
    private Vector3 restPosition;
    private float bobPhase;

    private Transform player;
    private bool pickedUp;

    // Height of the sprite's top above the pivot, measured once at rest so the
    // prompt anchor ignores the bob.
    private float anchorHeight;
    private float itemHeight;
    private float promptAlpha;

    // Buried items are hidden and cannot be picked up until Unearth raises them.
    private bool buried;
    private float riseElapsed = -1f;
    private float riseDuration;
    private Renderer[] renderers;

    public bool IsBuried => buried;

    private void Awake()
    {
        player = Interactables.FindPlayer();
        restPosition = transform.position;
        bobPhase = Random.value * Mathf.PI * 2f;
        Renderer itemRenderer = GetComponent<Renderer>();
        anchorHeight = itemRenderer != null ? itemRenderer.bounds.max.y - restPosition.y : 0.5f;
        itemHeight = itemRenderer != null ? itemRenderer.bounds.size.y : 0.5f;

        if (glow)
        {
            CreateGlow();
        }
    }

    private void CreateGlow()
    {
        var glowObject = new GameObject("Glow");
        glowObject.transform.SetParent(transform, false);
        glowObject.transform.position = transform.position + Vector3.up * glowHeight;
        glowLight = glowObject.AddComponent<Light>();
        glowLight.type = LightType.Point;
        glowLight.color = glowColor;
        glowLight.intensity = glowIntensity;
        glowLight.range = glowRange;
        glowLight.shadows = LightShadows.None;
    }

    private void OnEnable()
    {
        Interactables.Add(this);
    }

    private void OnDisable()
    {
        Interactables.Remove(this);
    }

    // Hides the item underground: nothing drawn, no glow, no prompt.
    public void Bury()
    {
        if (pickedUp)
        {
            return;
        }
        buried = true;
        riseElapsed = -1f;
        SetVisible(false);
    }

    // Raises a buried item out of the ground at its scene position; it can be picked up once it is out.
    public void Unearth(float duration)
    {
        if (!buried || riseElapsed >= 0f)
        {
            return;
        }
        riseDuration = Mathf.Max(0.01f, duration);
        riseElapsed = 0f;
        transform.position = restPosition - Vector3.up * RiseDepth();
        SetVisible(true);
        if (glowLight != null)
        {
            glowLight.intensity = 0f;
        }
    }

    // Deep enough that the whole sprite starts below its resting bottom edge.
    private float RiseDepth()
    {
        return itemHeight + 0.05f;
    }

    private void SetVisible(bool visible)
    {
        if (renderers == null)
        {
            renderers = GetComponentsInChildren<Renderer>(true);
        }
        foreach (Renderer itemRenderer in renderers)
        {
            if (itemRenderer != null)
            {
                itemRenderer.enabled = visible;
            }
        }
        if (glowLight != null)
        {
            glowLight.enabled = visible;
        }
    }

    private void UpdateRise()
    {
        if (riseElapsed < 0f)
        {
            return;
        }
        riseElapsed += Time.deltaTime;
        float t = Mathf.Clamp01(riseElapsed / riseDuration);
        float eased = 1f - (1f - t) * (1f - t) * (1f - t);
        // A slight shudder while it pushes through the dirt.
        float shake = (1f - t) * 0.03f * Mathf.Sin(riseElapsed * 60f);
        transform.position = restPosition - Vector3.up * (RiseDepth() * (1f - eased)) + transform.right * shake;
        if (glowLight != null)
        {
            glowLight.intensity = glowIntensity * eased;
        }
        if (t >= 1f)
        {
            buried = false;
            riseElapsed = -1f;
            transform.position = restPosition;
            // Start the bob at zero offset so it carries on smoothly from the rise.
            bobPhase = -Time.time * bobSpeed * Mathf.PI * 2f;
        }
    }

    private void Update()
    {
        if (buried)
        {
            UpdateRise();
            promptAlpha = 0f;
            return;
        }

        if (glowLight != null && glowPulse > 0f)
        {
            float wave = Mathf.Sin(Time.time * glowPulseSpeed * Mathf.PI * 2f) * 0.5f + 0.5f;
            glowLight.intensity = glowIntensity * (1f - glowPulse * wave);
        }

        if (bob && !pickedUp)
        {
            float offset = Mathf.Sin(Time.time * bobSpeed * Mathf.PI * 2f + bobPhase) * bobHeight;
            transform.position = restPosition + Vector3.up * offset;
        }

        bool blocked = InventoryInspection.IsOpen || PauseMenu.IsPaused || Interactables.Busy;
        bool showPrompt = !pickedUp && player != null && !blocked && Interactables.IsNearest(this);
        promptAlpha = Mathf.MoveTowards(promptAlpha, showPrompt ? 1f : 0f, Time.unscaledDeltaTime * 8f);

        if (pickedUp || player == null || blocked)
        {
            return;
        }

        // Only the nearest in-range item answers to E, so two overlapping
        // prompts never both consume the same press.
        if (!Interactables.IsNearest(this))
        {
            return;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.eKey.wasPressedThisFrame)
        {
            SpriteRenderer spriteRenderer = GetComponent<SpriteRenderer>();
            Sprite sprite = spriteRenderer != null ? spriteRenderer.sprite : null;
            bool added = InventorySystem.instance != null && InventorySystem.instance.TryAdd(itemName, sprite);
            if (added)
            {
                pickedUp = true;
                gameObject.SetActive(false);
            }
        }
    }

    private void OnGUI()
    {
        if (Event.current.type != EventType.Repaint || pickedUp)
        {
            return;
        }

        // Anchor on the top of the sprite at its rest position: the prompt sits
        // just above the item and does not follow the bob.
        Vector3 anchor = restPosition + Vector3.up * anchorHeight;
        InteractPrompt.Draw(anchor, hintOffsetY, hintText, actionText, promptPixelSize, promptAlpha);
    }

    public float InteractDistanceSqr()
    {
        if (pickedUp || buried || player == null || !isActiveAndEnabled)
        {
            return float.MaxValue;
        }
        Vector3 delta = player.position - transform.position;
        float distance = delta.x * delta.x + delta.z * delta.z;
        return distance <= pickupRadius * pickupRadius ? distance : float.MaxValue;
    }
}
