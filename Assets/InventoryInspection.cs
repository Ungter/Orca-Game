using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Runs before gameplay input so opening inspection cannot also move the player.
[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(InventorySystem))]
public class InventoryInspection : MonoBehaviour
{
    public static bool IsOpen { get; private set; }

    private const float HoldDuration = 0.35f;
    private const float RotationSpeed = 90f;
    private const string InspectControls = "W A S D  Rotate     /     ESC  Close";
    private static readonly Key[] SlotKeys = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5 };

    private InventorySystem inventory;
    private int heldSlot = -1;
    private int inspectedSlot = -1;
    private float holdStartedAt;
    private bool waitForRelease;
    private Vector2 rotation;
    private Canvas canvas;
    private Image itemImage;
    private Text title;
    private Text controls;
    private ClueMinigame minigame;
    private bool puzzleOpen;

    private void Awake()
    {
        inventory = GetComponent<InventorySystem>();
        minigame = GetComponent<ClueMinigame>();
        if (minigame == null)
        {
            // Scenes saved before the clue puzzle existed do not carry it yet.
            minigame = gameObject.AddComponent<ClueMinigame>();
        }
    }

    private void OnDisable()
    {
        Close();
        heldSlot = -1;
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            heldSlot = -1;
            return;
        }

        if (keyboard.escapeKey.wasPressedThisFrame)
        {
            Close();
            heldSlot = -1;
            // Esc must not immediately reopen an item whose slot key is still held.
            waitForRelease = true;
            return;
        }

        int slot = -1;
        for (int i = 0; i < Mathf.Min(SlotKeys.Length, inventory.SlotCount()); i++)
        {
            if (keyboard[SlotKeys[i]].isPressed)
            {
                slot = i;
                break;
            }
        }

        if (slot < 0)
        {
            heldSlot = -1;
            waitForRelease = false;
        }
        else if (!waitForRelease)
        {
            inventory.NotifyInteraction();
            if (slot != heldSlot)
            {
                heldSlot = slot;
                holdStartedAt = Time.unscaledTime;
            }
            if (Time.unscaledTime - holdStartedAt >= HoldDuration && inspectedSlot != slot)
            {
                Open(slot);
            }
        }

        // The clue puzzle reads WASD itself instead of rotating the letter.
        if (!IsOpen || puzzleOpen)
        {
            return;
        }

        Vector2 input = Vector2.zero;
        if (keyboard.wKey.isPressed) input.x -= 1f;
        if (keyboard.sKey.isPressed) input.x += 1f;
        if (keyboard.aKey.isPressed) input.y -= 1f;
        if (keyboard.dKey.isPressed) input.y += 1f;
        rotation += input * (RotationSpeed * Time.unscaledDeltaTime);
        rotation.x = Mathf.Repeat(rotation.x, 360f);
        rotation.y = Mathf.Repeat(rotation.y, 360f);
        // Rotate the flat sprite in 3D about its center, never translate it.
        itemImage.rectTransform.localRotation = Quaternion.Euler(rotation.x, rotation.y, 0f);
    }

    private void LateUpdate()
    {
        if (!IsOpen || puzzleOpen)
        {
            return;
        }

        Rect bounds = ((RectTransform)canvas.transform).rect;
        float aspect = itemImage.sprite.rect.width / itemImage.sprite.rect.height;
        float height = Mathf.Min(bounds.height * 0.76f, bounds.width * 0.84f / aspect);
        itemImage.rectTransform.sizeDelta = new Vector2(height * aspect, height);
    }

    private void Open(int slot)
    {
        string itemName = inventory.GetItem(slot);
        Sprite sprite = inventory.GetSprite(slot);
        bool puzzle = minigame.Handles(itemName);
        if (sprite == null && !puzzle)
        {
            return; // Empty slots and items without artwork have nothing to inspect.
        }

        if (canvas == null)
        {
            BuildView();
        }
        // Inspecting the clue letter opens the puzzle written on it.
        puzzleOpen = puzzle;
        itemImage.gameObject.SetActive(!puzzle);
        if (puzzle)
        {
            minigame.Show((RectTransform)canvas.transform, sprite);
        }
        else
        {
            minigame.Hide();
            itemImage.sprite = sprite;
            itemImage.rectTransform.localRotation = Quaternion.identity;
            rotation = Vector2.zero;
        }
        controls.text = puzzle ? ClueMinigame.Controls : InspectControls;
        title.text = itemName;
        inspectedSlot = slot;
        IsOpen = true;
        canvas.gameObject.SetActive(true);
    }

    private void Close()
    {
        if (!IsOpen)
        {
            return;
        }
        IsOpen = false;
        inspectedSlot = -1;
        puzzleOpen = false;
        minigame.Hide();
        canvas.gameObject.SetActive(false);
        inventory.NotifyInteraction();
    }

    private void BuildView()
    {
        var root = new GameObject("Inventory inspection", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        root.transform.SetParent(transform, false);
        canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

        var backdrop = new GameObject("Dim background", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        backdrop.transform.SetParent(root.transform, false);
        backdrop.color = new Color(0f, 0f, 0f, 0.78f);
        backdrop.raycastTarget = false;
        backdrop.rectTransform.anchorMin = Vector2.zero;
        backdrop.rectTransform.anchorMax = Vector2.one;
        backdrop.rectTransform.offsetMin = Vector2.zero;
        backdrop.rectTransform.offsetMax = Vector2.zero;

        itemImage = new GameObject("Inspected sprite", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        itemImage.transform.SetParent(root.transform, false);
        itemImage.raycastTarget = false;
        itemImage.preserveAspect = true;
        itemImage.rectTransform.anchorMin = itemImage.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        itemImage.rectTransform.anchoredPosition3D = Vector3.zero;

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        title = CreateLabel("Item name", font, 22, 0.94f);
        controls = CreateLabel("Controls", font, 14, 0.06f);
        controls.color = new Color(1f, 1f, 1f, 0.7f);
    }

    private Text CreateLabel(string name, Font font, int size, float anchorY)
    {
        var label = new GameObject(name, typeof(RectTransform), typeof(Text)).GetComponent<Text>();
        label.transform.SetParent(canvas.transform, false);
        label.font = font;
        label.fontSize = size;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        label.raycastTarget = false;
        label.rectTransform.anchorMin = new Vector2(0.1f, anchorY);
        label.rectTransform.anchorMax = new Vector2(0.9f, anchorY);
        label.rectTransform.sizeDelta = new Vector2(0f, 36f);
        label.rectTransform.anchoredPosition = Vector2.zero;
        return label;
    }
}
