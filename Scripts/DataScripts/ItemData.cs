using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "ItemData", menuName = "Game Data/Item Data")]
public class ItemData : ScriptableObject
{
    [SerializeField, HideInInspector] private string guid;
    public string Guid => string.IsNullOrEmpty(guid) ? (guid = System.Guid.NewGuid().ToString()) : guid;

    public int itemID;
    public string itemName;
    [TextArea] public string description;
    public Sprite icon;

    public bool canBeClue;
    public ClueData clueData;

    [Header("Dialogue (linear list)")]
    public List<DialogueBlock> dialogue = new List<DialogueBlock>();

    [Header("Dialogue (graph-based)")]
    public DialogueGraph graph;   // <— this is what ItemInteraction is reading

    [Header("Optional follow-ups after PickupYesNo (linear mode)")]
    [TextArea] public string afterYesText;
    [TextArea] public string afterNoText;

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (string.IsNullOrEmpty(guid))
            guid = System.Guid.NewGuid().ToString();
    }
#endif
}

public enum BlockType { Text, Choice }
public enum ChoiceSemantic
{
    None,
    PickupYes,     // explicit YES
    PickupNo,      // explicit NO

    // Legacy (linear list mode only). Leave if you still use the old yesIndex logic.
    PickupYesNo
}

[System.Serializable]
public class DialogueBlock
{
    public BlockType type = BlockType.Text;

    // Text bubble (or choice prompt if you want to reuse this for prompts)
    [TextArea] public string text;

    // Choice-only fields
    public string prompt;
    public List<string> options = new List<string> { "Yes", "No" };
    public int defaultIndex = 0;
    public int yesIndex = 0;
    public ChoiceSemantic semantic = ChoiceSemantic.None;
}
