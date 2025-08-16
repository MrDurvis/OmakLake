using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;

[Serializable]
public struct Link
{
    public string a;
    public string b;
}

[Serializable]
public class BoardLayoutSave
{
    public float zoom = 1f;
    public Vector2 pan = Vector2.zero;
    public Dictionary<string, Vector2> nodePositions = new(); // ClueGuid -> anchored pos
    public List<Link> confirmedLinks = new();
}

[Serializable]
public class GameSave
{
    public HashSet<string> discoveredClues = new();
    public HashSet<string> collectedItems = new();
    public BoardLayoutSave board = new();
}

[DefaultExecutionOrder(-1000)] // Run very early so it's ready for all other scripts
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

    // --- Clue tracking ---
    public void MarkClueDiscovered(string clueGuid)
    {
        if (!data.discoveredClues.Contains(clueGuid))
        {
            data.discoveredClues.Add(clueGuid);
            Save();
        }
    }
    public IEnumerable<string> GetDiscoveredClues() => data.discoveredClues;

    // --- Link tracking ---
    public void MarkLinkConfirmed(string a, string b)
    {
        if (!data.board.confirmedLinks.Exists(l => (l.a == a && l.b == b) || (l.a == b && l.b == a)))
        {
            data.board.confirmedLinks.Add(new Link { a = a, b = b });
            Save();
        }
    }

    // NEW: check if a pair is already confirmed
    public bool IsLinkConfirmed(string a, string b)
    {
        return data.board.confirmedLinks.Exists(l => (l.a == a && l.b == b) || (l.a == b && l.b == a));
    }

    // NEW: expose confirmed list for restore
    public IEnumerable<Link> GetConfirmedLinks() => data.board.confirmedLinks;

    // --- Board layout ---
    public void SetNodePosition(string clueGuid, Vector2 anchoredPos)
    {
        data.board.nodePositions[clueGuid] = anchoredPos;
        Save();
    }
    public void SetBoardZoom(float z) { data.board.zoom = z; Save(); }
    public void SetBoardPan(Vector2 p) { data.board.pan = p; Save(); }
    public BoardLayoutSave GetBoardLayout() => data.board;
}
