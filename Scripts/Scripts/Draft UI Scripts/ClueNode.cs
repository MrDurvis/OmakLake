using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ClueNode : MonoBehaviour
{
    public string nodeName;
    public int nodeID;
    public bool isDiscovered = false;
    public float connectionStrength = 0f; // 0-1
    public TextMeshPro labelText;
    public Image iconImage; // optional, for different categories
    public GameObject connectionsParent; // parent for connection lines

    private Vector3 targetPosition;
    private Vector3 currentVelocity;

    void Start()
    {
        labelText.text = nodeName;
        // Initialize node position randomly or at a default
        transform.localPosition = Random.insideUnitCircle * 300f;
        targetPosition = transform.localPosition;
    }

    void Update()
    {
        // Animate movement towards target position
        transform.localPosition = Vector3.SmoothDamp(transform.localPosition, targetPosition, ref currentVelocity, 0.3f);
    }

    public void SetTargetPosition(Vector3 newPos)
    {
        targetPosition = newPos;
    }
}
