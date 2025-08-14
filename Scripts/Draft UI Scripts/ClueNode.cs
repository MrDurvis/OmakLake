using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

[RequireComponent(typeof(RectTransform))]
public class ClueNode : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private RectTransform rect;
    private Image iconImage;
    private TMP_Text titleText;
    private Image categoryRing;
    private CognitionBoard board;

    public string ClueGuid { get; private set; }
    public RectTransform Rect => rect;
    public ClueData Data { get; private set; }

    private void Awake()
    {
        rect = GetComponent<RectTransform>();
        EnsureUIExists();
    }

    private void EnsureUIExists()
    {
        // Background (optional)
        if (!TryGetComponent<Image>(out _))
        {
            var bg = gameObject.AddComponent<Image>();
            bg.raycastTarget = true;
            bg.color = new Color(0f, 0f, 0f, 0.35f);
        }

        // Icon
        var icon = transform.Find("Icon") as RectTransform;
        if (!icon)
        {
            icon = new GameObject("Icon", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            icon.SetParent(transform, false);
            icon.sizeDelta = new Vector2(64, 64);
            icon.anchoredPosition = new Vector2(-60, 0);
        }
        iconImage = icon.GetComponent<Image>();

        // Ring (optional)
        var ring = transform.Find("Ring") as RectTransform;
        if (!ring)
        {
            ring = new GameObject("Ring", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            ring.SetParent(transform, false);
            ring.sizeDelta = new Vector2(74, 74);
            ring.anchoredPosition = new Vector2(-60, 0);
        }
        categoryRing = ring.GetComponent<Image>();
        categoryRing.raycastTarget = false;

        // Title
        var title = transform.Find("Title") as RectTransform;
        if (!title)
        {
            title = new GameObject("Title", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<RectTransform>();
            title.SetParent(transform, false);
            title.sizeDelta = new Vector2(220, 60);
            title.anchoredPosition = new Vector2(40, 0);
        }
        titleText = title.GetComponent<TextMeshProUGUI>();
        if (titleText != null)
        {
            titleText.textWrappingMode = TextWrappingModes.Normal;
            titleText.alignment = TextAlignmentOptions.MidlineLeft;
        }

        if (rect == null) rect = GetComponent<RectTransform>();
        if (rect != null) rect.sizeDelta = new Vector2(300, 90);
    }

    public void Initialize(CognitionBoard owner, ClueData data)
    {
        // >>> NEW: ensure everything exists even if Awake hasn't run yet
        if (rect == null || titleText == null || iconImage == null || categoryRing == null)
        {
            rect ??= GetComponent<RectTransform>() ?? gameObject.AddComponent<RectTransform>();
            EnsureUIExists();
        }

        Debug.Log($"[ClueNode] Initialize | owner={(owner!=null)} data={(data!=null)} rect={(rect!=null)} title={(titleText!=null)} icon={(iconImage!=null)} ring={(categoryRing!=null)}", this);

        if (owner == null || data == null)
        {
            Debug.LogError("[ClueNode] Initialize received null owner or data.", this);
            return;
        }

        board = owner;
        Data = data;
        ClueGuid = data.Guid;

        // Fill from ClueData
        if (titleText) titleText.text = string.IsNullOrWhiteSpace(data.clueName) ? "(Unnamed Clue)" : data.clueName;
        if (iconImage) iconImage.sprite = data.icon; // null sprite is OK
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

        if (rect) rect.anchoredPosition = Random.insideUnitCircle * 300f;

        Debug.Log($"[ClueNode] Init OK | Guid={data.Guid} | Name='{data.clueName}' | HasIcon={(data.icon!=null)} | Category={data.category}", this);
    }

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
