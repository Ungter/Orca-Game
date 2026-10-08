using UnityEngine;
using UnityEngine.InputSystem;

// A world object the player can walk up to and pick up by pressing E.
// While in range, a prompt is shown near the object. The prompt is drawn in
// screen space at the object's projected position, so it always faces the
// camera - the same visual effect the billboarded grass and tree sprites use.
public class PickupItem : MonoBehaviour
{
    // Registry of all active PickupItem instances, used to resolve which item
    // is nearest when several are within pickup range at the same time.
    private static readonly PickupItem[] instances = new PickupItem[32];
    private static int instanceCount = 0;

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

    private static readonly Color PromptKeyColor = new Color32(242, 211, 107, 255);
    private static readonly Color PromptTextColor = new Color32(233, 214, 168, 255);
    private static readonly Color PromptShadowColor = new Color32(26, 15, 10, 230);

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
    private float promptAlpha;

    private void Awake()
    {
        if (instanceCount < instances.Length)
        {
            instances[instanceCount] = this;
            instanceCount += 1;
        }

        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        if (playerObject == null)
        {
            playerObject = GameObject.Find("Player");
        }

        if (playerObject != null)
        {
            player = playerObject.transform;
        }

        restPosition = transform.position;
        bobPhase = Random.value * Mathf.PI * 2f;
        Renderer itemRenderer = GetComponent<Renderer>();
        anchorHeight = itemRenderer != null ? itemRenderer.bounds.max.y - restPosition.y : 0.5f;

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

    private void OnDestroy()
    {
        for (int i = 0; i < instanceCount; i++)
        {
            if (instances[i] == this)
            {
                instanceCount -= 1;
                instances[i] = instances[instanceCount];
                instances[instanceCount] = null;
                break;
            }
        }
    }

    private void Update()
    {
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

        bool showPrompt = !pickedUp && player != null && !InventoryInspection.IsOpen && this == NearestInRange();
        promptAlpha = Mathf.MoveTowards(promptAlpha, showPrompt ? 1f : 0f, Time.unscaledDeltaTime * 8f);

        if (pickedUp || player == null || InventoryInspection.IsOpen)
        {
            return;
        }

        // Only the nearest in-range item answers to E, so two overlapping
        // prompts never both consume the same press.
        if (this != NearestInRange())
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
        if (Event.current.type != EventType.Repaint || promptAlpha <= 0f || pickedUp || Camera.main == null)
        {
            return;
        }

        // Anchor on the top of the sprite at its rest position: the prompt sits
        // just above the item and does not follow the bob.
        Vector3 anchor = restPosition + Vector3.up * anchorHeight;
        Vector3 screenPos = Camera.main.WorldToScreenPoint(anchor);
        if (screenPos.z <= 0f)
        {
            return; // Behind the camera.
        }

        string key = "[" + hintText.ToUpperInvariant() + "]";
        string action = string.IsNullOrEmpty(actionText) ? "" : " " + actionText.ToUpperInvariant();
        int width = PixelFont.Measure(key + action);

        float uiScale = UIScale();
        int p = Mathf.Max(1, Mathf.RoundToInt(promptPixelSize * uiScale));
        // WorldToScreenPoint counts y from the bottom; IMGUI counts from the top.
        float bottom = Screen.height - screenPos.y - hintOffsetY * uiScale;
        var topLeft = new Vector2(screenPos.x - width * p * 0.5f, bottom - PixelFont.CapHeight * p);

        float a = promptAlpha * promptAlpha * (3f - 2f * promptAlpha);
        Color shadow = Fade(PromptShadowColor, a);
        PixelFont.Draw(topLeft, key, p, Fade(PromptKeyColor, a), shadow);
        if (action.Length > 0)
        {
            // Measure(key) excludes trailing spacing; add it back so the two runs join seamlessly.
            float offset = (PixelFont.Measure(key) + PixelFont.Spacing) * p;
            PixelFont.Draw(topLeft + new Vector2(offset, 0f), action, p, Fade(PromptTextColor, a), shadow);
        }
    }

    private static Color Fade(Color color, float alpha)
    {
        return new Color(color.r, color.g, color.b, color.a * alpha);
    }

    private bool IsInRange()
    {
        return DistanceSqr() <= pickupRadius * pickupRadius;
    }

    private float DistanceSqr()
    {
        Vector3 delta = player.position - transform.position;
        return delta.x * delta.x + delta.z * delta.z;
    }

    // Returns the unpicked item closest to the player that is within range.
    private static PickupItem NearestInRange()
    {
        PickupItem nearest = null;
        float nearestDistance = float.MaxValue;

        for (int i = 0; i < instanceCount; i++)
        {
            PickupItem candidate = instances[i];
            if (candidate == null || candidate.pickedUp || !candidate.isActiveAndEnabled)
            {
                continue;
            }

            if (!candidate.IsInRange())
            {
                continue;
            }

            float distance = candidate.DistanceSqr();
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = candidate;
            }
        }

        return nearest;
    }

    // Same reference resolution as the inventory HUD, so the prompt scales with it.
    private static float UIScale()
    {
        return Mathf.Max(0.5f, Mathf.Min(Screen.width / 1280f, Screen.height / 720f));
    }
}
