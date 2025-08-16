using UnityEngine;
using System.Collections.Generic;
using UnityEngine.InputSystem;

public class CognitionBoard : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private RectTransform contentRect;
    [SerializeField] private ClueNode nodePrefab;
    [SerializeField] private ConnectionLineUI linePrefab;

    [Header("Line Style")]
    [SerializeField] private Color suggestedColor = new(0.90f, 0.25f, 0.25f, 1f);
    [SerializeField] private float suggestedWidth = 4f;
    [SerializeField] private Color confirmedColor = new(0.20f, 0.90f, 0.35f, 1f);
    [SerializeField] private float confirmedWidth = 5.5f;

    [Header("Reveal (play on open)")]
    [SerializeField] private bool playRevealOnOpen = true;
    [SerializeField] private float revealDurationPerLine = 0.35f;
    [SerializeField] private float revealStagger = 0.10f;
    [SerializeField] private float nodePopDuration = 0.22f; // NEW
    [SerializeField] private float nodePopStagger = 0.05f; // NEW

    [Header("Navigation (UI map)")]
    [SerializeField] private InputActionReference navigateAction;
    [SerializeField] private InputActionReference submitAction;

    [Header("Navigation Tuning")]
    [SerializeField] private float navDeadZone = 0.5f;
    [SerializeField] private float navFirstDelay = 0.25f;   // (kept for future)
    [SerializeField] private float navRepeat = 0.15f;
    [SerializeField] private float dirConeDegrees = 70f;
    [SerializeField] private float minHopDistance = 30f;
    [SerializeField] private float selectedScale = 1.08f;
    [SerializeField] private bool centerOnSubmit = true;

    [Header("Viewport & Placement")]
    [SerializeField] private RectTransform viewportRect;     // assign the visible viewport (parent mask/scroll area)
    [SerializeField] private float minNodeSpacing = 140f;     // minimum distance between any two nodes (in content local space)
    [SerializeField] private float relatedMaxDistance = 420f; // when a node has existing related nodes, spawn no further than this from their centroid
    [SerializeField] private float spawnRadiusStart = 80f;    // starting search radius for new nodes
    [SerializeField] private float spawnRadiusStep = 60f;    // how much we expand the ring each iteration
    [SerializeField] private float spawnRadiusMax = 1200f;  // hard cap so we don’t spin forever

    [Header("Zoom")]
    [SerializeField, Range(0.25f, 3f)] private float minZoom = 0.5f;
    [SerializeField, Range(0.25f, 3f)] private float maxZoom = 2.0f;
    [SerializeField] private float zoomStep = 0.1f;

    public float CurrentZoom => ContentRect ? ContentRect.localScale.x : 1f;

    public void ZoomDelta(float delta)
    {
        if (!ContentRect) return;
        float z = Mathf.Clamp(CurrentZoom + delta, minZoom, maxZoom);
        ContentRect.localScale = Vector3.one * z;
        SaveSystem.Instance?.SetBoardZoom(z);
    }

    public void SetZoom(float zoom)
    {
        if (!ContentRect) return;
        float z = Mathf.Clamp(zoom, minZoom, maxZoom);
        ContentRect.localScale = Vector3.one * z;
        SaveSystem.Instance?.SetBoardZoom(z);
    }



    private readonly List<(string, string)> pendingLineReveals = new(); // ORDERED
    private readonly HashSet<(string, string)> pendingLineSet = new();  // uniqueness
    private readonly List<string> pendingNodePops = new();              // ORDERED

    public RectTransform ContentRect => contentRect ? contentRect : (RectTransform)transform;

    private readonly Dictionary<string, ClueNode> nodes = new();
    private readonly Dictionary<(string, string), ConnectionLineUI> lines = new();

    private RectTransform linesLayer;
    private RectTransform nodesLayer;

    // selection
    private string selectedGuid;
    private ClueNode selectedNode;
    private Vector2 lastNavDir;
    private float nextNavTime;
    private bool navHeld;
    private float cosCone;

    private void Awake()
    {
        gameObject.SetActive(false);
        if (!contentRect) contentRect = GetComponent<RectTransform>();
        if (!contentRect) Debug.LogError("[CognitionBoard] No RectTransform for content.", this);
        EnsureLayers();
        cosCone = Mathf.Cos(dirConeDegrees * Mathf.Deg2Rad);
    }

    private void OnEnable()
    {
        try { navigateAction?.action?.Enable(); } catch { }
        try { submitAction?.action?.Enable(); } catch { }
        navHeld = false; nextNavTime = 0f;
        if (selectedNode == null) AutoSelectClosestToCenter();
    }

    private void OnDisable()
    {
        if (selectedNode) selectedNode.Rect.localScale = Vector3.one;
        selectedNode = null;
        selectedGuid = null;
        navHeld = false; nextNavTime = 0f;
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
            t.anchorMin = Vector2.zero; t.anchorMax = Vector2.one;
            t.offsetMin = Vector2.zero; t.offsetMax = Vector2.zero;
        }
        t.SetSiblingIndex(siblingIndex);
        return t;
    }

    // ---------- Public API ----------
    public void AddNode(ClueData data)
    {
        if (!data) { Debug.LogError("[CognitionBoard] AddNode null data"); return; }
        if (nodes.ContainsKey(data.Guid)) { BuildAutoLinksTouching(data.Guid); return; }

        ClueNode node;
        if (nodePrefab) node = Instantiate(nodePrefab, nodesLayer);
        else
        {
            var go = new GameObject($"ClueNode_{data.clueName}", typeof(RectTransform), typeof(ClueNode));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(nodesLayer, false);
            node = go.GetComponent<ClueNode>();
        }

        node.Initialize(this, data);

        // --- NEW: initial placement ---
        // If we already have a saved position, do nothing; SaveSystem restore will place it later.
        // Otherwise, pick a sensible spot near either the related centroid or the view center,
        // enforcing min spacing and (for related) a max distance constraint.
        bool hasSaved = false;
        try
        {
            // If you have a SaveSystem query for node positions, call it here.
            // Example pattern:
            // hasSaved = SaveSystem.Instance?.TryGetNodePosition(data.Guid, out var _) == true;
        }
        catch { /* ignore */ }

        if (!hasSaved && node.Rect && ContentRect)
        {
            Vector2 seed = GetViewCenterLocal();

            if (TryGetExistingRelatedCentroid(data, out var centroid))
            {
                // Start near the related centroid, but cap distance
                Vector2 start = ClampWithinRelatedMax(seed, centroid, relatedMaxDistance);
                node.Rect.anchoredPosition = FindFreeSpot(start, minNodeSpacing);
            }
            else
            {
                // No known neighbors — spawn near the current view center
                node.Rect.anchoredPosition = FindFreeSpot(seed, minNodeSpacing);
            }
        }



        nodes.Add(data.Guid, node);

        // Queue pop if board is closed (so it animates next open)
        if (!gameObject.activeInHierarchy && playRevealOnOpen)
        {
            node.Rect.localScale = Vector3.zero;
            if (!pendingNodePops.Contains(data.Guid))
                pendingNodePops.Add(data.Guid);
        }

        BuildAutoLinksTouching(data.Guid);

        if (isActiveAndEnabled && selectedNode == null)
            SelectNodeInternal(node);
    }

    public void BeginNodeDrag(ClueNode _) { }
    public void OnNodeMoved(ClueNode _) { }
    public void EndNodeDrag(ClueNode node)
    {
        SaveSystem.Instance?.SetNodePosition(node.ClueGuid, node.Rect.anchoredPosition);
    }

    // restore pan/zoom/positions (leave lines for reveal system)
    public void RestoreLayoutFromSave(bool rebuildLines = false)
    {
        var layout = SaveSystem.Instance?.GetBoardLayout();
        if (layout == null) return;

        ContentRect.localScale = Vector3.one * layout.zoom;
        ContentRect.anchoredPosition = layout.pan;

        foreach (var kvp in layout.nodePositions)
            if (nodes.TryGetValue(kvp.Key, out var node))
                node.Rect.anchoredPosition = kvp.Value;

        if (rebuildLines) RebuildAllAutoLinks();
        AutoSelectClosestToCenter();
    }

    // Called by PauseMenuController right after opening the board
    public void NotifyBoardOpened()
    {
        if (!playRevealOnOpen) { pendingNodePops.Clear(); pendingLineReveals.Clear(); pendingLineSet.Clear(); return; }
        if (!gameObject.activeInHierarchy) return;
        if (pendingNodePops.Count == 0 && pendingLineReveals.Count == 0) return;

        StopAllCoroutines();
        StartCoroutine(PlayOpenRevealSequence());
    }

    // ---------- Update (navigation) ----------
    private void Update()
    {
        if (!isActiveAndEnabled || nodes.Count == 0) return;

        Vector2 dir = Vector2.zero;
        try { dir = navigateAction ? navigateAction.action.ReadValue<Vector2>() : Vector2.zero; } catch { }
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
                nextNavTime = now + (moved ? navRepeat : navRepeat * 0.5f);
            }
        }
        else { navHeld = false; nextNavTime = 0f; }

        bool submitTriggered = false;
        try { submitTriggered = submitAction && submitAction.action.triggered; } catch { }
        if (submitTriggered && selectedNode && centerOnSubmit)
            CenterOnNode(selectedNode);
    }

    // ---------- Selection helpers ----------
    private void AutoSelectClosestToCenter()
    {
        if (nodes.Count == 0) { ClearSelection(); return; }
        float best = float.MaxValue; ClueNode bestNode = null;
        foreach (var kv in nodes)
        {
            var n = kv.Value; if (!n || !n.Rect) continue;
            float d2 = n.Rect.anchoredPosition.sqrMagnitude;
            if (d2 < best) { best = d2; bestNode = n; }
        }
        if (bestNode) SelectNodeInternal(bestNode);
    }
    private void SelectNodeInternal(ClueNode node)
    {
        if (selectedNode == node) return;
        if (selectedNode) selectedNode.Rect.localScale = Vector3.one;
        selectedNode = node; selectedGuid = node ? node.ClueGuid : null;
        if (selectedNode)
        {
            selectedNode.Rect.localScale = Vector3.one * selectedScale;
            selectedNode.Rect.SetAsLastSibling();
        }
    }
    private void ClearSelection()
    {
        if (selectedNode) selectedNode.Rect.localScale = Vector3.one;
        selectedNode = null; selectedGuid = null;
    }
    private bool MoveSelectionInDirection(Vector2 dir)
    {
        if (!selectedNode) { AutoSelectClosestToCenter(); return selectedNode != null; }

        var from = selectedNode.Rect.anchoredPosition;
        string bestGuid = null;
        float bestScore = float.MaxValue;
        foreach (var kv in nodes)
        {
            var n = kv.Value; if (!n || n == selectedNode) continue;
            Vector2 to = n.Rect.anchoredPosition - from;
            float dist = to.magnitude; if (dist < minHopDistance) continue;
            Vector2 nd = to / dist;
            float dot = Vector2.Dot(nd, dir);
            if (dot < cosCone) continue;
            float angleCost = 1f - dot;
            float distCost = dist * 0.0015f;
            float score = angleCost * 1.25f + distCost;
            if (score < bestScore) { bestScore = score; bestGuid = kv.Key; }
        }
        if (bestGuid != null && nodes.TryGetValue(bestGuid, out var bestNode))
        { SelectNodeInternal(bestNode); return true; }
        return false;
    }
    private void CenterOnNode(ClueNode node)
    {
        if (!node || !node.Rect || !ContentRect) return;
        ContentRect.anchoredPosition = -node.Rect.anchoredPosition;
        SaveSystem.Instance?.SetBoardPan(ContentRect.anchoredPosition);
    }

    // ---------- Auto-link logic ----------
    private void BuildAutoLinksTouching(string guid)
    {
        if (!nodes.ContainsKey(guid)) return;

        var aData = nodes[guid].Data;
        if (aData?.relatedClueGuids != null)
            foreach (var otherGuid in aData.relatedClueGuids)
                EnsureLineWithStyle(guid, otherGuid);

        foreach (var kv in nodes)
        {
            var otherData = kv.Value.Data;
            if (otherData?.relatedClueGuids == null) continue;
            if (otherData.relatedClueGuids.Contains(guid))
                EnsureLineWithStyle(kv.Key, guid);
        }
    }

    private void RebuildAllAutoLinks()
    {
        foreach (var l in lines.Values) if (l) Destroy(l.gameObject);
        lines.Clear();

        foreach (var a in nodes)
        {
            var aData = a.Value.Data;
            if (aData?.relatedClueGuids == null) continue;
            foreach (var bGuid in aData.relatedClueGuids)
                EnsureLineWithStyle(a.Key, bGuid);
        }
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

    private void EnsureLineWithStyle(string aGuid, string bGuid)
    {
        if (string.IsNullOrEmpty(aGuid) || string.IsNullOrEmpty(bGuid)) return;
        if (aGuid == bGuid) return;
        if (!nodes.ContainsKey(aGuid) || !nodes.ContainsKey(bGuid)) return;

        bool confirmed = SaveSystem.Instance != null && SaveSystem.Instance.IsLinkConfirmed(aGuid, bGuid);

        var key = PairKey(aGuid, bGuid);
        if (lines.TryGetValue(key, out var existing))
        {
            if (confirmed) { if (existing) Destroy(existing.gameObject); lines.Remove(key); }
            else return;
        }

        if (!linePrefab) { Debug.LogWarning("[CognitionBoard] No linePrefab assigned; cannot draw auto-links."); return; }

        var aNode = nodes[key.Item1];
        var bNode = nodes[key.Item2];
        var aAnchor = aNode.LineAnchor ? aNode.LineAnchor : aNode.Rect;
        var bAnchor = bNode.LineAnchor ? bNode.LineAnchor : bNode.Rect;

        var line = Instantiate(linePrefab, linesLayer);
        if (confirmed) line.Initialize(aAnchor, bAnchor, confirmedColor, confirmedWidth);
        else line.Initialize(aAnchor, bAnchor, suggestedColor, suggestedWidth);

        // Decide which end to grow from based on discovery order (older -> newer)
        int ia = SaveSystem.Instance ? SaveSystem.Instance.GetDiscoveryIndex(key.Item1) : int.MaxValue;
        int ib = SaveSystem.Instance ? SaveSystem.Instance.GetDiscoveryIndex(key.Item2) : int.MaxValue;
        bool fromA = ia <= ib;
        line.SetGrowFrom(fromA);

        if (!gameObject.activeInHierarchy && playRevealOnOpen)
        {
            line.SetReveal(0f);
            if (pendingLineSet.Add(key))
                pendingLineReveals.Add(key); // keep append order
        }
        else
        {
            line.SetReveal(1f);
        }

        lines[key] = line;
    }

    // Convenience wrappers (if other code wants explicit calls)
    public void AddSuggestedConnectionsFor(string guid) => BuildAutoLinksTouching(guid);
    public void BuildAllAutoConnections() => RebuildAllAutoLinks();
    public void RestoreConnectionsFromSave() => RebuildAllAutoLinks();

    // ---------- Reveal sequence on open ----------
    private System.Collections.IEnumerator PlayOpenRevealSequence()
    {
        // 1) Pop nodes in discovery order
        var order = SaveSystem.Instance ? SaveSystem.Instance.GetDiscoveryOrder() : null;
        if (order != null && order.Count > 0 && pendingNodePops.Count > 0)
        {
            for (int i = 0; i < order.Count; i++)
            {
                string guid = order[i];
                if (!pendingNodePops.Contains(guid)) continue;
                if (!nodes.TryGetValue(guid, out var node) || !node || !node.Rect) continue;

                // Animate scale 0 -> 1
                float t = 0f;
                while (t < 1f)
                {
                    t += Time.unscaledDeltaTime / Mathf.Max(0.01f, nodePopDuration);
                    float s = Mathf.SmoothStep(0f, 1f, t);
                    node.Rect.localScale = new Vector3(s, s, 1f);
                    yield return null;
                }
                node.Rect.localScale = Vector3.one;

                // small stagger
                float end = Time.unscaledTime + nodePopStagger;
                while (Time.unscaledTime < end) yield return null;
            }
        }
        pendingNodePops.Clear();

        // 2) Reveal lines, oldest->newest (by newer endpoint’s discovery index)
        if (pendingLineReveals.Count > 0)
        {
            // sort by max(discoveryIndexA, discoveryIndexB)
            pendingLineReveals.Sort((p, q) =>
            {
                int pa = SaveSystem.Instance ? SaveSystem.Instance.GetDiscoveryIndex(p.Item1) : int.MaxValue;
                int pb = SaveSystem.Instance ? SaveSystem.Instance.GetDiscoveryIndex(p.Item2) : int.MaxValue;
                int qa = SaveSystem.Instance ? SaveSystem.Instance.GetDiscoveryIndex(q.Item1) : int.MaxValue;
                int qb = SaveSystem.Instance ? SaveSystem.Instance.GetDiscoveryIndex(q.Item2) : int.MaxValue;

                int pKey = Mathf.Max(pa, pb);
                int qKey = Mathf.Max(qa, qb);
                return pKey.CompareTo(qKey);
            });

            foreach (var key in pendingLineReveals)
            {
                if (!lines.TryGetValue(key, out var line) || line == null) continue;

                float t = 0f;
                while (t < 1f)
                {
                    t += Time.unscaledDeltaTime / Mathf.Max(0.01f, revealDurationPerLine);
                    line.SetReveal(Mathf.SmoothStep(0f, 1f, t));
                    yield return null;
                }
                line.SetReveal(1f);

                float end = Time.unscaledTime + revealStagger;
                while (Time.unscaledTime < end) yield return null;
            }
        }

        pendingLineReveals.Clear();
        pendingLineSet.Clear();
    }

    public void ClearAll()
    {
        // Destroy nodes
        foreach (var kv in nodes)
            if (kv.Value) Destroy(kv.Value.gameObject);
        nodes.Clear();

        // Destroy lines
        foreach (var kv in lines)
            if (kv.Value) Destroy(kv.Value.gameObject);
        lines.Clear();

        // Drop any queued reveals
        // (rename or clear whatever collections you use)
        // e.g.:
        // pendingLineReveals.Clear();
        // pendingLineSet.Clear();
        // pendingNodePops.Clear();

        // Reset pan/zoom
        if (ContentRect)
        {
            ContentRect.localScale = Vector3.one;
            ContentRect.anchoredPosition = Vector2.zero;
        }

        // Persist the reset layout
        SaveSystem.Instance?.SetBoardZoom(1f);
        SaveSystem.Instance?.SetBoardPan(Vector2.zero);
    }

    private Vector2 GetViewCenterLocal()
    {
        // If we know the viewport, convert its center to content-local space.
        if (viewportRect && ContentRect && viewportRect.gameObject.activeInHierarchy)
        {
            var vpWorld = viewportRect.TransformPoint(viewportRect.rect.center);
            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                ContentRect, RectTransformUtility.WorldToScreenPoint(null, vpWorld), null, out local);
            return local;
        }

        // Fallback: content anchoredPosition is the pan; view center ≈ -pan
        return -ContentRect.anchoredPosition;
    }

    private bool IsFarEnoughFromOthers(Vector2 p, float minDist)
    {
        float minSqr = minDist * minDist;
        foreach (var kv in nodes)
        {
            var n = kv.Value;
            if (!n || !n.Rect) continue;
            var d = (n.Rect.anchoredPosition - p).sqrMagnitude;
            if (d < minSqr) return false;
        }
        return true;
    }

    private Vector2 FindFreeSpot(Vector2 seed, float minDist)
    {
        // Try rings around seed; sample a few angles per ring.
        float r = Mathf.Max(0f, spawnRadiusStart);
        var rand = new System.Random((int)(Time.realtimeSinceStartup * 1000f));

        while (r <= spawnRadiusMax)
        {
            int samples = Mathf.Clamp(Mathf.CeilToInt(2f * Mathf.PI * r / Mathf.Max(1f, minDist)), 8, 48);
            for (int i = 0; i < samples; i++)
            {
                // jitter the angle a bit so identical radii don't pick identical points
                float t = (i + (float)rand.NextDouble() * 0.35f) / samples;
                float ang = t * Mathf.PI * 2f;
                var p = seed + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
                if (IsFarEnoughFromOthers(p, minDist)) return p;
            }
            r += Mathf.Max(8f, spawnRadiusStep);
        }

        // If we didn’t find anything, just drop at seed.
        return seed;
    }

    private bool TryGetExistingRelatedCentroid(ClueData data, out Vector2 centroid)
    {
        centroid = Vector2.zero;
        if (data?.relatedClueGuids == null || data.relatedClueGuids.Count == 0) return false;

        int count = 0;
        foreach (var gid in data.relatedClueGuids)
        {
            if (string.IsNullOrEmpty(gid)) continue;
            if (nodes.TryGetValue(gid, out var n) && n && n.Rect)
            {
                centroid += n.Rect.anchoredPosition;
                count++;
            }
        }
        if (count == 0) return false;
        centroid /= count;
        return true;
    }

    private Vector2 ClampWithinRelatedMax(Vector2 desired, Vector2 centroid, float maxDist)
    {
        var delta = desired - centroid;
        float d = delta.magnitude;
        if (d <= maxDist || d <= Mathf.Epsilon) return desired;
        return centroid + delta / d * maxDist;
    }


}
