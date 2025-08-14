using UnityEngine;
using System;
using System.Collections.Generic;

[DefaultExecutionOrder(-900)] // Runs after SaveSystem, before most gameplay
public class ClueManager : MonoBehaviour
{
    public static ClueManager Instance { get; private set; }

    [Header("References")]
    [SerializeField] private CognitionBoard cognitionBoard; // Assign in Inspector

    // Event fired when a new clue is discovered
    public event Action<ClueData> OnClueDiscovered;

    // Runtime storage of discovered clues (by GUID)
    private readonly Dictionary<string, ClueData> discovered = new();

    private void Awake()
    {
        if (Instance && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        Debug.Log("[ClueManager] Awake. Ready to track clues.");
    }

    /// <summary>
    /// Check if the clue has already been discovered.
    /// </summary>
    public bool HasClue(string guid) => discovered.ContainsKey(guid);

    /// <summary>
    /// Record a clue as discovered, persist it, and add it to the board.
    /// </summary>
    public void DiscoverClue(ClueData data)
    {
        if (data == null)
        {
            Debug.LogError("[ClueManager] DiscoverClue called with NULL data.");
            return;
        }

        if (HasClue(data.Guid))
        {
            Debug.Log($"[ClueManager] Clue '{data.clueName}' already discovered.");
            return;
        }

        // Store runtime
        discovered.Add(data.Guid, data);
        Debug.Log($"[ClueManager] Discovered clue '{data.clueName}' (GUID: {data.Guid})");

        // Persist to save
        if (SaveSystem.Instance != null)
        {
            SaveSystem.Instance.MarkClueDiscovered(data.Guid);
            Debug.Log($"[ClueManager] Saved '{data.clueName}' to SaveSystem.");
        }
        else
        {
            Debug.LogWarning("[ClueManager] SaveSystem.Instance is NULL. Clue discovery not persisted.");
        }

        // Add node to cognition board
        if (cognitionBoard != null)
        {
            cognitionBoard.AddNode(data);
            Debug.Log($"[ClueManager] Added '{data.clueName}' to Cognition Board.");
        }
        else
        {
            Debug.LogWarning("[ClueManager] CognitionBoard not assigned. Node will appear after restore.");
        }

        // Notify listeners
        OnClueDiscovered?.Invoke(data);
    }

    /// <summary>
    /// Restore clues and board layout from save.
    /// </summary>
    /// <param name="guids">Collection of clue GUIDs to restore.</param>
    /// <param name="resolver">Function to resolve a GUID to its ClueData asset.</param>
    public void RestoreFromSave(IEnumerable<string> guids, Func<string, ClueData> resolver)
    {
        if (resolver == null)
        {
            Debug.LogError("[ClueManager] RestoreFromSave called without a resolver function.");
            return;
        }

        foreach (var g in guids)
        {
            if (discovered.ContainsKey(g))
                continue;

            var data = resolver(g);
            if (data != null)
            {
                discovered[g] = data;
                if (cognitionBoard != null)
                    cognitionBoard.AddNode(data);
                else
                    Debug.LogWarning($"[ClueManager] Board missing; will not visually add '{data.clueName}' until later.");
            }
            else
            {
                Debug.LogWarning($"[ClueManager] Could not resolve clue GUID '{g}' to a ClueData asset.");
            }
        }

        cognitionBoard?.RestoreLayoutFromSave();
        Debug.Log("[ClueManager] RestoreFromSave completed.");
    }

    /// <summary>
    /// Get a discovered clue by GUID.
    /// </summary>
    public ClueData GetClue(string guid)
    {
        discovered.TryGetValue(guid, out var data);
        return data;
    }
}
