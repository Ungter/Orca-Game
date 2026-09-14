using UnityEngine;

// Central inventory for the player: a fixed number of slots filled in pickup order.
// Gameplay code adds items through TryAdd; UI code reads slots with GetItem.
public class InventorySystem : MonoBehaviour
{
    public static InventorySystem instance;

    [SerializeField]
    private int slotCount = 5;

    private string[] items;
    private int itemCount;

    private void Awake()
    {
        instance = this;
        items = new string[slotCount];
        itemCount = 0;
    }

    public int SlotCount()
    {
        return slotCount;
    }

    public bool IsFull()
    {
        return itemCount >= slotCount;
    }

    // Stores the item in the next free slot. Returns false when the inventory is full.
    public bool TryAdd(string itemName)
    {
        if (IsFull())
        {
            Debug.Log("Inventory is full - could not pick up " + itemName);
            return false;
        }

        items[itemCount] = itemName;
        itemCount += 1;
        Debug.Log("Picked up " + itemName + " (" + itemCount + "/" + slotCount + ")");
        return true;
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