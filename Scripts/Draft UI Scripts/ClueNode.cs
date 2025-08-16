using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

[RequireComponent(typeof(RectTransform))]
public class ClueNode : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public enum VisualStyle { CompactCircle, WideWithTitle }

    [Header("Visual")]
    [SerializeField] private VisualStyle style = VisualStyle.CompactCircle;
    [SerializeField] private bool showTitle = false;     // <- off by default

    private RectTransform rect;
    private Image iconImage;
    private RectTransform iconRect;                      // <- center anchor for lines
    private Image categoryRing;
    private TMP_Text titleText;
    private CognitionBoard board;

    public string ClueGuid { get; private set; }
    public RectTransform Rect => rect;
    public RectTransform LineAnchor => iconRect ? iconRect : rect;  // <- for lines
    public ClueData Data { get; private set; }

    void Awake()
    {
        rect = GetComponent<RectTransform>();
        EnsureUIExists();
        ApplyStyle();
    }

    private void EnsureUIExists()
    {
        // background (optional)
        if (!TryGetComponent<Image>(out _))
        {
            var bg = gameObject.AddComponent<Image>();
            bg.raycastTarget = true;
            bg.color = new Color(0f, 0f, 0f, 0.0f); // fully transparent by default
        }

        // Icon (the circular photo)
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

        // Ring overlay
        var ringRect = (transform.Find("Ring") as RectTransform);
        if (!ringRect)
        {
            ringRect = new GameObject("Ring", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            ringRect.SetParent(transform, false);
        }
        categoryRing = ringRect.GetComponent<Image>();
        categoryRing.raycastTarget = false;
        ringRect.anchorMin = ringRect.anchorMax = new Vector2(0.5f, 0.5f);
        ringRect.pivot = new Vector2(0.5f, 0.5f);
        ringRect.anchoredPosition = Vector2.zero;

        // Title (optional)
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
    }

    private void ApplyStyle()
    {
        if (!rect) rect = GetComponent<RectTransform>();

        if (style == VisualStyle.CompactCircle)
        {
            // Square node; centered icon + ring
            rect.sizeDelta = new Vector2(110, 110);
            iconRect.sizeDelta = new Vector2(100, 100);
            if (categoryRing) categoryRing.rectTransform.sizeDelta = new Vector2(112, 112);
            if (titleText) titleText.gameObject.SetActive(false);
        }
        else // WideWithTitle
        {
            rect.sizeDelta = new Vector2(300, 110);
            iconRect.sizeDelta = new Vector2(90, 90);
            iconRect.anchoredPosition = new Vector2(-90, 0);
            if (categoryRing) categoryRing.rectTransform.sizeDelta = new Vector2(100, 100);

            if (titleText)
            {
                titleText.gameObject.SetActive(showTitle);
                titleText.rectTransform.sizeDelta = new Vector2(180, 80);
                titleText.rectTransform.anchoredPosition = new Vector2(40, 0);
                titleText.alignment = TextAlignmentOptions.MidlineLeft;
            }
        }
    }

    public void Initialize(CognitionBoard owner, ClueData data)
    {
        if (!rect || !iconRect || !iconImage || !categoryRing) { EnsureUIExists(); ApplyStyle(); }

        board = owner;
        Data = data;
        ClueGuid = data.Guid;

        // Fill from data
        if (iconImage) iconImage.sprite = data.icon;    // null OK

        if (categoryRing)
        {
            categoryRing.color = data.category switch
            {
                ClueData.ClueCategory.Person   => new Color(0.85f, 0.20f, 0.20f),
                ClueData.ClueCategory.Object   => new Color(0.20f, 0.70f, 1.00f),
                ClueData.ClueCategory.Location => new Color(0.20f, 0.90f, 0.50f),
                ClueData.ClueCategory.Event    => new Color(1.00f, 0.80f, 0.20f),
                _ => Color.white
            };
        }

        if (titleText)
        {
            titleText.text = string.IsNullOrWhiteSpace(data.clueName) ? "" : data.clueName;
            titleText.gameObject.SetActive(showTitle && style == VisualStyle.WideWithTitle);
        }

        // spawn at a random point so new nodes don't stack
        rect.anchoredPosition = Random.insideUnitCircle * 300f;
    }

    // Selection feedback (tiny scale bump)
    public void SetSelected(bool sel)
    {
        transform.localScale = sel ? Vector3.one * 1.08f : Vector3.one;
    }

    // --- Drag handling ---
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
}
