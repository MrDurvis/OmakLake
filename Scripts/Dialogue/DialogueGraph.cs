using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "DialogueGraph", menuName = "Game Data/Dialogue Graph")]
public class DialogueGraph : ScriptableObject
{
    [SerializeField, HideInInspector] private string guid;
    public string Guid => string.IsNullOrEmpty(guid) ? (guid = System.Guid.NewGuid().ToString()) : guid;

    [Tooltip("GUID of the node where this graph starts.")]
    public string startGuid;

    [Tooltip("All nodes in this graph.")]
    public List<DialogueNode> nodes = new();

    public DialogueNode Get(string nodeGuid) => nodes.Find(n => n != null && n.guid == nodeGuid);

#if UNITY_EDITOR
    private void OnValidate()
    {
        bool changed = false;

        if (string.IsNullOrEmpty(guid))
        {
            guid = System.Guid.NewGuid().ToString();
            changed = true;
        }

        if (nodes != null)
        {
            foreach (var n in nodes)
            {
                if (n == null) continue;

                if (string.IsNullOrEmpty(n.guid))
                {
                    n.guid = System.Guid.NewGuid().ToString();
                    changed = true;
                }

                if (n.choices == null)
                {
                    n.choices = new List<DialogueChoice>();
                    changed = true;
                }

                if (n.isChoice && n.choices.Count > 0)
                {
                    n.defaultChoiceIndex = Mathf.Clamp(n.defaultChoiceIndex, 0, n.choices.Count - 1);
                }
                else if (!n.isChoice)
                {
                    n.defaultChoiceIndex = 0; // unused when not a choice
                }
            }
        }

        if (string.IsNullOrEmpty(startGuid) && nodes.Count > 0 && nodes[0] != null)
        {
            startGuid = nodes[0].guid;
            changed = true;
        }

        if (changed) UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
}

[Serializable]
public class DialogueNode
{
    [Tooltip("Unique ID for this node. Auto-filled.")]
    public string guid = "";

    [Tooltip("If true, this node shows choices; otherwise it shows 'text' and then goes to 'nextGuid'.")]
    public bool isChoice;

    [TextArea]
    [Tooltip("Text to display (for non-choice nodes) or the prompt (for choice nodes).")]
    public string text;

    [Tooltip("Choices shown when isChoice == true.")]
    public List<DialogueChoice> choices = new();

    [Tooltip("Next node to go to when this is NOT a choice node.")]
    public string nextGuid;

    [Tooltip("Which option is selected if the player presses B/Cancel on this choice node.")]
    public int defaultChoiceIndex = 0;
}

[Serializable]
public class DialogueChoice
{
    [Tooltip("Label shown to the player, e.g., Yes/No.")]
    public string label = "Option";

    [Tooltip("Node GUID to follow if this option is chosen.")]
    public string nextGuid;

    [Tooltip("Optional meaning so gameplay can react (e.g., PickupYes / PickupNo / None).")]
    public ChoiceSemantic semantic = ChoiceSemantic.None;
}
