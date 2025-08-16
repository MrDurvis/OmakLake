using UnityEngine;
using UnityEngine.UI;

public class ConnectionLineUI : MonoBehaviour
{
    [SerializeField] private RectTransform from;       // A anchor (older or chosen start)
    [SerializeField] private RectTransform to;         // B anchor
    [SerializeField] private RectTransform lineRect;   // this rect (horizontal bar)
    [SerializeField] private Image image;              // Image on this object
    [SerializeField] private RectTransform container;  // parent (Lines layer)

    [Range(0f, 1f)] public float reveal = 1f;          // 0..1 length
    private bool growFromA = true;                     // NEW: direction of reveal

    public void Initialize(RectTransform a, RectTransform b, Color color, float width)
    {
        from = a; to = b;
        if (!lineRect)  lineRect  = GetComponent<RectTransform>();
        if (!image)     image     = GetComponent<Image>();
        if (!container) container = (RectTransform)transform.parent;

        if (image) image.color = color;
        SetThickness(width);
        UpdateLine();
    }

    public void SetStyle(Color color, float width)
    {
        if (image) image.color = color;
        SetThickness(width);
    }

    public void SetReveal(float t)
    {
        reveal = Mathf.Clamp01(t);
        UpdateLine();
    }

    public void SetGrowFrom(bool fromA) => growFromA = fromA; // NEW

    private void SetThickness(float width)
    {
        if (!lineRect) return;
        var sz = lineRect.sizeDelta;
        sz.y = Mathf.Max(1f, width);
        lineRect.sizeDelta = sz;
    }

    private void LateUpdate() => UpdateLine();

    private void UpdateLine()
    {
        if (!from || !to || !lineRect) return;
        if (!container) container = (RectTransform)transform.parent;
        if (!container) return;

        Vector2 a = WorldCenterToLocal(from);
        Vector2 b = WorldCenterToLocal(to);
        Vector2 delta = b - a;
        float fullLen = delta.magnitude;
        if (fullLen < 0.001f) return;

        float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;

        Vector2 start, end;
        if (reveal >= 0.999f)
        {
            start = a; end = b;
        }
        else if (growFromA)
        {
            start = a;
            end   = a + delta * Mathf.Clamp01(reveal);
        }
        else
        {
            end   = b;
            start = b - delta * Mathf.Clamp01(reveal);
        }

        Vector2 mid = (start + end) * 0.5f;
        float len = (end - start).magnitude;

        lineRect.anchoredPosition = mid;
        var sz = lineRect.sizeDelta;
        sz.x = len;
        lineRect.sizeDelta = sz;
        lineRect.localRotation = Quaternion.Euler(0, 0, angle);
    }

    private Vector2 WorldCenterToLocal(RectTransform rt)
    {
        Vector3 world = rt.TransformPoint(rt.rect.center);
        return ((RectTransform)transform.parent).InverseTransformPoint(world);
    }
}
