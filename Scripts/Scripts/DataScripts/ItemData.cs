using UnityEngine;

[CreateAssetMenu(fileName = "ItemData", menuName = "Game Data/Item Data")]
public class ItemData : ScriptableObject
{
    public int itemID;                   // Unique identifier for the item
    public string itemName;              // Name to display, e.g., "Bloody Knife"
    [TextArea]
    public string description;           // Dialogue or info shown when examined
    public Sprite icon;                  // Icon for inventory or UI representation
    public bool canBeClue;               // Indicates if this item can generate a clue
    public ClueData clueData;            // Optional: link to ClueData if this item is a clue
}