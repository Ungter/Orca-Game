using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Runs before gameplay input so opening inspection cannot also move the player.
[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(InventorySystem))]
public class InventoryInspection : MonoBehaviour
{
    public static bool IsOpen { get; private set; }

    private const float RotationSpeed = 90f;
    private const string InspectControls = "[WASD] ROTATE    [ESC] CLOSE";
    private static readonly Key[] SlotKeys = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5 };

    private InventorySystem inventory;
    private int inspectedSlot = -1;
    private bool waitForRelease;
    private Vector2 rotation;
    private Canvas canvas;
    private Image itemImage;
    private PixelText title;
    private PixelText controls;
    private ClueMinigame minigame;
    private ShellMinigame shellMinigame;
    private bool shellPuzzleOpen;

    private void Awake()
    {
        inventory = GetComponent<InventorySystem>();
        minigame = GetComponent<ClueMinigame>();
        if (minigame == null)
        {
            // Scenes saved before the clue puzzle existed do not carry it yet.
            minigame = gameObject.AddComponent<ClueMinigame>();
        }
        shellMinigame = GetComponent<ShellMinigame>();
        if (shellMinigame == null)
        {
            shellMinigame = gameObject.AddComponent<ShellMinigame>();
        }
    }

    private void OnDisable()
    {
        Close();
    }

    private void Update()
    {
        if (PauseMenu.IsPaused)
        {
            // Slot keys held through the pause must be pressed again to inspect.
            waitForRelease = true;
            return;
        }
        if (Interactables.Busy)
        {
            // No inspecting mid-conversation or mid-dig; keys held through it must be pressed again.
            waitForRelease = true;
            return;
        }
        if (IsOpen && shellPuzzleOpen && shellMinigame.ShouldClose)
        {
            Close();
            // A held inventory key must be released before the finished letter can reopen.
            waitForRelease = true;
            return;
        }
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (keyboard.escapeKey.wasPressedThisFrame)
        {
            Close();
            // Esc must not immediately reopen an item whose slot key is still held.
            waitForRelease = true;
            return;
        }

        // A slot key opens its item the moment it goes down; holding it does nothing more.
        int slot = -1;
        bool anyHeld = false;
        for (int i = 0; i < Mathf.Min(SlotKeys.Length, inventory.SlotCount()); i++)
        {
            var key = keyboard[SlotKeys[i]];
            anyHeld |= key.isPressed;
            if (slot < 0 && key.wasPressedThisFrame)
            {
                slot = i;
            }
        }

        if (!anyHeld)
        {
            waitForRelease = false;
        }
        else if (slot >= 0 && !waitForRelease)
        {
            inventory.NotifyInteraction();
            if (inspectedSlot != slot)
            {
                Open(slot);
            }
        }

        // The puzzle handles its own input instead of rotating the letter.
        if (!IsOpen || shellPuzzleOpen)
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
        if (IsOpen && shellPuzzleOpen)
        {
            // Update the controls as the puzzle changes phase.
            controls.text = shellMinigame.Controls;
        }
        if (!IsOpen || shellPuzzleOpen)
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
        if (PlayerLantern.Handles(itemName))
        {
            // The lantern's number holds it up or puts it away instead of inspecting it.
            Close();
            if (PlayerLantern.Current != null) PlayerLantern.Current.Toggle();
            return;
        }
        if (minigame.Handles(itemName))
        {
            // The clue letter's number holds its map up in the corner or puts it away; the
            // potion hunt is played on the island, not in this view.
            Close();
            minigame.ToggleHeld();
            return;
        }
        Sprite sprite = inventory.GetSprite(slot);
        bool shellPuzzle = shellMinigame.Handles(itemName);
        if (sprite == null && !shellPuzzle)
        {
            return; // Empty slots and items without artwork have nothing to inspect.
        }

        if (canvas == null)
        {
            BuildView();
        }
        // The magician's letter opens its puzzle in place of the inspected sprite.
        shellPuzzleOpen = shellPuzzle;
        itemImage.gameObject.SetActive(!shellPuzzle);
        if (shellPuzzle)
        {
            shellMinigame.Show((RectTransform)canvas.transform, sprite);
        }
        else
        {
            shellMinigame.Hide();
            itemImage.sprite = sprite;
            itemImage.rectTransform.localRotation = Quaternion.identity;
            rotation = Vector2.zero;
        }
        controls.text = shellPuzzle ? shellMinigame.Controls : InspectControls;
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
        shellPuzzleOpen = false;
        shellMinigame.Hide();
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

        title = CreateLabel("Item name", 15f, 0.94f);
        title.uppercase = true;
        title.color = new Color32(233, 214, 168, 255);
        controls = CreateLabel("Controls", 10f, 0.06f);
        controls.color = new Color32(200, 168, 114, 255);
        controls.highlightBrackets = true;
    }

private PixelText CreateLabel(string name, float capHeight, float anchorY)
    {
        var label = new GameObject(name, typeof(RectTransform), typeof(PixelText)).GetComponent<PixelText>();
        label.transform.SetParent(canvas.transform, false);
        label.fontSize = capHeight;
        label.alignment = TextAnchor.MiddleCenter;
        label.shadowColor = new Color32(26, 15, 10, 230);
        label.raycastTarget = false;
        label.rectTransform.anchorMin = new Vector2(0.1f, anchorY);
        label.rectTransform.anchorMax = new Vector2(0.9f, anchorY);
        label.rectTransform.sizeDelta = new Vector2(0f, 36f);
        label.rectTransform.anchoredPosition = Vector2.zero;
        return label;
    }
}
