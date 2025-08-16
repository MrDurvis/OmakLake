using UnityEngine;
using UnityEngine.UI;

public class ConnectionLineUI : MonoBehaviour
{
    [SerializeField] private RectTransform from;         // anchor A (node LineAnchor or node Rect)
    [SerializeField] private RectTransform to;           // anchor B
    [SerializeField] private RectTransform lineRect;     // this rect (the line)
    [SerializeField] private Image image;                // Image on this object
    [SerializeField] private RectTransform container;    // usually the "Lines" layer (parent)

    public void Initialize(RectTransform a, RectTransform b, Color color, float width)
    {
        from = a;
        to   = b;

        if (!lineRect)  lineRect  = GetComponent<RectTransform>();
        if (!image)     image     = GetComponent<Image>();
        if (!container) container = (RectTransform)transform.parent;

        if (image) image.color = color;
        SetWidth(width);
        UpdateLine();
    }

    public void SetStyle(Color color, float width)
    {
        if (image) image.color = color;
        SetWidth(width);
    }

    private void SetWidth(float width)
    {
        if (!lineRect) return;
        var sz = lineRect.sizeDelta;
        sz.y = Mathf.Max(1f, width);       // thickness (vertical size)
        lineRect.sizeDelta = sz;
    }

    private void LateUpdate()
    {
        UpdateLine();
    }

    private void UpdateLine()
    {
        if (!from || !to || !lineRect) return;
        if (!container) container = (RectTransform)transform.parent;
        if (!container) return;

        // Get world-space center of each anchor, then convert to container local
        Vector2 a = WorldCenterToLocal(from);
        Vector2 b = WorldCenterToLocal(to);

        Vector2 mid   = (a + b) * 0.5f;
        Vector2 delta = b - a;

        float len   = delta.magnitude;
        float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;

        lineRect.anchoredPosition = mid;

        // Set X length (width along line), Y = thickness already set
        var sz = lineRect.sizeDelta;
        sz.x = len;
        lineRect.sizeDelta = sz;

        lineRect.localRotation = Quaternion.Euler(0, 0, angle);
    }

    private Vector2 WorldCenterToLocal(RectTransform rt)
    {
        // Use the rect’s true geometric center in world space
        Vector3 world = rt.TransformPoint(rt.rect.center);
        return ((RectTransform)transform.parent).InverseTransformPoint(world);
    }
}
