using UnityEngine;
using TMPro;
using UnityEngine.InputSystem;

public class DialogueManager : MonoBehaviour
{
    public static DialogueManager Instance { get; private set; }

    [SerializeField] private GameObject dialogueUI;
    [SerializeField] private TMP_Text dialogueText;
    [SerializeField] private InputActionReference submitAction; // typically UI/Submit or Gameplay/Submit
    [SerializeField] private float minShowSeconds = 0.3f;

    private bool isActive;
    private float shownAt;

    private void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        Debug.Log($"[DialogueManager] Awake. UI={(dialogueUI ? dialogueUI.name : "NULL")}, Text={(dialogueText ? dialogueText.name : "NULL")}, SubmitAction={(submitAction ? submitAction.name : "NULL")}");

        if (dialogueUI) dialogueUI.SetActive(false);
    }

    private void OnEnable()
    {
        if (submitAction != null)
        {
            submitAction.action.performed += OnSubmit;
            submitAction.action.Enable();
            Debug.Log($"[DialogueManager] Submit action enabled: '{submitAction.action.name}' in map '{submitAction.action.actionMap?.name}'.");
        }
        else Debug.LogWarning("[DialogueManager] No Submit action assigned.");
    }

    private void OnDisable()
    {
        if (submitAction != null)
        {
            submitAction.action.performed -= OnSubmit;
            submitAction.action.Disable();
            Debug.Log("[DialogueManager] Submit action disabled.");
        }
    }

    public void ShowDialogue(string msg)
    {
        if (!dialogueUI || !dialogueText)
        {
            Debug.LogError("[DialogueManager] Dialogue UI/Text not assigned.");
            return;
        }

        dialogueText.text = msg;
        dialogueUI.SetActive(true);
        isActive = true;
        shownAt = Time.unscaledTime;
        Debug.Log($"[DialogueManager] Show. '{msg}'");
    }

    private void OnSubmit(InputAction.CallbackContext _)
    {
        if (!isActive) return;
        if (Time.unscaledTime - shownAt < minShowSeconds) return;

        Debug.Log("[DialogueManager] Submit pressed. Hiding dialogue.");
        Hide();
    }

    public void Hide()
    {
        if (dialogueUI) dialogueUI.SetActive(false);
        isActive = false;
    }

    public bool IsActive() => isActive;
}
