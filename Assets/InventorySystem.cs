using System.Collections.Generic;
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
    private readonly HashSet<string> revealedWords = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> RevealedWords => revealedWords;

    public bool HasRevealedWord(string word) => !string.IsNullOrWhiteSpace(word) && revealedWords.Contains(word.Trim());

    public void RevealClueWords(string words)
    {
        if (string.IsNullOrWhiteSpace(words)) return;
        foreach (string part in words.Split(','))
        {
            string word = part.Trim().ToUpperInvariant();
            if (word.Length == 0) continue;
            bool lettersOnly = true;
            foreach (char letter in word)
                if (letter < 'A' || letter > 'Z') { lettersOnly = false; break; }
            if (lettersOnly) revealedWords.Add(word);
        }
    }

    private void Awake()
    {
        instance = this;
        items = new string[slotCount];
        itemSprites = new Sprite[slotCount];
        itemCount = 0;
        revealedWords.Clear();
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

    public bool Contains(string itemName)
    {
        for (int i = 0; i < itemCount; i++)
            if (items[i] == itemName) return true;
        return false;
    }

    // Quest hand-ins free a slot without separating any remaining item from its sprite.
    public bool TryRemove(string itemName)
    {
        for (int i = 0; i < itemCount; i++)
        {
            if (items[i] != itemName) continue;
            for (int j = i; j < itemCount - 1; j++)
            {
                items[j] = items[j + 1];
                itemSprites[j] = itemSprites[j + 1];
            }
            itemCount--;
            items[itemCount] = null;
            itemSprites[itemCount] = null;
            NotifyInteraction();
            return true;
        }
        return false;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
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
