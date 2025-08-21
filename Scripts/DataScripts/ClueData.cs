using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "ClueData", menuName = "Game Data/Clue Data")]
public class ClueData : ScriptableObject
{
    [SerializeField, HideInInspector] private string guid;
    public string Guid => string.IsNullOrEmpty(guid) ? (guid = System.Guid.NewGuid().ToString()) : guid;

    [Header("Core")]
    public string clueName;
    [TextArea] public string description;
    public Sprite icon;
    public ClueCategory category;

    [Header("UI Overrides (optional)")]
    [Tooltip("If empty, UI will use 'clueName'.")]
    public string displayTitle;
    [Tooltip("If empty, UI will use 'description'.")]
    [TextArea(2, 8)] public string displayBody;

    [Header("Related Clues (drag & drop)")]
    [Tooltip("Drag other ClueData assets here. We'll keep the GUID list in sync automatically.")]
    public List<ClueData> relatedClues = new();

    // Kept for runtime/board code. Hidden so you don't edit by hand.
    [SerializeField, HideInInspector] public List<string> relatedClueGuids = new();

    public enum ClueCategory { Person, Object, Location, Event }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Ensure we have a GUID
        if (string.IsNullOrEmpty(guid))
            guid = System.Guid.NewGuid().ToString();

        // Keep GUID list in sync with the drag-and-drop list
        SyncRelatedGuidList();
    }

    /// <summary>
    /// Copies relatedClues -> relatedClueGuids. Removes nulls/duplicates/self.
    /// </summary>
    private void SyncRelatedGuidList()
    {
        if (relatedClueGuids == null) relatedClueGuids = new List<string>();
        var set = new HashSet<string>();

        if (relatedClues != null)
        {
            foreach (var cd in relatedClues)
            {
                if (!cd) continue;
                var g = cd.Guid;            // forces GUID creation for each related asset
                if (string.IsNullOrEmpty(g)) continue;
                if (g == Guid) continue;    // no self-link
                set.Add(g);
            }
        }

        bool changed = false;
        if (set.Count != relatedClueGuids.Count) changed = true;
        else
        {
            foreach (var g in relatedClueGuids)
                if (!set.Contains(g)) { changed = true; break; }
        }

        if (changed)
        {
            relatedClueGuids.Clear();
            relatedClueGuids.AddRange(set);
            UnityEditor.EditorUtility.SetDirty(this);
        }
    }

    // Optional helpers to speed up authoring

    [ContextMenu("Related ▸ Make Links Mutual")]
    private void MakeLinksMutual()
    {
        // Ensure every related clue also lists *this* clue.
        foreach (var other in relatedClues)
        {
            if (!other) continue;
            if (!other.relatedClues.Contains(this))
            {
                other.relatedClues.Add(this);
                other.SyncRelatedGuidList();
                UnityEditor.EditorUtility.SetDirty(other);
            }
        }
        SyncRelatedGuidList();
        UnityEditor.EditorUtility.SetDirty(this);
        Debug.Log($"[{name}] Links made mutual with {relatedClues.Count} clues.");
    }

    [ContextMenu("Related ▸ Clear Nulls & Duplicates")]
    private void CleanRelatedList()
    {
        var seen = new HashSet<ClueData>();
        for (int i = relatedClues.Count - 1; i >= 0; i--)
        {
            var c = relatedClues[i];
            if (!c || c == this || !seen.Add(c))
                relatedClues.RemoveAt(i);
        }
        SyncRelatedGuidList();
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
}
