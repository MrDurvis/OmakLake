using UnityEngine;
using System.Collections.Generic;
using UnityEngine.InputSystem;

public class CognitionBoard : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private RectTransform contentRect;           // Board content; if null, uses own RectTransform
    [SerializeField] private ClueNode nodePrefab;                 // Optional; if null node is built in code
    [SerializeField] private ConnectionLineUI linePrefab;         // REQUIRED: UI line prefab (Image + ConnectionLineUI)

    [Header("Line Style")]
    [SerializeField] private Color suggestedColor = new(0.90f, 0.25f, 0.25f, 1f);
    [SerializeField] private float suggestedWidth = 4f;
    [SerializeField] private Color confirmedColor = new(0.20f, 0.90f, 0.35f, 1f);
    [SerializeField] private float confirmedWidth = 5.5f;

    [Header("Navigation (UI map)")]
    [Tooltip("Typically bind to UI/Navigate (Vector2)")]
    [SerializeField] private InputActionReference navigateAction;
    [Tooltip("Typically bind to UI/Submit (Button)")]
    [SerializeField] private InputActionReference submitAction;

    [Header("Navigation Tuning")]
    [SerializeField] private float navDeadZone = 0.5f;      // stick magnitude threshold
    [SerializeField] private float navFirstDelay = 0.25f;   // seconds before first repeat
    [SerializeField] private float navRepeat = 0.15f;       // seconds between repeats while held
    [SerializeField] private float dirConeDegrees = 70f;    // how wide the target cone is
    [SerializeField] private float minHopDistance = 30f;    // ignore tiny moves (px)
    [SerializeField] private float selectedScale = 1.08f;   // visual scale for selection
    [SerializeField] private bool centerOnSubmit = true;    // press Submit to pan to node

    public RectTransform ContentRect => contentRect ? contentRect : (RectTransform)transform;

    // Runtime maps
    private readonly Dictionary<string, ClueNode> nodes = new();                      // guid -> node
    private readonly Dictionary<(string, string), ConnectionLineUI> lines = new();    // ordered pair -> line

    // Layers (Lines under Nodes so strings sit behind)
    private RectTransform linesLayer;
    private RectTransform nodesLayer;

    // Selection state
    private string selectedGuid;
    private ClueNode selectedNode;
    private Vector2 lastNavDir;
    private float nextNavTime;  // for repeat-gated navigation
    private bool navHeld;       // whether we're in repeat mode

    private float cosCone;      // cached from dirConeDegrees

    private void Awake()
    {
        // Keep board hidden by default; PauseMenu will activate it
        gameObject.SetActive(false);

        if (!contentRect) contentRect = GetComponent<RectTransform>();
        if (!contentRect) Debug.LogError("[CognitionBoard] No RectTransform for content.", this);

        EnsureLayers();

        cosCone = Mathf.Cos(dirConeDegrees * Mathf.Deg2Rad);
    }

    private void OnEnable()
    {
        // We only *read* inputs; Pause Menu should be enabling the UI map
        try { navigateAction?.action?.Enable(); } catch {}
        try { submitAction?.action?.Enable(); } catch {}

        // Reset repeat gate
        navHeld = false;
        nextNavTime = 0f;

        // If nothing is selected, auto-pick a nice starting node
        if (selectedNode == null) AutoSelectClosestToCenter();
    }

    private void OnDisable()
    {
        // Do not Disable() the UI map here (PauseMenu owns that)
        // Just clear our repeat gate & selection visuals
        if (selectedNode) selectedNode.Rect.localScale = Vector3.one;
        selectedNode = null;
        selectedGuid = null;
        navHeld = false;
        nextNavTime = 0f;
    }

    private void EnsureLayers()
    {
        linesLayer = FindOrCreateLayer("Lines", 0);
        nodesLayer = FindOrCreateLayer("Nodes", 1);
    }

    private RectTransform FindOrCreateLayer(string name, int siblingIndex)
    {
        var t = ContentRect.Find(name) as RectTransform;
        if (!t)
        {
            var go = new GameObject(name, typeof(RectTransform));
            t = go.GetComponent<RectTransform>();
            t.SetParent(ContentRect, false);
            t.anchorMin = Vector2.zero;
            t.anchorMax = Vector2.one;
            t.offsetMin = Vector2.zero;
            t.offsetMax = Vector2.zero;
        }
        t.SetSiblingIndex(siblingIndex);
        return t;
    }

    // ---------- Public API ----------

    public void AddNode(ClueData data)
    {
        if (!data) { Debug.LogError("[CognitionBoard] AddNode null data"); return; }
        if (nodes.ContainsKey(data.Guid))
        {
            // Node already exists — still try to add any missing links
            BuildAutoLinksTouching(data.Guid);
            return;
        }

        // Create node
        ClueNode node;
        if (nodePrefab)
        {
            node = Instantiate(nodePrefab, nodesLayer);
        }
        else
        {
            var go = new GameObject($"ClueNode_{data.clueName}", typeof(RectTransform), typeof(ClueNode));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(nodesLayer, false);
            node = go.GetComponent<ClueNode>();
        }

        node.Initialize(this, data);
        nodes.Add(data.Guid, node);

        // Try to make all auto-links involving this clue
        BuildAutoLinksTouching(data.Guid);

        // If board is open & nothing selected yet, select this one (feels nice)
        if (isActiveAndEnabled && selectedNode == null)
            SelectNodeInternal(node);
    }

    // drag callbacks (called by ClueNode)
    public void BeginNodeDrag(ClueNode _) { /* could raise Z-order, etc. */ }
    public void OnNodeMoved(ClueNode _)   { /* line scripts track positions per LateUpdate */ }
    public void EndNodeDrag(ClueNode node)
    {
        SaveSystem.Instance?.SetNodePosition(node.ClueGuid, node.Rect.anchoredPosition);
    }

    // restore pan/zoom/positions (then rebuild lines)
    public void RestoreLayoutFromSave()
    {
        var layout = SaveSystem.Instance?.GetBoardLayout();
        if (layout == null) return;

        ContentRect.localScale       = Vector3.one * layout.zoom;
        ContentRect.anchoredPosition = layout.pan;

        foreach (var kvp in layout.nodePositions)
            if (nodes.TryGetValue(kvp.Key, out var node))
                node.Rect.anchoredPosition = kvp.Value;

        RebuildAllAutoLinks(); // will style as confirmed when appropriate

        // Re-evaluate selection (closest to center)
        AutoSelectClosestToCenter();
    }

    // ---------- Update: navigation & submit ----------

    private void Update()
    {
        if (!isActiveAndEnabled) return;
        if (nodes.Count == 0) return;

        // NAV
        Vector2 dir = Vector2.zero;
        try { dir = navigateAction ? navigateAction.action.ReadValue<Vector2>() : Vector2.zero; }
        catch {}

        float mag = dir.magnitude;
        float now = Time.unscaledTime;

        if (mag > navDeadZone)
        {
            Vector2 norm = dir / mag;

            if (!navHeld || now >= nextNavTime || Vector2.Dot(norm, lastNavDir) < 0.65f)
            {
                lastNavDir = norm;
                bool moved = MoveSelectionInDirection(norm);
                navHeld = true;
                nextNavTime = now + (moved ? navRepeat : navRepeat * 0.5f); // shorter if no target found
            }
        }
        else
        {
            navHeld = false;
            nextNavTime = 0f;
        }

        // SUBMIT
        bool submitTriggered = false;
        try { submitTriggered = submitAction && submitAction.action.triggered; } catch {}
        if (submitTriggered && selectedNode && centerOnSubmit)
        {
            CenterOnNode(selectedNode);
        }
    }

    // ---------- Selection helpers ----------

    private void AutoSelectClosestToCenter()
    {
        if (nodes.Count == 0) { ClearSelection(); return; }

        // Content space "center" is (0,0) if you use simple pan logic.
        // Choose the node with smallest |pos|.
        float best = float.MaxValue;
        ClueNode bestNode = null;

        foreach (var kv in nodes)
        {
            var n = kv.Value;
            if (!n || !n.Rect) continue;
            float d2 = n.Rect.anchoredPosition.sqrMagnitude;
            if (d2 < best) { best = d2; bestNode = n; }
        }

        if (bestNode) SelectNodeInternal(bestNode);
    }

    private void SelectNodeInternal(ClueNode node)
    {
        if (selectedNode == node) return;

        // clear old visual
        if (selectedNode) selectedNode.Rect.localScale = Vector3.one;

        selectedNode = node;
        selectedGuid = node ? node.ClueGuid : null;

        if (selectedNode)
        {
            selectedNode.Rect.localScale = Vector3.one * selectedScale;
            selectedNode.Rect.SetAsLastSibling(); // draw on top of other nodes (lines are in separate layer)
        }
    }

    private void ClearSelection()
    {
        if (selectedNode) selectedNode.Rect.localScale = Vector3.one;
        selectedNode = null;
        selectedGuid = null;
    }

    private bool MoveSelectionInDirection(Vector2 dir)
    {
        // If nothing selected yet, auto-pick something reasonable
        if (!selectedNode)
        {
            AutoSelectClosestToCenter();
            return selectedNode != null;
        }

        var from = selectedNode.Rect.anchoredPosition;
        string bestGuid = null;
        float bestScore = float.MaxValue;

        foreach (var kv in nodes)
        {
            var n = kv.Value;
            if (!n || n == selectedNode) continue;

            Vector2 to = n.Rect.anchoredPosition - from;
            float dist = to.magnitude;
            if (dist < minHopDistance) continue;

            Vector2 nd = to / dist;
            float dot = Vector2.Dot(nd, dir);
            if (dot < cosCone) continue; // outside direction cone

            // Lower score = better. Weight angle more than distance.
            float angleCost = 1f - dot;            // [0..2], smaller = closer to direction
            float distCost  = dist * 0.0015f;      // tune distance influence
            float score = angleCost * 1.25f + distCost;

            if (score < bestScore)
            {
                bestScore = score;
                bestGuid = kv.Key;
            }
        }

        if (bestGuid != null && nodes.TryGetValue(bestGuid, out var bestNode))
        {
            SelectNodeInternal(bestNode);
            return true;
        }
        return false;
    }

    private void CenterOnNode(ClueNode node)
    {
        if (!node || !node.Rect || !ContentRect) return;
        // Simple pan: move content so that node sits at (0,0) in content space
        ContentRect.anchoredPosition = -node.Rect.anchoredPosition;
        // Persist pan, if you like:
        SaveSystem.Instance?.SetBoardPan(ContentRect.anchoredPosition);
    }

    // ---------- Auto-link logic ----------

    // Build all links that touch a given clue (both directions)
    private void BuildAutoLinksTouching(string guid)
    {
        if (!nodes.ContainsKey(guid)) return;

        // A) this clue lists others as related
        var aData = nodes[guid].Data;
        if (aData?.relatedClueGuids != null)
        {
            foreach (var otherGuid in aData.relatedClueGuids)
                EnsureLineWithStyle(guid, otherGuid);
        }

        // B) other discovered clues list this as related
        foreach (var kv in nodes)
        {
            var otherData = kv.Value.Data;
            if (otherData == null || otherData.relatedClueGuids == null) continue;
            if (otherData.relatedClueGuids.Contains(guid))
                EnsureLineWithStyle(kv.Key, guid);
        }
    }

    private void RebuildAllAutoLinks()
    {
        // Clear existing lines
        foreach (var l in lines.Values)
            if (l) Destroy(l.gameObject);
        lines.Clear();

        // Forward declarations
        foreach (var a in nodes)
        {
            var aData = a.Value.Data;
            if (aData?.relatedClueGuids == null) continue;
            foreach (var bGuid in aData.relatedClueGuids)
                EnsureLineWithStyle(a.Key, bGuid);
        }

        // Reversed-only declarations
        foreach (var b in nodes)
        {
            var bData = b.Value.Data;
            if (bData?.relatedClueGuids == null) continue;
            foreach (var aGuid in bData.relatedClueGuids)
                EnsureLineWithStyle(aGuid, b.Key);
        }
    }

    private (string, string) PairKey(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return default;
        return string.CompareOrdinal(a, b) <= 0 ? (a, b) : (b, a);
    }

    /// <summary>
    /// Ensure a line exists between two clue GUIDs, styled as suggested or confirmed.
    /// Uses each node's LineAnchor (center of the circle) when available.
    /// </summary>
    private void EnsureLineWithStyle(string aGuid, string bGuid)
    {
        if (string.IsNullOrEmpty(aGuid) || string.IsNullOrEmpty(bGuid)) return;
        if (aGuid == bGuid) return;
        if (!nodes.ContainsKey(aGuid) || !nodes.ContainsKey(bGuid)) return;

        bool confirmed = SaveSystem.Instance != null && SaveSystem.Instance.IsLinkConfirmed(aGuid, bGuid);

        var key = PairKey(aGuid, bGuid);
        if (lines.TryGetValue(key, out var existing))
        {
            // If the link just became confirmed, rebuild with confirmed style.
            if (confirmed)
            {
                if (existing) Destroy(existing.gameObject);
                lines.Remove(key);
            }
            else
            {
                return; // already present as suggested
            }
        }

        if (!linePrefab)
        {
            Debug.LogWarning("[CognitionBoard] No linePrefab assigned; cannot draw auto-links.");
            return;
        }

        // Anchor at the circle center when possible
        var aNode = nodes[key.Item1];
        var bNode = nodes[key.Item2];

        var aAnchor = aNode.LineAnchor ? aNode.LineAnchor : aNode.Rect;
        var bAnchor = bNode.LineAnchor ? bNode.LineAnchor : bNode.Rect;

        var line = Instantiate(linePrefab, linesLayer);
        if (confirmed)
            line.Initialize(aAnchor, bAnchor, confirmedColor, confirmedWidth);
        else
            line.Initialize(aAnchor, bAnchor, suggestedColor, suggestedWidth);

        lines[key] = line;
    }

    // Optional public helper if you confirm a link at runtime elsewhere:
    public void PromoteLinkToConfirmed(string aGuid, string bGuid)
    {
        EnsureLineWithStyle(aGuid, bGuid); // rebuilds line with confirmed style if needed
    }

    // Convenience wrappers (used by ClueManager or elsewhere)
    public void AddSuggestedConnectionsFor(string guid)   => BuildAutoLinksTouching(guid);
    public void BuildAllAutoConnections()                 => RebuildAllAutoLinks();
    public void RestoreConnectionsFromSave()              => RebuildAllAutoLinks();
}
