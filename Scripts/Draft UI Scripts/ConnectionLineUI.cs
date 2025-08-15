using UnityEngine;
using UnityEngine.UI;

public class ConnectionLineUI : MonoBehaviour
{
    [SerializeField] private RectTransform rect;
    [SerializeField] private Image img;

    public RectTransform A { get; private set; }
    public RectTransform B { get; private set; }

    void Reset()
    {
        rect = GetComponent<RectTransform>();
        img = GetComponent<Image>();
        if (img) img.raycastTarget = false;
    }

    public void Init(RectTransform a, RectTransform b, Color color, float thickness = 6f)
    {
        A = a; B = b;
        if (!rect) rect = GetComponent<RectTransform>();
        if (!img) img = GetComponent<Image>();

        img.color = color;

        var s = rect.sizeDelta;
        s.x = thickness;       // width
        rect.sizeDelta = s;

        Update();
    }

    void LateUpdate() => Update();

    private void Update()
    {
        if (!rect || !A || !B) return;

        Vector2 a = A.anchoredPosition;
        Vector2 b = B.anchoredPosition;
        Vector2 delta = b - a;

        float length = delta.magnitude;
        float angle  = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;

        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = (a + b) * 0.5f;
        rect.sizeDelta = new Vector2(rect.sizeDelta.x, length);
        rect.localRotation = Quaternion.Euler(0, 0, angle - 90f);
    }

    public void SetColor(Color c) { if (img) img.color = c; }
}
