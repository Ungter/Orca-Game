using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Experimental.Rendering;

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

    // Constant screen-space gap between the object's projected position and
    // the prompt. The gap does not change with distance, so the prompt stays
    // glued just above the object instead of drifting away as the camera moves.
    [SerializeField]
    private float hintOffsetY = 20f;

    [SerializeField]
    private string hintText = "E to pick up";

    private Transform player;
    private bool pickedUp;

    private Texture2D whiteTexture;
    private GUIStyle promptBorderStyle;
    private GUIStyle promptBackgroundStyle;
    private GUIStyle promptTextStyle;

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
        if (pickedUp || player == null)
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
            bool added = InventorySystem.instance != null && InventorySystem.instance.TryAdd(itemName);
            if (added)
            {
                pickedUp = true;
                gameObject.SetActive(false);
            }
        }
    }

    private void OnGUI()
    {
        if (pickedUp || player == null || Camera.main == null)
        {
            return;
        }

        if (this != NearestInRange())
        {
            return;
        }

        // Project the object's own position - not a point lifted above it. A
        // lifted anchor drifts relative to the object as the camera angle
        // changes; anchoring to the object itself keeps the prompt glued to
        // it, exactly like a little sign standing on the ground next to it.
        // IMGUI counts from the bottom-left corner, matching WorldToScreenPoint.
        Vector3 screenPos = Camera.main.WorldToScreenPoint(transform.position);
        if (screenPos.z <= 0f)
        {
            return; // Behind the camera.
        }

        BuildStyles();

        // Constant size and constant gap: the prompt moves on screen exactly as
        // the object does, never rising or falling relative to it.
        float width = 140f;
        float height = 24f;
        Rect promptRect = new Rect(screenPos.x - width * 0.5f, screenPos.y + hintOffsetY, width, height);
        GUI.Box(promptRect, "", promptBorderStyle);

        float inset = 2f;
        Rect innerRect = new Rect(promptRect.x + inset, promptRect.y + inset, promptRect.width - inset * 2f, promptRect.height - inset * 2f);
        GUI.Box(innerRect, "", promptBackgroundStyle);
        GUI.Label(innerRect, hintText, promptTextStyle);
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

    private void BuildStyles()
    {
        if (promptBorderStyle != null)
        {
            return;
        }

        whiteTexture = new Texture2D(1, 1, GraphicsFormat.R8G8B8A8_SRGB, TextureCreationFlags.None) { name = "pickup_white" };
        whiteTexture.SetPixel(0, 0, Color.white);
        whiteTexture.Apply();

        promptBorderStyle = new GUIStyle(GUI.skin.box);
        promptBorderStyle.normal.background = whiteTexture;
        promptBorderStyle.border = new RectOffset(0, 0, 0, 0);

        promptBackgroundStyle = new GUIStyle(GUI.skin.box);
        promptBackgroundStyle.border = new RectOffset(1, 1, 1, 1);

        promptTextStyle = new GUIStyle(GUI.skin.label);
        promptTextStyle.normal.textColor = Color.white;
        promptTextStyle.alignment = TextAnchor.MiddleCenter;
        promptTextStyle.fontSize = 16;
    }
}
