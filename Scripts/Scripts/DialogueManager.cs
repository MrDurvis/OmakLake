using UnityEngine;
using TMPro;
public class DialogueManager : MonoBehaviour
{
    public static DialogueManager Instance; // Singleton

    public GameObject dialogueUI;
    public TMP_Text dialogueText;

    private bool isShowing = false;
    public bool isDialogueActive = false;

    private float hideDelay = 0.5f; // seconds
    private float timer = 0f; // internal countdown

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (dialogueUI != null)
        {
            dialogueUI.SetActive(false);
            Debug.Log("Dialogue UI set to inactive at start");
        }
        else
        {
            Debug.LogError("DialogueUI not assigned!");
        }
    }

    public void ShowDialogue(string message)
    {
        Debug.Log($"ShowDialogue called with message: {message}");
        if (dialogueUI == null || dialogueText == null)
        {
            Debug.LogError("Dialogue UI or Text not assigned!");
            return;
        }

        if (!dialogueUI.activeSelf)
        {
            dialogueUI.SetActive(true);
            Debug.Log("Dialogue UI activated");
        }

        dialogueText.text = message;
        isShowing = true;
        isDialogueActive = true;
        timer = 0f; // reset timer on show
        Debug.Log("Dialogue is now active");
    }

    public void HideDialogue()
    {
        if (dialogueUI != null)
        {
            dialogueUI.SetActive(false);
            Debug.Log("Dialogue UI deactivated");
        }
        isShowing = false;
        isDialogueActive = false;
        Debug.Log("Dialogue is now inactive");
    }

    private void Update()
    {
        // Accumulate time if dialogue is active
        if (isShowing)
        {
            timer += Time.unscaledDeltaTime;
        }

        // Only allow hide after delay
        if (isShowing && timer >= hideDelay && Input.GetKeyDown(KeyCode.JoystickButton0))
        {
            Debug.Log("Joystick button pressed after delay, hiding dialogue");
            HideDialogue();
        }
    }

    public bool IsDialogueActive()
    {
        return isDialogueActive;
    }
}
