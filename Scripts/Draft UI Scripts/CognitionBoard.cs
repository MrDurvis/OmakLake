using UnityEngine;
using System.Collections.Generic;

public class CognitionBoard : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private RectTransform contentRect; // if null, we’ll use our own RectTransform
    [SerializeField] private ClueNode nodePrefab;       // optional; if null we’ll create nodes from code

    public RectTransform ContentRect => contentRect ? contentRect : (RectTransform)transform;

    private readonly Dictionary<string, ClueNode> nodes = new();

    private void Awake()
    {
        // Ensure hidden by default (pause menu will open it)
        gameObject.SetActive(false);

        // Fallback: if user forgot to assign, use our own rect
        if (!contentRect) contentRect = GetComponent<RectTransform>();
        if (!contentRect) Debug.LogError("[CognitionBoard] No RectTransform found for ContentRect.", this);
    }

    public void AddNode(ClueData data)
    {
        if (data == null) { Debug.LogError("[CognitionBoard] AddNode called with NULL ClueData.", this); return; }
        if (nodes.ContainsKey(data.Guid))
        {
            Debug.Log($"[CognitionBoard] Node already exists for '{data.clueName}'.", this);
            return;
        }

        ClueNode node;
        if (nodePrefab != null)
        {
            node = Instantiate(nodePrefab, ContentRect);
        }
        else
        {
            // Build a node entirely from code — no prefab required
            var go = new GameObject($"ClueNode_{data.clueName}", typeof(RectTransform), typeof(ClueNode));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(ContentRect, false);
            node = go.GetComponent<ClueNode>();
        }

        try
        {
            node.Initialize(this, data);
        }
        catch (System.Exception ex)
        {
            Debug.LogException(ex, node);
            Destroy(node.gameObject);
            return;
        }

        nodes.Add(data.Guid, node);
        Debug.Log($"[CognitionBoard] Spawned node for '{data.clueName}'.", this);
    }

    // Drag callbacks
    public void BeginNodeDrag(ClueNode node) { node.Rect.SetAsLastSibling(); }
    public void OnNodeMoved(ClueNode node) { /* could live-update lines later */ }
    public void EndNodeDrag(ClueNode node)
    {
        SaveSystem.Instance?.SetNodePosition(node.ClueGuid, node.Rect.anchoredPosition);
    }

    // Restore layout
    public void RestoreLayoutFromSave()
    {
        var layout = SaveSystem.Instance?.GetBoardLayout();
        if (layout == null) return;

        ContentRect.localScale = Vector3.one * layout.zoom;
        ContentRect.anchoredPosition = layout.pan;

        foreach (var kvp in layout.nodePositions)
            if (nodes.TryGetValue(kvp.Key, out var node))
                node.Rect.anchoredPosition = kvp.Value;
    }
}
