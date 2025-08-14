// ConnectionLine.cs
using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class ConnectionLine : MonoBehaviour
{
    public enum ConnectionState { Suggested, Confirmed }

    [SerializeField] private LineRenderer lr;
    [SerializeField] private RectTransform a;
    [SerializeField] private RectTransform b;
    [SerializeField] private ConnectionState state;

    public string AGuid { get; private set; }
    public string BGuid { get; private set; }
    public ConnectionState State => state;

    private void Awake()
    {
        if (!lr) lr = GetComponent<LineRenderer>();
        lr.positionCount = 2;
        lr.useWorldSpace = false;
        ApplyStyle();
    }

    public void Initialize(RectTransform aRect, string aGuid, RectTransform bRect, string bGuid, ConnectionState s)
    {
        a = aRect; b = bRect; AGuid = aGuid; BGuid = bGuid; state = s;
        ApplyStyle();
        UpdateLine();
    }

    public void SetState(ConnectionState s) { state = s; ApplyStyle(); }

    private void ApplyStyle()
    {
        var color = state == ConnectionState.Confirmed ? Color.green : Color.red;
        lr.startColor = lr.endColor = color;
        var width = state == ConnectionState.Confirmed ? 0.045f : 0.03f;
        lr.startWidth = lr.endWidth = width;
    }

    private void LateUpdate() { UpdateLine(); }

    private void UpdateLine()
    {
        if (!a || !b) return;
        lr.SetPosition(0, a.anchoredPosition);
        lr.SetPosition(1, b.anchoredPosition);
    }
}
