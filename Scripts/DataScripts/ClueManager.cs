using UnityEngine;
using System;
using System.Collections.Generic;

[DefaultExecutionOrder(-900)] // Runs after SaveSystem, before most gameplay
public class ClueManager : MonoBehaviour
{
    public static ClueManager Instance { get; private set; }

    [Header("References")]
    [SerializeField] private CognitionBoard cognitionBoard; // Assign in Inspector

    public event Action<ClueData> OnClueDiscovered;

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
    }

    public bool HasClue(string guid) => discovered.ContainsKey(guid);

    public void DiscoverClue(ClueData data)
    {
        if (data == null) return;
        if (HasClue(data.Guid)) return;

        discovered.Add(data.Guid, data);
        SaveSystem.Instance?.MarkClueDiscovered(data.Guid);

        if (cognitionBoard != null)
        {
            cognitionBoard.AddNode(data);

            // 🔧 make sure we pass the GUID, not the whole ClueData
            cognitionBoard.AddSuggestedConnectionsFor(data.Guid);
        }

        OnClueDiscovered?.Invoke(data);
    }
    public void RestoreFromSave(IEnumerable<string> guids, Func<string, ClueData> resolver)
    {
        if (resolver == null) return;

        foreach (var g in guids)
        {
            if (discovered.ContainsKey(g)) continue;

            var data = resolver(g);
            if (data != null)
            {
                discovered[g] = data;
                if (cognitionBoard != null)
                    cognitionBoard.AddNode(data);
            }
            else
            {
                Debug.LogWarning($"[ClueManager] Could not resolve clue GUID '{g}' to a ClueData asset.");
            }
        }

        // NEW: once all nodes exist, build all auto connections (red),
        // then restyle saved confirmed links (green).
        cognitionBoard?.BuildAllAutoConnections();
        cognitionBoard?.RestoreConnectionsFromSave();

        cognitionBoard?.RestoreLayoutFromSave();
    }

    public ClueData GetClue(string guid)
    {
        discovered.TryGetValue(guid, out var data);
        return data;
    }
}
