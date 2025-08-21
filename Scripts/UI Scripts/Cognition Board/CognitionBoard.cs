using UnityEngine;
using System.Collections.Generic;
using UnityEngine.InputSystem;

public class CognitionBoard : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private RectTransform contentRect;
    [SerializeField] private ClueNode nodePrefab;
    [SerializeField] private ConnectionLineUI linePrefab;

    [Header("Clue Info Panel")]
    [Tooltip("Optional. If assigned, shows clue name/description when in hard focus.")]
    [SerializeField] private ClueInfoPanel infoPanel;
    [Tooltip("If true, the info panel is only visible when not in Freelook.")]
    [SerializeField] private bool showInfoOnlyWhenHardFocus = true;

    [Header("Line Style")]
    [SerializeField] private Color suggestedColor = new(0.90f, 0.25f, 0.25f, 1f);
    [SerializeField] private float suggestedWidth = 4f;
    [SerializeField] private Color confirmedColor = new(0.20f, 0.90f, 0.35f, 1f);
    [SerializeField] private float confirmedWidth = 5.5f;

    // ─────────────────────────────────────────────────────────────────────
    // Cutscene tuning
    [Header("New Node Cutscene")]
    [SerializeField] private bool useCutsceneForNewNodes = true;
    [SerializeField] private float defaultZoom = 1.0f;
    [SerializeField] private float cutsceneZoom = 2.2f;
    [SerializeField] private float cutsceneInZoomSeconds = 0.25f;
    [SerializeField] private float cutsceneOutZoomSeconds = 0.30f;
    [SerializeField] private float panelDelayAfterPop = 0.10f;
    [SerializeField] private float minAdvanceDelayAfterType = 0.50f;
    [SerializeField] private bool  lockInputsDuringCutscene = true;

    [Header("Reveal (play on open)")]
    [SerializeField] private bool playRevealOnOpen = true;
    [SerializeField] private float revealDurationPerLine = 0.35f;
    [SerializeField] private float revealStagger = 0.10f;
    [SerializeField] private float nodePopDuration = 0.22f;
    [SerializeField] private float nodePopStagger = 0.05f;

    [Header("Navigation (UI map)")]
    [SerializeField] private InputActionReference navigateAction; // left stick / WASD
    [SerializeField] private InputActionReference submitAction;   // advance/skip

    [Header("Zoom Input")]
    [SerializeField] private InputActionReference zoomAction;

    // -------------------- FREELOOK --------------------
    [Header("Freelook (right stick)")]
    [SerializeField] private InputActionReference freeLookAction;
    [SerializeField] private float freeLookDeadZone = 0.25f;
    [SerializeField] private bool invertFreelookY = false;

    [Header("Freelook Speed Ramp")]
    [SerializeField] private float panSpeedInitial = 700f;
    [SerializeField] private float panSpeedRampDuration = 0.5f;
    [SerializeField] private float panSpeedMax = 2200f;

    // -------------------- ZOOM --------------------
    [Header("Zoom")]
    [SerializeField] private float zoomStep = 0.1f;

    [Header("Zoom Rate Ramp")]
    [SerializeField] private float zoomRateInitial = 1.0f;
    [SerializeField] private float zoomRateRampDuration = 0.5f;
    [SerializeField] private float zoomRateMax = 5.0f;

    // -------------------- Selection & Centering --------------------
    [Header("Centering")]
    [SerializeField] private bool keepSelectedCentered = true;

    [Header("Navigation Tuning")]
    [SerializeField] private float navDeadZone = 0.5f;
    [SerializeField] private float navFirstDelay = 0.25f;   // reserved
    [SerializeField] private float navRepeat = 0.15f;
    [SerializeField] private float dirConeDegrees = 70f;
    [SerializeField] private float minHopDistance = 30f;
    [SerializeField] private float selectedScale = 1.08f;
    [SerializeField] private bool centerOnSubmit = true;

    [Header("Viewport & Placement")]
    [SerializeField] private RectTransform viewportRect;
    [SerializeField] private float minNodeSpacing = 140f;
    [SerializeField] private float relatedMaxDistance = 420f;
    [SerializeField] private float spawnRadiusStart = 80f;
    [SerializeField] private float spawnRadiusStep = 60f;
    [SerializeField] private float spawnRadiusMax = 1200f;

    [SerializeField] private BoardCamera boardCamera;

    public RectTransform ContentRect => contentRect ? contentRect : (RectTransform)transform;
    public float CurrentZoom => ContentRect ? ContentRect.localScale.x : 1f;

    private readonly List<(string, string)> pendingLineReveals = new();
    private readonly HashSet<(string, string)> pendingLineSet = new();
    private readonly List<string> pendingNodePops = new();

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

    // state
    private bool inFreelook;
    private float freelookHeldTime;
    private float zoomHeldTime;

    // Input gate for cutscene
    private bool cutsceneActive = false;

    // Prevent double-start (OnEnable + NotifyBoardOpened)
    private bool cutsceneScheduled = false;
    private bool cutsceneRunning   = false;

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
        try { zoomAction?.action?.Enable(); } catch { }
        try { freeLookAction?.action?.Enable(); } catch { }

        navHeld = false; nextNavTime = 0f;
        inFreelook = false;
        freelookHeldTime = 0f;
        zoomHeldTime = 0f;

        bool willCutscene = useCutsceneForNewNodes && HasPendingReveals();

        if (willCutscene && !cutsceneScheduled && !cutsceneRunning)
        {
            cutsceneScheduled = true;
            cutsceneActive = true;
            infoPanel?.Hide(true);

            var ordered = BuildOrderedNewNodes(); // filters to truly-new nodes

            if (ordered.Count > 0)
            {
                var prime = ordered[^1];           // most recent pending node (what the player just found)
                SelectNodeInternal(prime);

                // SNAP focus now and persist pan to defeat any external restore-on-open.
                if (boardCamera && prime && prime.Rect)
                {
                    boardCamera.FocusOn(prime.Rect, immediate: true);
                }
                // Persist the pan & (current) zoom so any “restore layout” uses this.
                SaveSystem.Instance?.SetBoardPan(ContentRect.anchoredPosition);
                SaveSystem.Instance?.SetBoardZoom(boardCamera ? boardCamera.CurrentZoom : CurrentZoom);

                Debug.Log($"[CognitionBoard] OnEnable: preselect first new node '{prime.Data?.clueName}'.", this);
                Debug.Log($"[CognitionBoard] Precenter persisted. Pan={ContentRect.anchoredPosition} Zoom={(boardCamera? boardCamera.CurrentZoom : CurrentZoom):0.00}", this);
            }
            else
            {
                Debug.Log("[CognitionBoard] OnEnable: no filtered new nodes (nothing to reveal).", this);
            }

            StopAllCoroutines();
            StartCoroutine(PlayDiscoveryCutscene(ordered));
            return;
        }

        // No cutscene path
        if (selectedNode == null) AutoSelectClosestToCenter();
        else if (keepSelectedCentered && boardCamera && selectedNode)
            boardCamera.FocusOn(selectedNode.Rect, immediate: false);

        if (selectedNode) selectedNode.SetSelected(true);

        if (infoPanel && selectedNode && (!showInfoOnlyWhenHardFocus || !inFreelook))
            infoPanel.ShowFor(selectedNode.Data, immediate: true);

        if (playRevealOnOpen && HasPendingReveals())
        {
            StopAllCoroutines();
            StartCoroutine(PlayOpenRevealSequence());
        }
    }

    private void OnDisable()
    {
        if (selectedNode)
        {
            selectedNode.SetSelected(false);
            selectedNode.Rect.localScale = Vector3.one;
        }
        selectedNode = null;
        selectedGuid = null;
        navHeld = false; nextNavTime = 0f;

        try { zoomAction?.action?.Disable(); } catch { }
        try { freeLookAction?.action?.Disable(); } catch { }

        infoPanel?.Hide(true);

        // reset guards
        cutsceneActive   = false;
        cutsceneScheduled = false;
        cutsceneRunning   = false;
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

        if (!nodesLayer) EnsureLayers();

        ClueNode node;
        if (nodePrefab)
        {
            node = Instantiate(nodePrefab, nodesLayer);
            if (node.Rect)
            {
                var rt = node.Rect;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.localScale = Vector3.one;
                rt.localRotation = Quaternion.identity;
            }
        }
        else
        {
            var go = new GameObject($"ClueNode_{data.clueName}", typeof(RectTransform), typeof(ClueNode));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(nodesLayer, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
            node = go.GetComponent<ClueNode>();
        }

        node.Initialize(this, data);

        bool hasSaved = false;
        try { /* hasSaved = SaveSystem.Instance?.TryGetNodePosition(data.Guid, out var _) == true; */ } catch { }

        if (!hasSaved && node.Rect && ContentRect)
        {
            Vector2 seed = GetViewCenterLocal();

            if (TryGetExistingRelatedCentroid(data, out var centroid))
            {
                Vector2 start = ClampWithinRelatedMax(seed, centroid, relatedMaxDistance);
                node.Rect.anchoredPosition = FindFreeSpot(start, minNodeSpacing);
            }
            else
            {
                node.Rect.anchoredPosition = FindFreeSpot(seed, minNodeSpacing);
            }
        }

        nodes.Add(data.Guid, node);

        // Hide new nodes until reveal
        node.Rect.localScale = Vector3.zero;

        if (!pendingNodePops.Contains(data.Guid))
            pendingNodePops.Add(data.Guid);

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

        if (selectedNode) selectedNode.SetSelected(true);
    }

    public void NotifyBoardOpened()
    {
        if (!gameObject.activeInHierarchy) return;

        if (useCutsceneForNewNodes && HasPendingReveals())
        {
            if (!cutsceneScheduled && !cutsceneRunning)
            {
                cutsceneScheduled = true;
                cutsceneActive = true;
                infoPanel?.Hide(true);

                var ordered = BuildOrderedNewNodes();
                if (ordered.Count > 0)
                {
                    var prime = ordered[^1];
                    SelectNodeInternal(prime);

                    // same pre-center+persist here in case this is the only entry point
                    if (boardCamera && prime && prime.Rect)
                        boardCamera.FocusOn(prime.Rect, immediate: true);

                    SaveSystem.Instance?.SetBoardPan(ContentRect.anchoredPosition);
                    SaveSystem.Instance?.SetBoardZoom(boardCamera ? boardCamera.CurrentZoom : CurrentZoom);

                    Debug.Log($"[CognitionBoard] NotifyBoardOpened: preselect '{prime.Data?.clueName}' and persisted pan {ContentRect.anchoredPosition}.", this);
                }

                StopAllCoroutines();
                StartCoroutine(PlayDiscoveryCutscene(ordered));
            }
            return;
        }

        if (!playRevealOnOpen) { pendingNodePops.Clear(); pendingLineReveals.Clear(); pendingLineSet.Clear(); return; }
        if (HasPendingReveals())
        {
            StopAllCoroutines();
            StartCoroutine(PlayOpenRevealSequence());
        }
    }

    private bool HasPendingReveals() => pendingNodePops.Count > 0 || pendingLineReveals.Count > 0;

    // ---------- Update (navigation + freelook + zoom) ----------
    private void Update()
    {
        if (cutsceneActive) return; // inputs gated during cutscene

        if (!isActiveAndEnabled || nodes.Count == 0) return;

        float dt = Time.unscaledDeltaTime;
        float now = Time.unscaledTime;

        // --- FREELook (right stick) ----------------------------------------
        Vector2 look = Vector2.zero;
        try { look = freeLookAction ? freeLookAction.action.ReadValue<Vector2>() : Vector2.zero; } catch { }
        float lookMag = look.magnitude;

        if (lookMag > freeLookDeadZone)
        {
            if (invertFreelookY) look.y = -look.y;

            if (!inFreelook)
            {
                inFreelook = true;
                if (infoPanel && showInfoOnlyWhenHardFocus) infoPanel.Hide(false);
            }

            freelookHeldTime += dt;

            float speedNow = ExpoRamp(panSpeedInitial, panSpeedMax, freelookHeldTime, panSpeedRampDuration);
            Vector2 contentDelta = (look.normalized * speedNow * dt) / Mathf.Max(0.01f, CurrentZoom);

            if (boardCamera) boardCamera.Nudge(contentDelta);
            else
            {
                ContentRect.anchoredPosition += contentDelta;
                SaveSystem.Instance?.SetBoardPan(ContentRect.anchoredPosition);
            }
        }
        else
        {
            freelookHeldTime = 0f;
        }

        // --- Directional selection (left stick / keys) ----------------------
        Vector2 dir = Vector2.zero;
        try { dir = navigateAction ? navigateAction.action.ReadValue<Vector2>() : Vector2.zero; } catch { }
        float mag = dir.magnitude;

        if (mag > navDeadZone)
        {
            if (inFreelook)
            {
                inFreelook = false;
                SelectClosestToViewCenterAndFocus();

                if (infoPanel && selectedNode && showInfoOnlyWhenHardFocus == true)
                    infoPanel.ShowFor(selectedNode.Data, immediate: false);
            }

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
            CenterOnNodeImmediate(selectedNode);

        // --- Zoom input -----------------------------------------------------
        float zDelta = 0f;
        try
        {
            if (zoomAction && zoomAction.action.enabled)
            {
                var controlType = zoomAction.action.expectedControlType;
                if (controlType == "Vector2")
                {
                    Vector2 v = zoomAction.action.ReadValue<Vector2>();
                    if (Mathf.Abs(v.y) > 0.01f) zDelta = Mathf.Sign(v.y);
                }
                else
                {
                    float f = zoomAction.action.ReadValue<float>();
                    if (Mathf.Abs(f) > 0.01f) zDelta = Mathf.Sign(f);
                }
            }
        }
        catch { }

        if (Mathf.Abs(zDelta) > 0f)
        {
            zoomHeldTime += dt;
            ZoomDelta(zDelta);
        }
        else
        {
            zoomHeldTime = 0f;
        }
    }

    // ---------- Zoom helpers ----------
    public void ZoomDelta(float direction)
    {
        if (!ContentRect || boardCamera == null) return;

        float rateNow = ExpoRamp(zoomRateInitial, zoomRateMax, zoomHeldTime, zoomRateRampDuration);
        float magnitude = Mathf.Max(0f, zoomStep * rateNow * Time.unscaledDeltaTime);
        if (magnitude <= Mathf.Epsilon) return;

        float factor = (direction > 0f) ? (1f + magnitude) : (1f / (1f + magnitude));

        if (!inFreelook && keepSelectedCentered && selectedNode && viewportRect)
        {
            Vector2 pivotInViewport =
                viewportRect.InverseTransformPoint(
                    selectedNode.Rect.TransformPoint(selectedNode.Rect.rect.center));

            boardCamera.ZoomAround(factor, pivotInViewport);
            boardCamera.FocusOn(selectedNode.Rect, immediate: false);
        }
        else
        {
            boardCamera.ZoomAroundCenter(factor);
        }
    }

    public void SetZoom(float absoluteZoom)
    {
        if (!ContentRect) return;
        ContentRect.localScale = Vector3.one * Mathf.Max(0.01f, absoluteZoom);
        SaveSystem.Instance?.SetBoardZoom(ContentRect.localScale.x);
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

    private void SelectClosestToViewCenterAndFocus()
    {
        AutoSelectClosestToCenter();
        if (selectedNode)
            boardCamera?.FocusOn(selectedNode.Rect, immediate: false);
    }

    private void SelectNodeInternal(ClueNode node)
    {
        if (selectedNode == node) return;

        if (selectedNode)
        {
            selectedNode.SetSelected(false);
            selectedNode.Rect.localScale = Vector3.one;
        }

        selectedNode = node;
        selectedGuid = node ? node.ClueGuid : null;

        if (selectedNode)
        {
            selectedNode.SetSelected(true);
            selectedNode.Rect.localScale = Vector3.one * selectedScale;
            selectedNode.Rect.SetAsLastSibling();
        }

        OnSelectedNodeChanged(selectedNode);

        if (!cutsceneActive && infoPanel && selectedNode && (!showInfoOnlyWhenHardFocus || !inFreelook))
            infoPanel.ShowFor(selectedNode.Data, immediate: false);
    }

    void OnSelectedNodeChanged(ClueNode node)
    {
        boardCamera?.FocusOn(node?.Rect, immediate: false);
    }

    private void ClearSelection()
    {
        if (selectedNode)
        {
            selectedNode.SetSelected(false);
            selectedNode.Rect.localScale = Vector3.one;
        }
        selectedNode = null; selectedGuid = null;
        if (!cutsceneActive) infoPanel?.Hide(false);
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
        {
            SelectNodeInternal(bestNode);
            return true;
        }
        return false;
    }

    private void CenterOnNodeImmediate(ClueNode node)
    {
        if (!node || !node.Rect) return;

        if (boardCamera)
        {
            boardCamera.FocusOn(node.Rect, immediate: true);
        }
        else
        {
            if (!ContentRect) return;
            ContentRect.anchoredPosition = -node.Rect.anchoredPosition;
            SaveSystem.Instance?.SetBoardPan(ContentRect.anchoredPosition);
        }

        if (!cutsceneActive && infoPanel && node && (!showInfoOnlyWhenHardFocus || !inFreelook))
            infoPanel.ShowFor(node.Data, immediate: true);
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

        int ia = SaveSystem.Instance ? SaveSystem.Instance.GetDiscoveryIndex(key.Item1) : int.MaxValue;
        int ib = SaveSystem.Instance ? SaveSystem.Instance.GetDiscoveryIndex(key.Item2) : int.MaxValue;
        bool fromA = ia <= ib;
        line.SetGrowFrom(fromA);

        line.SetReveal(0f);
        if (pendingLineSet.Add(key))
            pendingLineReveals.Add(key);

        lines[key] = line;
    }

    public void AddSuggestedConnectionsFor(string guid) => BuildAutoLinksTouching(guid);
    public void BuildAllAutoConnections() => RebuildAllAutoLinks();
    public void RestoreConnectionsFromSave() => RebuildAllAutoLinks();

    // ---------- Reveal sequence on open (legacy) ----------
    private System.Collections.IEnumerator PlayOpenRevealSequence()
    {
        var order = SaveSystem.Instance ? SaveSystem.Instance.GetDiscoveryOrder() : null;
        if (order != null && order.Count > 0 && pendingNodePops.Count > 0)
        {
            for (int i = 0; i < order.Count; i++)
            {
                string guid = order[i];
                if (!pendingNodePops.Contains(guid)) continue;
                if (!nodes.TryGetValue(guid, out var node) || !node || !node.Rect) continue;

                float t = 0f;
                while (t < 1f)
                {
                    t += Time.unscaledDeltaTime / Mathf.Max(0.01f, nodePopDuration);
                    float s = Mathf.SmoothStep(0f, 1f, t);
                    node.Rect.localScale = new Vector3(s, s, 1f);
                    yield return null;
                }
                node.Rect.localScale = Vector3.one;

                float end = Time.unscaledTime + nodePopStagger;
                while (Time.unscaledTime < end) yield return null;
            }
        }
        pendingNodePops.Clear();

        if (pendingLineReveals.Count > 0)
        {
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
        foreach (var kv in nodes)
            if (kv.Value) Destroy(kv.Value.gameObject);
        nodes.Clear();

        foreach (var kv in lines)
            if (kv.Value) Destroy(kv.Value.gameObject);
        lines.Clear();

        if (ContentRect)
        {
            ContentRect.localScale = Vector3.one;
            ContentRect.anchoredPosition = Vector2.zero;
        }

        SaveSystem.Instance?.SetBoardZoom(1f);
        SaveSystem.Instance?.SetBoardPan(Vector2.zero);
        infoPanel?.Hide(true);
    }

    // ---------- Placement helpers ----------
    private Vector2 GetViewCenterLocal()
    {
        if (viewportRect && ContentRect && viewportRect.gameObject.activeInHierarchy)
        {
            var vpWorld = viewportRect.TransformPoint(viewportRect.rect.center);
            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                ContentRect, RectTransformUtility.WorldToScreenPoint(null, vpWorld), null, out local);
            return local;
        }
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
        float r = Mathf.Max(0f, spawnRadiusStart);
        var rand = new System.Random((int)(Time.realtimeSinceStartup * 1000f));

        while (r <= spawnRadiusMax)
        {
            int samples = Mathf.Clamp(Mathf.CeilToInt(2f * Mathf.PI * r / Mathf.Max(1f, minDist)), 8, 48);
            for (int i = 0; i < samples; i++)
            {
                float t = (i + (float)rand.NextDouble() * 0.35f) / samples;
                float ang = t * Mathf.PI * 2f;
                var p = seed + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
                if (IsFarEnoughFromOthers(p, minDist)) return p;
            }
            r += Mathf.Max(8f, spawnRadiusStep);
        }
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

    // ---------- helpers ----------
    private static bool IsVisiblyRevealed(ClueNode n)
    {
        return n && n.Rect && n.Rect.localScale.x >= 0.9f;
    }

    // ---------- math ----------
    private static float ExpoRamp(float start, float max, float heldTime, float rampTime)
    {
        if (heldTime <= 0f) return Mathf.Max(0f, start);
        if (rampTime <= 0f) return Mathf.Max(start, max);

        float s = Mathf.Max(0.0001f, start);
        float ratio = Mathf.Max(0.0001f, max / s);
        float u = Mathf.Clamp01(heldTime / rampTime);
        return s * Mathf.Pow(ratio, u);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Discovery cutscene (with guards)
    private System.Collections.IEnumerator PlayDiscoveryCutscene(List<ClueNode> orderedPrecomputed = null)
    {
        cutsceneRunning = true;
        Debug.Log("[CognitionBoard] Cutscene START — inputs gated locally.", this);
        Debug.Log($"[CognitionBoard] Cutscene START pan={ContentRect.anchoredPosition} zoom={(boardCamera? boardCamera.CurrentZoom : CurrentZoom):0.00}", this);

        // Ensure any queued nodes start at scale 0
        foreach (var guid in pendingNodePops)
            if (nodes.TryGetValue(guid, out var n) && n && n.Rect) n.Rect.localScale = Vector3.zero;

        var ordered = (orderedPrecomputed != null && orderedPrecomputed.Count > 0)
            ? orderedPrecomputed
            : BuildOrderedNewNodes();

        // Quick zoom IN to cutsceneZoom
        yield return StartCoroutine(ZoomToOverSeconds(Mathf.Max(0.05f, cutsceneZoom), cutsceneInZoomSeconds));

        for (int i = 0; i < ordered.Count; i++)
        {
            var node = ordered[i];
            if (!node || !node.Rect) continue;

            SelectNodeInternal(node);
            boardCamera?.FocusOn(node.Rect, immediate: false);

            float tPop = 0f;
            Vector3 from = node.Rect.localScale;
            Vector3 to   = Vector3.one;
            while (tPop < 1f)
            {
                tPop += Time.unscaledDeltaTime / Mathf.Max(0.01f, nodePopDuration);
                float e = Mathf.SmoothStep(0f, 1f, tPop);
                node.Rect.localScale = Vector3.LerpUnclamped(from, to, e);
                yield return null;
            }
            node.Rect.localScale = Vector3.one;

            if (panelDelayAfterPop > 0f)
            {
                float end = Time.unscaledTime + panelDelayAfterPop;
                while (Time.unscaledTime < end) yield return null;
            }

            if (infoPanel) infoPanel.ShowFor(node.Data, immediate: false);

            // Guarded advance logic
            bool readyToAdvance = false;
            float guardTimer = 0f;
            bool releaseSeen = false;

            while (!readyToAdvance)
            {
                var action = submitAction ? submitAction.action : null;
                bool pressedThisFrame = action != null && action.WasPerformedThisFrame();
                bool isPressed        = action != null && action.IsPressed();

                if (infoPanel && infoPanel.IsTyping)
                {
                    if (pressedThisFrame)
                    {
                        infoPanel.CompleteTyping();
                        guardTimer = 0f;
                        releaseSeen = false;
                        Debug.Log("[CognitionBoard] Cutscene: typewriter fast-forwarded.", this);
                    }
                }
                else
                {
                    if (!releaseSeen)
                    {
                        if (!isPressed)
                        {
                            releaseSeen = true;
                            guardTimer = 0f;
                            Debug.Log($"[CognitionBoard] Cutscene: release detected; starting {minAdvanceDelayAfterType:0.00}s guard.", this);
                        }
                    }
                    else
                    {
                        guardTimer += Time.unscaledDeltaTime;
                        if (guardTimer >= Mathf.Max(0f, minAdvanceDelayAfterType) && pressedThisFrame)
                        {
                            readyToAdvance = true;
                            Debug.Log("[CognitionBoard] Cutscene: advance accepted after guard.", this);
                        }
                    }
                }

                yield return null;
            }

            // Reveal any pending lines that touch THIS node.
            var toRemove = new List<(string,string)>();
            foreach (var pair in pendingLineReveals)
            {
                bool touchesThis = pair.Item1 == node.ClueGuid || pair.Item2 == node.ClueGuid;
                if (!touchesThis) continue;

                EnsureLineWithStyle(pair.Item1, pair.Item2);

                if (!lines.TryGetValue(pair, out var line) || line == null) continue;

                bool connectsToNextNew =
                    (i + 1 < ordered.Count) &&
                    (ordered[i + 1].ClueGuid == pair.Item1 || ordered[i + 1].ClueGuid == pair.Item2);

                line.SetReveal(0f);

                float t = 0f;
                while (t < 1f)
                {
                    t += Time.unscaledDeltaTime / Mathf.Max(0.01f, revealDurationPerLine);
                    float p = Mathf.Clamp01(t);
                    line.SetReveal(p);

                    if (connectsToNextNew)
                    {
                        var tip = line.GetRevealTipWorld();
                        boardCamera?.FocusOnWorldPoint(tip, immediate: false);
                    }

                    yield return null;
                }
                line.SetReveal(1f);

                toRemove.Add(pair);
                Debug.Log($"[CognitionBoard] Cutscene: revealed connection '{pair.Item1}' <-> '{pair.Item2}'. NextNew={connectsToNextNew}", this);
            }
            foreach (var pr in toRemove)
            {
                pendingLineReveals.Remove(pr);
                pendingLineSet.Remove(pr);
            }

            infoPanel?.Hide(false);
        }

        // Done with node reveals
        pendingNodePops.Clear();

        // Smooth zoom OUT to default
        yield return StartCoroutine(ZoomToOverSeconds(Mathf.Max(0.05f, defaultZoom), cutsceneOutZoomSeconds));

        // Final selection & info restore
        if (ordered.Count > 0) SelectNodeInternal(ordered[^1]);
        else SelectClosestToViewCenterAndFocus();

        if (selectedNode && (!showInfoOnlyWhenHardFocus || !inFreelook))
            infoPanel?.ShowFor(selectedNode.Data, immediate: true);

        cutsceneRunning = false;
        cutsceneScheduled = false;
        cutsceneActive = false;

        Debug.Log("[CognitionBoard] Cutscene END — inputs ungated; normal selection/info behavior resumed.", this);
    }

    // Build ordered list of *new* nodes (filters out anything already visible).
    private List<ClueNode> BuildOrderedNewNodes()
    {
        var list = new List<ClueNode>();
        var order = SaveSystem.Instance ? SaveSystem.Instance.GetDiscoveryOrder() : null;

        if (order != null && order.Count > 0)
        {
            foreach (var guid in order)
            {
                if (!pendingNodePops.Contains(guid)) continue;
                if (!nodes.TryGetValue(guid, out var n) || n == null) continue;
                if (IsVisiblyRevealed(n)) continue; // defensive filter
                list.Add(n);
            }
        }

        if (list.Count == 0)
        {
            foreach (var guid in pendingNodePops)
            {
                if (!nodes.TryGetValue(guid, out var n) || n == null) continue;
                if (IsVisiblyRevealed(n)) continue;
                list.Add(n);
            }
        }

        return list;
    }

    // Smooth absolute zoom to target using BoardCamera
    private System.Collections.IEnumerator ZoomToOverSeconds(float targetAbsoluteZoom, float seconds)
    {
        if (!boardCamera) yield break;

        float start = boardCamera.CurrentZoom;
        targetAbsoluteZoom = Mathf.Max(0.05f, targetAbsoluteZoom);
        seconds = Mathf.Max(0.001f, seconds);

        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / seconds;
            float z = Mathf.Lerp(start, targetAbsoluteZoom, Mathf.SmoothStep(0f, 1f, t));

            float factor = Mathf.Clamp(z / Mathf.Max(0.0001f, boardCamera.CurrentZoom), 0.01f, 100f);
            boardCamera.ZoomAroundCenter(factor);

            yield return null;
        }
    }
}
