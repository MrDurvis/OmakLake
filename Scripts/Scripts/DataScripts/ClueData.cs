using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "ClueData", menuName = "Game Data/Clue Data")]
public class ClueData : ScriptableObject
{
    public int clueID;                      // Unique identifier for the clue
    public string clueName;                 // Name shown on the Cognition Board, e.g., "Bloody Knife"
    [TextArea]
    public string description;              // Description or info about the clue
    public Sprite icon;                     // Icon used on the node or clue indicator
    public ClueCategory category;           // Enum or categorization (e.g., Person, Object, Location)
    public List<int> relatedCluesIDs;       // Optional: IDs of related clues for connections

    public enum ClueCategory
    {
        Person,
        Object,
        Location,
        Event
    }
}