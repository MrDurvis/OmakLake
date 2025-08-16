using UnityEngine;
using System;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
// ...

[DefaultExecutionOrder(-900)] // Runs after SaveSystem, before most gameplay
public class ClueManager : MonoBehaviour
{
    public static ClueManager Instance { get; private set; }

    [Header("References")]
    [SerializeField] private CognitionBoard cognitionBoard; // Assign in Inspector

    public event Action<ClueData> OnClueDiscovered;

    private readonly Dictionary<string, ClueData> discovered = new();

    private readonly Queue<ClueData> _pendingAdds = new();

private bool EnsureBoard()
{
    if (cognitionBoard) return true;

    // Find even if the board object is inactive (it starts inactive in Awake)
#if UNITY_2022_1_OR_NEWER
    cognitionBoard = FindFirstObjectByType<CognitionBoard>(FindObjectsInactive.Include);
#else
    cognitionBoard = Resources.FindObjectsOfTypeAll<CognitionBoard>()?.Length > 0
        ? Resources.FindObjectsOfTypeAll<CognitionBoard>()[0]
        : null;
#endif
    return cognitionBoard != null;
}

private void FlushPending()
{
    if (!cognitionBoard) return;
    while (_pendingAdds.Count > 0)
    {
        var data = _pendingAdds.Dequeue();
        cognitionBoard.AddNode(data);
        cognitionBoard.AddSuggestedConnectionsFor(data.Guid);
    }
}


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

    private void OnEnable()
{
    SceneManager.sceneLoaded += OnSceneLoaded;
}

private void OnDisable()
{
    SceneManager.sceneLoaded -= OnSceneLoaded;
}

private void OnSceneLoaded(Scene s, LoadSceneMode m)
{
    if (EnsureBoard()) FlushPending();
}

    public bool HasClue(string guid) => discovered.ContainsKey(guid);

    public void DiscoverClue(ClueData data)
{
    if (data == null) return;
    if (HasClue(data.Guid)) return;

    discovered.Add(data.Guid, data);
    SaveSystem.Instance?.MarkClueDiscovered(data.Guid);

    if (!EnsureBoard())
    {
        // Board not in this scene yet — remember to add as soon as it appears.
        _pendingAdds.Enqueue(data);
        return;
    }

    cognitionBoard.AddNode(data);
    cognitionBoard.AddSuggestedConnectionsFor(data.Guid);

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

            if (!EnsureBoard())
            {
                _pendingAdds.Enqueue(data);
            }
            else
            {
                cognitionBoard.AddNode(data);
            }
        }
        else
        {
            Debug.LogWarning($"[ClueManager] Could not resolve clue GUID '{g}' to a ClueData asset.");
        }
    }

    // Once nodes are ensured, rebuild autos
    if (EnsureBoard())
    {
        cognitionBoard.BuildAllAutoConnections();
    }
}


    public ClueData GetClue(string guid)
    {
        discovered.TryGetValue(guid, out var data);
        return data;
    }

    public void ClearAllRuntime()
{
    // Clear discovered cache
    discovered.Clear();

    // Clear the visual board too (if it's around)
    cognitionBoard?.ClearAll();
}

}
