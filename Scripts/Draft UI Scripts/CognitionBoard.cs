using UnityEngine;
using System.Collections.Generic;

public class CognitionBoard : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private RectTransform contentRect;      // board root
    [SerializeField] private ClueNode nodePrefab;            // your node
    [SerializeField] private ConnectionLineUI linePrefab;    // NEW: UI line prefab

    [Header("Layout")]
    [SerializeField] private float bandPadding = 120f;
    [SerializeField] private float ringRadius  = 220f;
    [SerializeField] private float jitter      = 40f;

    public RectTransform ContentRect => contentRect ? contentRect : (RectTransform)transform;

    private readonly Dictionary<string, ClueNode> nodes = new();
    private readonly Dictionary<string, ConnectionLineUI> lines = new(); // key = a|b
    private readonly int[] categoryCounts = new int[4];

    private static string Key(string a, string b) => string.CompareOrdinal(a, b) < 0 ? $"{a}|{b}" : $"{b}|{a}";

    private void Awake()
    {
        gameObject.SetActive(false);
        if (!contentRect) contentRect = GetComponent<RectTransform>();
        if (!contentRect) Debug.LogError("[CognitionBoard] No RectTransform found for ContentRect.", this);
    }

    private void OnEnable()
    {
        // Rebuild connections (useful if board is toggled on/off)
        RebuildAllConnections();
    }

    // ----------------- NODES -----------------
    public void AddNode(ClueData data)
    {
        if (data == null) { Debug.LogError("[CognitionBoard] AddNode NULL.", this); return; }
        if (nodes.ContainsKey(data.Guid)) { Debug.Log($"[CognitionBoard] Node exists for '{data.clueName}'.", this); return; }

        ClueNode node = nodePrefab
            ? Instantiate(nodePrefab, ContentRect)
            : CreateNodeRuntime();

        try { node.Initialize(this, data); }
        catch (System.Exception ex)
        {
            Debug.LogException(ex, node);
            Destroy(node.gameObject);
            return;
        }

        // Initial placement: saved position, else category band
        if (TryLoadPos(data.Guid, out var pos)) node.Rect.anchoredPosition = pos;
        else node.Rect.anchoredPosition = NextSlotForCategory(data.category);

        nodes.Add(data.Guid, node);
        AutoConnectFor(data);
        ApplySavedConfirmStates();
        // Debug.Log($"[CognitionBoard] Spawned node '{data.clueName}'.", this);
    }

    private ClueNode CreateNodeRuntime()
    {
        var go = new GameObject("ClueNode", typeof(RectTransform), typeof(ClueNode));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(ContentRect, false);
        return go.GetComponent<ClueNode>();
    }

    private Vector2 NextSlotForCategory(ClueData.ClueCategory cat)
    {
        int band = (int)cat;
        categoryCounts[band]++;

        // band center Y
        float h = ContentRect.rect.height;
        float bands = 4f;
        float t = band / (bands - 1f);
        float bandCenterY = Mathf.Lerp(h * 0.40f, -h * 0.40f, t);

        // spread around a circle per band
        float angle = (categoryCounts[band] * 137.508f) * Mathf.Deg2Rad; // golden angle
        Vector2 center = new(0, bandCenterY);
        Vector2 p = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * ringRadius;
        p += Random.insideUnitCircle * jitter;
        return p;
    }

    // Drag callbacks
    public void BeginNodeDrag(ClueNode node) { node.Rect.SetAsLastSibling(); }
    public void OnNodeMoved(ClueNode node) { /* lines update themselves in LateUpdate */ }
    public void EndNodeDrag(ClueNode node)
    {
        SaveSystem.Instance?.SetNodePosition(node.ClueGuid, node.Rect.anchoredPosition);
    }

    // ----------------- LINES -----------------
    private void AutoConnectFor(ClueData data)
    {
        if (data.relatedClueGuids == null || data.relatedClueGuids.Count == 0) return;
        if (!nodes.TryGetValue(data.Guid, out var a)) return;

        foreach (var otherGuid in data.relatedClueGuids)
        {
            if (nodes.TryGetValue(otherGuid, out var b))
                EnsureLine(a, b, suggested: true);
        }
    }

    private void EnsureLine(ClueNode a, ClueNode b, bool suggested)
    {
        string key = Key(a.ClueGuid, b.ClueGuid);
        if (lines.ContainsKey(key)) return;

        var line = Instantiate(linePrefab, ContentRect);
        line.Init(a.Rect, b.Rect, suggested ? new Color(0.65f, 0.1f, 0.1f, 0.9f) : Color.green, 6f);
        lines.Add(key, line);
    }

    public void ConfirmLink(string aGuid, string bGuid)
    {
        string key = Key(aGuid, bGuid);
        if (!lines.TryGetValue(key, out var line))
        {
            if (!nodes.TryGetValue(aGuid, out var a) || !nodes.TryGetValue(bGuid, out var b)) return;
            line = Instantiate(linePrefab, ContentRect);
            lines[key] = line;
            line.Init(a.Rect, b.Rect, Color.green, 6f);
        }
        else
        {
            line.SetColor(Color.green);
        }

        SaveSystem.Instance?.MarkLinkConfirmed(aGuid, bGuid);
    }

    private void RebuildAllConnections()
    {
        // Clear old
        foreach (var kv in lines) if (kv.Value) Destroy(kv.Value.gameObject);
        lines.Clear();

        // Suggested lines from data
        foreach (var n in nodes.Values) AutoConnectFor(n.Data);

        // Confirmed state from save
        ApplySavedConfirmStates();
    }

    private void ApplySavedConfirmStates()
    {
        var layout = SaveSystem.Instance?.GetBoardLayout();
        if (layout == null) return;

        foreach (var link in layout.confirmedLinks)
        {
            // If a line exists → tint green, else create as confirmed.
            string key = Key(link.a, link.b);
            if (lines.TryGetValue(key, out var line)) line.SetColor(Color.green);
            else if (nodes.TryGetValue(link.a, out var a) && nodes.TryGetValue(link.b, out var b))
            {
                var l = Instantiate(linePrefab, ContentRect);
                l.Init(a.Rect, b.Rect, Color.green, 6f);
                lines[key] = l;
            }
        }
    }

    // ----------------- SAVE LOAD -----------------
    public void RestoreLayoutFromSave()
    {
        var layout = SaveSystem.Instance?.GetBoardLayout();
        if (layout == null) return;

        ContentRect.localScale      = Vector3.one * layout.zoom;
        ContentRect.anchoredPosition = layout.pan;

        foreach (var kvp in layout.nodePositions)
            if (nodes.TryGetValue(kvp.Key, out var node))
                node.Rect.anchoredPosition = kvp.Value;

        ApplySavedConfirmStates();
    }

    private bool TryLoadPos(string guid, out Vector2 pos)
    {
        pos = default;
        var layout = SaveSystem.Instance?.GetBoardLayout();
        if (layout == null) return false;
        return layout.nodePositions.TryGetValue(guid, out pos);
    }
}
