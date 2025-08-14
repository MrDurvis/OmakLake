using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ClueNode : MonoBehaviour
{
    // Reference to the ClueData ScriptableObject
    [SerializeField] private ClueData clueData;

    // Positioning variables
    private Vector3 targetPosition;
    private Vector3 currentVelocity;

    void Start()
    {
        if (clueData != null)
        {
            Initialize(clueData);
        }
    }

    // Initialize the node with ClueData
    public void Initialize(ClueData data)
    {
        clueData = data;
        CreateUIElements();
        UpdateDisplay();

        // Optional: start at a random position
        transform.localPosition = Random.insideUnitCircle * 300f;
        targetPosition = transform.localPosition;
    }

    // Create and set up UI elements directly
    private void CreateUIElements()
    {
        // Create and set up a TextMeshProUGUI for the clue name
        GameObject labelObj = new GameObject("LabelText", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelObj.transform.SetParent(transform, false);
        TextMeshProUGUI labelText = labelObj.GetComponent<TextMeshProUGUI>();
        labelText.text = clueData.clueName;
        labelText.alignment = TextAlignmentOptions.Center;
        // Adjust additional TextMeshPro properties as needed

        // Create and set up an Image for the clue icon
        GameObject imageObj = new GameObject("IconImage", typeof(RectTransform), typeof(Image));
        imageObj.transform.SetParent(transform, false);
        Image iconImage = imageObj.GetComponent<Image>();
        iconImage.sprite = clueData.icon;
        // Adjust additional Image properties as needed
    }

    // Currently, nothing additional needed to update
    private void UpdateDisplay() { }

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
