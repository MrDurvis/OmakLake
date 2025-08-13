using UnityEngine;
using TMPro; // Assuming you're using TextMeshPro for dialogue text

public class DialogueManager : MonoBehaviour
{
    public static DialogueManager Instance; // Singleton instance

    public GameObject dialogueUI;       // Assign your dialogue panel in inspector
    public TMP_Text dialogueText;       // Text component to display dialogue text

    private bool isShowing = false;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject); // Ensure only one instance
            return;
        }
        Instance = this;

        // Optional: Persist across scenes
        // DontDestroyOnLoad(gameObject);
    }

    /// <summary>
    /// Show dialogue with text
    /// </summary>
    public void ShowDialogue(string message)
    {
        if (dialogueUI == null || dialogueText == null)
        {
            Debug.LogError("Dialogue UI or Text not assigned!");
            return;
        }

        dialogueUI.SetActive(true);
        dialogueText.text = message;
        isShowing = true;
    }

    /// <summary>
    /// Hide dialogue box
    /// </summary>
    public void HideDialogue()
    {
        if (dialogueUI != null)
        {
            dialogueUI.SetActive(false);
        }
        isShowing = false;
    }

    private void Update()
    {
        // Optional: Press any key or button to dismiss dialogue
        if (isShowing && (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return)))
        {
            HideDialogue();
        }
    }
}