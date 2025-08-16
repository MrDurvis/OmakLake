using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;

[Serializable] public struct Link { public string a; public string b; }

[Serializable]
public class BoardLayoutSave
{
    public float zoom = 1f;
    public Vector2 pan = Vector2.zero;
    public Dictionary<string, Vector2> nodePositions = new();
    public List<Link> confirmedLinks = new();
}

[Serializable]
public class GameSave
{
    public HashSet<string> discoveredClues = new();
    public HashSet<string> collectedItems = new();

    // NEW: keep order of discovery
    public List<string> discoveryOrder = new();

    public BoardLayoutSave board = new();
}

[DefaultExecutionOrder(-1000)]
public class SaveSystem : MonoBehaviour
{
    public static SaveSystem Instance { get; private set; }

    private string SavePath => Path.Combine(Application.persistentDataPath, "save.json");
    private GameSave data = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void EnsureExists()
    {
        if (Instance != null) return;
        var go = new GameObject("SaveSystem");
        go.AddComponent<SaveSystem>();
        DontDestroyOnLoad(go);
    }

    private void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        Load();
        Debug.Log($"[SaveSystem] Bootstrapped and loaded from '{SavePath}'.");
    }

    // --- Persistence API ---
    public void Save()
    {
        try
        {
            var json = JsonUtility.ToJson(data, prettyPrint: true);
            File.WriteAllText(SavePath, json);
            Debug.Log($"[SaveSystem] Saved to {SavePath}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SaveSystem] Failed to save: {ex}");
        }
    }

    public void Load()
    {
        try
        {
            if (File.Exists(SavePath))
            {
                var json = File.ReadAllText(SavePath);
                data = JsonUtility.FromJson<GameSave>(json) ?? new GameSave();
                Debug.Log($"[SaveSystem] Loaded save from {SavePath}");
            }
            else
            {
                data = new GameSave();
                Debug.Log("[SaveSystem] No existing save found, starting fresh.");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SaveSystem] Failed to load: {ex}");
            data = new GameSave();
        }

        // Backfill if older saves had no discoveryOrder
        if (data.discoveryOrder == null) data.discoveryOrder = new List<string>();
        foreach (var g in data.discoveredClues)
            if (!data.discoveryOrder.Contains(g))
                data.discoveryOrder.Add(g);
    }

    // --- Item tracking ---
    public void MarkItemCollected(string itemGuid)
    {
        if (!data.collectedItems.Contains(itemGuid))
        {
            data.collectedItems.Add(itemGuid);
            Save();
        }
    }
    public bool IsItemCollected(string itemGuid) => data.collectedItems.Contains(itemGuid);

    // --- Clue tracking + order ---
    public void MarkClueDiscovered(string clueGuid)
    {
        bool added = data.discoveredClues.Add(clueGuid);
        if (added && !data.discoveryOrder.Contains(clueGuid))
            data.discoveryOrder.Add(clueGuid);
        if (added) Save();
    }
    public IEnumerable<string> GetDiscoveredClues() => data.discoveredClues;

    // NEW: expose discovery order + index helpers
    public IReadOnlyList<string> GetDiscoveryOrder() => data.discoveryOrder;
    public int GetDiscoveryIndex(string guid)
    {
        if (data.discoveryOrder == null) return int.MaxValue;
        return data.discoveryOrder.IndexOf(guid);
    }

    // --- Link tracking ---
    public void MarkLinkConfirmed(string a, string b)
    {
        if (!data.board.confirmedLinks.Exists(l => (l.a == a && l.b == b) || (l.a == b && l.b == a)))
        {
            data.board.confirmedLinks.Add(new Link { a = a, b = b });
            Save();
        }
    }

    // --- Board layout ---
    public void SetNodePosition(string clueGuid, Vector2 anchoredPos)
    {
        data.board.nodePositions[clueGuid] = anchoredPos; Save();
    }
    public void SetBoardZoom(float z) { data.board.zoom = z; Save(); }
    public void SetBoardPan(Vector2 p) { data.board.pan = p; Save(); }
    public BoardLayoutSave GetBoardLayout() => data.board;

    public IEnumerable<Link> GetConfirmedLinks() => data.board.confirmedLinks;
    public bool IsLinkConfirmed(string a, string b)
    {
        foreach (var l in data.board.confirmedLinks)
            if ((l.a == a && l.b == b) || (l.a == b && l.b == a)) return true;
        return false;
    }
    
    public void WipeSave(bool deleteFile = true)
{
    // Reset in-memory save
    data = new GameSave();

    // Optionally delete the on-disk file so it's a truly fresh boot next time too
    try
    {
        if (deleteFile && File.Exists(SavePath))
            File.Delete(SavePath);
    }
    catch (Exception ex)
    {
        Debug.LogError($"[SaveSystem] Failed to delete save file: {ex}");
    }

    // Not strictly necessary to re-save here (we just wiped),
    // but calling Save() would create a fresh empty file if you want that.
    // Save();
}

}
