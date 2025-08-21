using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;
using System.Collections.Generic;

[RequireComponent(typeof(RectTransform))]
public class ClueNode : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public enum VisualStyle { CompactCircle, WideWithTitle }
    public enum AmbientShadowMode { Never, Always, OnlyWhenSelected }

    [Header("Visual")]
    [SerializeField] private VisualStyle style = VisualStyle.CompactCircle;
    [SerializeField] private bool showTitle = false;

    // ---------- Selection ring ----------
    [Header("Selection Visuals")]
    [SerializeField] private bool showRingOnlyWhenSelected = true;
    [SerializeField] private Color ringSelectedColor = new(0.15f, 0.75f, 1f, 1f);
    [SerializeField] private Color ringUnselectedColor = new(1f, 1f, 1f, 0f);
    [SerializeField] private bool useCategoryColorForUnselected = false;

    [Header("Ring Size")]
    [SerializeField, Min(0.1f)] private float ringScale = 1.12f;
    [SerializeField] private float ringExtraPixels = 0f;

    [Header("Effects On Selection (optional)")]
    [SerializeField] private Outline outlineOnSelected;
    [SerializeField] private bool enableOutlineOnSelected = false;
    [SerializeField] private Color outlineSelectedColor = Color.white;
    [SerializeField] private Vector2 outlineSelectedDistance = new(3f, -3f);

    [SerializeField] private Shadow glowOnSelected;
    [SerializeField] private bool enableGlowOnSelected = false;
    [SerializeField] private Color glowSelectedColor = new(1f, 1f, 1f, 0.5f);
    [SerializeField] private Vector2 glowSelectedDistance = Vector2.zero;

    [Header("Base Effects (always on/off)")]
    [SerializeField] private bool baseShadowEnabled = false;
    [SerializeField] private Shadow baseShadow;

    [Header("Ambient Shadow (sprite underlay)")]
    [SerializeField] private Image ambientShadowImage;
    [SerializeField] private AmbientShadowMode ambientShadow = AmbientShadowMode.Never;

    [Header("Input")]
    [Tooltip("If ON, add a transparent Image to the root so the whole node is a raycast target. Usually keep this OFF.")]
    [SerializeField] private bool addRaycastBackground = false;

    // ---------- Private refs ----------
    private RectTransform rect;
    private Image iconImage;
    private RectTransform iconRect;
    [SerializeField] private Image ringImage;
    private Image backgroundImage; // optional, only if addRaycastBackground == true

    private TMP_Text titleText;
    private CognitionBoard board;

    private readonly List<Shadow> _allShadows = new();
    private readonly List<Outline> _allOutlines = new();

    public string ClueGuid { get; private set; }
    public RectTransform Rect => rect;
    public RectTransform LineAnchor => iconRect ? iconRect : rect;
    public ClueData Data { get; private set; }

    private static Sprite _runtimeDefaultSprite;

    // ---------- Unity ----------
    private void Awake()
    {
        rect = GetComponent<RectTransform>();
        EnsureUIExists();
        CollectEffectsCache();
        ApplyStyle();
        ApplyRingSizing();

        SetSelected(false);
        SyncBaseShadow();
        ApplyAmbientShadow(false);
    }

    private void Reset()      { TryAutoAssignRefs(); ApplyRingSizing(); CollectEffectsCache(); SyncBaseShadow(); ApplyAmbientShadow(false); }
    private void OnValidate() { if (!Application.isPlaying) { TryAutoAssignRefs(); ApplyRingSizing(); CollectEffectsCache(); SyncBaseShadow(); ApplyAmbientShadow(false); } }

    // ---------- UI build / wiring ----------
    private void EnsureUIExists()
    {
        // Only add a background Image if explicitly requested.
        if (addRaycastBackground)
        {
            if (!TryGetComponent<Image>(out backgroundImage))
            {
                backgroundImage = gameObject.AddComponent<Image>();
            }
            // Make sure it truly draws nothing but still catches raycasts.
            backgroundImage.sprite = null;                     // <- no sprite
            backgroundImage.type = Image.Type.Simple;
            backgroundImage.color = new Color(1f, 1f, 1f, 0f); // <- zero alpha
            backgroundImage.material = null;
            backgroundImage.raycastTarget = true;
        }

        // Icon
        iconRect = (transform.Find("Icon") as RectTransform);
        if (!iconRect)
        {
            iconRect = new GameObject("Icon", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            iconRect.SetParent(transform, false);
        }
        iconImage = iconRect.GetComponent<Image>();
        iconImage.preserveAspect = true;
        iconRect.anchorMin = iconRect.anchorMax = new Vector2(0.5f, 0.5f);
        iconRect.pivot = new Vector2(0.5f, 0.5f);
        iconRect.anchoredPosition = Vector2.zero;
        iconImage.raycastTarget = true; // <- drag works without a root background

        // Ring
        var ringRect = (transform.Find("Ring") as RectTransform);
        if (!ringRect)
        {
            ringRect = new GameObject("Ring", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            ringRect.SetParent(transform, false);
        }
        if (!ringImage) ringImage = ringRect.GetComponent<Image>();
        ringImage.raycastTarget = true;                       // <- also clickable
        ringRect.anchorMin = ringRect.anchorMax = new Vector2(0.5f, 0.5f);
        ringRect.pivot = new Vector2(0.5f, 0.5f);
        ringRect.anchoredPosition = Vector2.zero;

        // Title
        var title = transform.Find("Title") as RectTransform;
        if (!title)
        {
            title = new GameObject("Title", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<RectTransform>();
            title.SetParent(transform, false);
        }
        titleText = title.GetComponent<TextMeshProUGUI>();
        if (titleText)
        {
            titleText.textWrappingMode = TextWrappingModes.Normal;
            titleText.alignment = TextAlignmentOptions.MidlineLeft;
        }

        if (!rect) rect = GetComponent<RectTransform>();

        // Only ensure sprites on Icon/Ring – never touch the optional background.
        var def = GetDefaultSprite();
        if (iconImage && iconImage.sprite == null) iconImage.sprite = def;
        if (ringImage && ringImage.sprite == null) ringImage.sprite = def;

        TryAutoAssignRefs();
    }

    private void TryAutoAssignRefs()
    {
        if (!ringImage)
        {
            var t = transform.Find("Ring") ?? transform.Find("Frame") ?? transform.Find("Border");
            if (t) ringImage = t.GetComponent<Image>();
        }

        // Ambient shadow auto-detect by common names
        if (!ambientShadowImage)
        {
            foreach (var img in GetComponentsInChildren<Image>(true))
            {
                if (!img || img == ringImage || img == iconImage || img == backgroundImage) continue;
                var n = img.name.ToLowerInvariant();
                if (n.Contains("shadow") || n.Contains("drop") || n.Contains("underlay"))
                { ambientShadowImage = img; break; }
            }
        }

        if (!outlineOnSelected && ringImage) outlineOnSelected = ringImage.GetComponent<Outline>();
        if (!glowOnSelected && ringImage)    glowOnSelected    = ringImage.GetComponent<Shadow>();

        if (!baseShadow)
        {
            baseShadow = GetComponent<Shadow>();
            if (!baseShadow && ringImage) baseShadow = ringImage.GetComponent<Shadow>();
            if (!baseShadow && iconRect)  baseShadow = iconRect.GetComponent<Shadow>();
        }
    }

    private void ApplyStyle()
    {
        if (!rect) rect = GetComponent<RectTransform>();

        if (style == VisualStyle.CompactCircle)
        {
            rect.sizeDelta = new Vector2(110, 110);
            iconRect.sizeDelta = new Vector2(100, 100);
            if (titleText) titleText.gameObject.SetActive(false);
        }
        else
        {
            rect.sizeDelta = new Vector2(300, 110);
            iconRect.sizeDelta = new Vector2(90, 90);
            iconRect.anchoredPosition = new Vector2(-90, 0);
            if (titleText)
            {
                titleText.gameObject.SetActive(showTitle);
                titleText.rectTransform.sizeDelta = new Vector2(180, 80);
                titleText.rectTransform.anchoredPosition = new Vector2(40, 0);
                titleText.alignment = TextAlignmentOptions.MidlineLeft;
            }
        }
    }

    private void ApplyRingSizing()
    {
        if (!iconRect || !ringImage) return;
        var icon = iconRect.sizeDelta;
        var outer = icon * Mathf.Max(0.1f, ringScale) + Vector2.one * ringExtraPixels;
        ringImage.rectTransform.sizeDelta = outer;
    }

    private void CollectEffectsCache()
    {
        _allShadows.Clear();
        _allOutlines.Clear();
        _allShadows.AddRange(GetComponentsInChildren<Shadow>(true));
        _allOutlines.AddRange(GetComponentsInChildren<Outline>(true));
    }

    private void SyncBaseShadow()
    {
        if (!baseShadow) return;
        baseShadow.enabled = baseShadowEnabled;
        if (glowOnSelected && baseShadow == glowOnSelected && !baseShadowEnabled)
            baseShadow.enabled = false;
    }

    // ---------- Data / Init ----------
    public void Initialize(CognitionBoard owner, ClueData data)
    {
        if (!rect || !iconRect || !iconImage || !ringImage) { EnsureUIExists(); ApplyStyle(); }
        CollectEffectsCache();

        board = owner;
        Data = data;
        ClueGuid = data.Guid;

        if (iconImage) iconImage.sprite = data.icon;

        if (useCategoryColorForUnselected)
        {
            ringUnselectedColor = data.category switch
            {
                ClueData.ClueCategory.Person   => new Color(0.85f, 0.20f, 0.20f),
                ClueData.ClueCategory.Object   => new Color(0.20f, 0.70f, 1.00f),
                ClueData.ClueCategory.Location => new Color(0.20f, 0.90f, 0.50f),
                ClueData.ClueCategory.Event    => new Color(1.00f, 0.80f, 0.20f),
                _ => ringUnselectedColor
            };
        }

        if (titleText)
        {
            titleText.text = string.IsNullOrWhiteSpace(data.clueName) ? "" : data.clueName;
            titleText.gameObject.SetActive(showTitle && style == VisualStyle.WideWithTitle);
        }

        SetSelected(false);
        ApplyAmbientShadow(false);
    }

    // ---------- Selection visuals ----------
    public void SetSelected(bool selected)
    {
        // Ring
        if (ringImage)
        {
            if (showRingOnlyWhenSelected)
            {
                ringImage.enabled = selected;
                if (selected) ringImage.color = ringSelectedColor;
            }
            else
            {
                ringImage.enabled = true;
                ringImage.color = selected ? ringSelectedColor : ringUnselectedColor;
            }
        }

        // Outline
        if (outlineOnSelected)
        {
            outlineOnSelected.enabled = enableOutlineOnSelected && selected;
            if (selected)
            {
                outlineOnSelected.effectColor = outlineSelectedColor;
                outlineOnSelected.effectDistance = outlineSelectedDistance;
            }
        }

        // Glow (Shadow)
        if (glowOnSelected)
        {
            glowOnSelected.enabled = enableGlowOnSelected && selected;
            if (selected)
            {
                glowOnSelected.effectColor = glowSelectedColor;
                glowOnSelected.effectDistance = glowSelectedDistance;
            }
        }

        // Safety: disable any stray Shadows/Outlines when unselected
        if (!selected)
        {
            foreach (var s in _allShadows)
            {
                if (!s) continue;
                if (baseShadowEnabled && s == baseShadow) { s.enabled = true; continue; }
                if (s == glowOnSelected) { s.enabled = false; continue; }
                s.enabled = false;
            }
            foreach (var o in _allOutlines)
            {
                if (!o) continue;
                if (o == outlineOnSelected) { o.enabled = false; continue; }
                o.enabled = false;
            }
        }
        else
        {
            if (baseShadow) baseShadow.enabled = baseShadowEnabled;
        }

        // Ambient shadow sprite
        ApplyAmbientShadow(selected);
    }

    private void ApplyAmbientShadow(bool selected)
    {
        if (!ambientShadowImage) return;
        ambientShadowImage.enabled = ambientShadow switch
        {
            AmbientShadowMode.Never            => false,
            AmbientShadowMode.Always           => true,
            AmbientShadowMode.OnlyWhenSelected => selected,
            _                                  => false
        };
    }

    // ---------- Drag handling ----------
    public void OnBeginDrag(PointerEventData e) { board?.BeginNodeDrag(this); }
    public void OnDrag(PointerEventData e)
    {
        if (board == null || Rect == null) return;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(board.ContentRect, e.position, e.pressEventCamera, out var lp))
        {
            Rect.anchoredPosition = lp;
            board.OnNodeMoved(this);
        }
    }
    public void OnEndDrag(PointerEventData e) { board?.EndNodeDrag(this); }

    // ---------- Debug ----------
    [ContextMenu("Debug: Print Effects")]
    private void DebugPrintEffects()
    {
        foreach (var s in GetComponentsInChildren<Shadow>(true))
            Debug.Log($"[ClueNode] Shadow -> {s.name} (enabled:{s.enabled})", s);
        foreach (var o in GetComponentsInChildren<Outline>(true))
            Debug.Log($"[ClueNode] Outline -> {o.name} (enabled:{o.enabled})", o);
        if (ambientShadowImage)
            Debug.Log($"[ClueNode] AmbientShadowImage -> {ambientShadowImage.name} (enabled:{ambientShadowImage.enabled})", ambientShadowImage);
        if (backgroundImage)
            Debug.Log($"[ClueNode] BackgroundImage -> {backgroundImage.name} (enabled:{backgroundImage.enabled}, alpha:{backgroundImage.color.a})", backgroundImage);
    }

    // ---------- Sprite fallback ----------
    private static Sprite GetDefaultSprite()
    {
        if (_runtimeDefaultSprite != null) return _runtimeDefaultSprite;
        #if UNITY_EDITOR
        var editorSprite = UnityEditor.AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        if (editorSprite != null) { _runtimeDefaultSprite = editorSprite; return _runtimeDefaultSprite; }
        #endif
        const int W = 16, H = 16;
        var tex = new Texture2D(W, H, TextureFormat.ARGB32, false);
        var px = new Color32[W * H];
        for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
        tex.SetPixels32(px); tex.Apply(false, true);
        _runtimeDefaultSprite = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f);
        _runtimeDefaultSprite.name = "DefaultUISpriteRuntime";
        return _runtimeDefaultSprite;
    }
}
