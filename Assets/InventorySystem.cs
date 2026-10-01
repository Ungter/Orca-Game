using UnityEngine;

// Central inventory for the player: a fixed number of slots filled in pickup order.
// Gameplay code adds items through TryAdd; UI code reads slots with GetItem.
public class InventorySystem : MonoBehaviour
{
    public static InventorySystem instance;

    // Includes rejected pickups so a full inventory is still revealed.
    public event System.Action Interacted;

    [SerializeField]
    private int slotCount = 5;

    private string[] items;
    private Sprite[] itemSprites;
    private int itemCount;

    private void Awake()
    {
        instance = this;
        items = new string[slotCount];
        itemSprites = new Sprite[slotCount];
        itemCount = 0;
    }

    public int SlotCount()
    {
        return slotCount;
    }

    public int ItemCount()
    {
        return itemCount;
    }

    public bool IsFull()
    {
        return itemCount >= slotCount;
    }

    public void NotifyInteraction()
    {
        Interacted?.Invoke();
    }

    // Stores the item in the next free slot. Returns false when the inventory is full.
    public bool TryAdd(string itemName, Sprite sprite = null)
    {
        if (IsFull())
        {
            Debug.Log("Inventory is full - could not pick up " + itemName);
            NotifyInteraction();
            return false;
        }

        items[itemCount] = itemName;
        itemSprites[itemCount] = sprite;
        itemCount += 1;
        Debug.Log("Picked up " + itemName + " (" + itemCount + "/" + slotCount + ")");
        NotifyInteraction();
        return true;
    }

    public Sprite GetSprite(int slot)
    {
        return slot >= 0 && slot < itemCount ? itemSprites[slot] : null;
    }

    // Returns the item name in the given slot, or an empty string for empty slots.
    public string GetItem(int slot)
    {
        if (slot < 0 || slot >= itemCount)
        {
            return "";
        }

        return items[slot];
    }
}
