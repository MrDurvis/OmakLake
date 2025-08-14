using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ClueNode : MonoBehaviour
{
    // Reference to the ClueData ScriptableObject
    public ClueData clueData;

    // UI components
    public TextMeshPro labelText;    // To display clue's name
    public Image iconImage;          // To display clue's icon

    // Positioning
    private Vector3 targetPosition;
    private Vector3 currentVelocity;

    // Initialize with ClueData
    public void Initialize(ClueData data)
    {
        clueData = data;
        UpdateDisplay();

        // Optional: start at a random position
        transform.localPosition = Random.insideUnitCircle * 300f;
        targetPosition = transform.localPosition;
    }

    // Update UI display based on ClueData
    private void UpdateDisplay()
    {
        if (labelText != null)
            labelText.text = clueData.clueName;

        if (iconImage != null && clueData.icon != null)
            iconImage.sprite = clueData.icon;
    }

    void Update()
    {
        // Animate movement toward target position
        transform.localPosition = Vector3.SmoothDamp(transform.localPosition, targetPosition, ref currentVelocity, 0.3f);
    }

    // Set a new target position (e.g., when connections are made)
    public void SetTargetPosition(Vector3 newPos)
    {
        targetPosition = newPos;
    }
}
