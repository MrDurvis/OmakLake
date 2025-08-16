using UnityEngine;
using TMPro;

/// <summary>
/// Locates your Dialogue UI (panel + TMP text) in the scene and binds it to DialogueManager.
/// Choose a locate strategy in the inspector. Safe to keep on a prefab/scene object.
/// </summary>
[DefaultExecutionOrder(-50)] // Bind early
public class DialogueUIBinder : MonoBehaviour
{
    public enum LocateMode
    {
        UseAssigned,   // Use serialized references as-is
        ByTag,         // Find panel by tag
        ByName,        // Find panel (and optionally text) by exact names
        DeepScan       // Scan canvases for the first TMP_Text
    }

    [Header("Binding Target")]
    [Tooltip("If null, will use DialogueManager.Instance.")]
    [SerializeField] private DialogueManager manager;

    [Header("UI References (optional)")]
    [Tooltip("Root of the dialogue UI (usually a panel under a Canvas).")]
    [SerializeField] private GameObject dialoguePanel;
    [Tooltip("TMP_Text used to render the dialogue text.")]
    [SerializeField] private TMP_Text dialogueText;

    [Header("Locate Strategy")]
    [SerializeField] private LocateMode locateStrategy = LocateMode.UseAssigned;

    [Tooltip("When LocateMode = ByTag, set the panel's tag here.")]
    [SerializeField] private string panelTag = "";

    [Tooltip("When LocateMode = ByName, exact name of the panel GameObject.")]
    [SerializeField] private string panelName = "DialogueUI";
    [Tooltip("When LocateMode = ByName, exact name of the TMP_Text child (optional).")]
    [SerializeField] private string textName = "DialogueText";

    [Header("Behavior")]
    [Tooltip("Automatically bind on Awake.")]
    [SerializeField] private bool bindOnAwake = true;
    [Tooltip("Clone the font material on the bound TMP_Text so changes don't affect shared presets.")]
    [SerializeField] private bool decoupleMaterial = true;
    [Tooltip("Ensure CanvasGroup/Canvas are interactable/visible after binding.")]
    [SerializeField] private bool ensureVisibility = true;
    [Tooltip("Print helpful logs.")]
    [SerializeField] private bool verboseLogs = true;

    private void Awake()
    {
        if (bindOnAwake) TryBindNow();
    }

    /// <summary>
    /// Attempts to locate the panel + text according to the chosen strategy and bind them to DialogueManager.
    /// </summary>
    [ContextMenu("DialogueUIBinder/Try Bind Now")]
    public void TryBindNow()
    {
        if (!manager) manager = DialogueManager.Instance;

        // 1) Resolve UI references according to strategy
        switch (locateStrategy)
        {
            case LocateMode.UseAssigned:
                // use existing serialized refs
                break;

            case LocateMode.ByTag:
                if (!dialoguePanel && !string.IsNullOrEmpty(panelTag))
                {
                    var tagged = GameObject.FindGameObjectWithTag(panelTag);
                    if (tagged) dialoguePanel = tagged;
                }
                break;

            case LocateMode.ByName:
                if (!dialoguePanel && !string.IsNullOrEmpty(panelName))
                {
                    var go = GameObject.Find(panelName);
                    if (go) dialoguePanel = go;
                }
                if (dialoguePanel && !dialogueText)
                {
                    TMP_Text found = null;
                    if (!string.IsNullOrEmpty(textName))
                    {
                        var tr = dialoguePanel.transform.Find(textName);
                        if (tr) found = tr.GetComponent<TMP_Text>();
                    }
                    if (!found) found = dialoguePanel.GetComponentInChildren<TMP_Text>(true);
                    dialogueText = found;
                }
                break;

            case LocateMode.DeepScan:
                if (!dialoguePanel || !dialogueText)
                {
                    // Find any Canvas with a TMP_Text child (includes inactive)
#if UNITY_2022_1_OR_NEWER
                    var canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
                    var canvases = Resources.FindObjectsOfTypeAll<Canvas>();
#endif
                    foreach (var c in canvases)
                    {
                        var t = c.GetComponentInChildren<TMP_Text>(true);
                        if (t != null)
                        {
                            dialoguePanel = c.gameObject;
                            dialogueText = t;
                            break;
                        }
                    }
                }
                break;
        }

        // 2) Fallback: if we have a panel but no text, search under panel
        if (dialoguePanel && !dialogueText)
            dialogueText = dialoguePanel.GetComponentInChildren<TMP_Text>(true);

        // 3) Early validation
        if (!dialoguePanel || !dialogueText)
        {
            if (verboseLogs)
                Debug.LogWarning($"[DialogueUIBinder] Binding failed. Panel={(dialoguePanel ? dialoguePanel.name : "null")}  Text={(dialogueText ? dialogueText.name : "null")}", this);
            return;
        }

        // 4) Optional: decouple font material so edits don't touch shared presets
        if (decoupleMaterial && dialogueText)
        {
            var shared = dialogueText.fontSharedMaterial;
            var local = dialogueText.fontMaterial;
            if (ReferenceEquals(shared, local))
                dialogueText.fontMaterial = new Material(shared);
        }

        // 5) Ensure UI is visible/interactive
        if (ensureVisibility && dialoguePanel)
        {
            var cg = dialoguePanel.GetComponent<CanvasGroup>();
            if (!cg) cg = dialoguePanel.AddComponent<CanvasGroup>();
            cg.alpha = 1f;
            cg.interactable = true;
            cg.blocksRaycasts = true;

            var canvas = dialoguePanel.GetComponentInParent<Canvas>(true);
            if (canvas) canvas.enabled = true;
        }

        // 6) Bind to manager
        if (!manager) manager = DialogueManager.Instance;
        if (manager)
        {
            manager.BindUI(dialoguePanel, dialogueText);
            if (verboseLogs)
                Debug.Log($"[DialogueUIBinder] Bound to manager. Panel={dialoguePanel.name}, Text={dialogueText.name}", this);
        }
        else
        {
            if (verboseLogs)
                Debug.LogWarning("[DialogueUIBinder] DialogueManager.Instance not found. Binding deferred.", this);
        }
    }

    /// <summary>Quick inspector helper to print current refs.</summary>
    [ContextMenu("DialogueUIBinder/Validate Local UI")]
    private void ValidateLocal()
    {
        Debug.Log($"[DialogueUIBinder] Panel={(dialoguePanel ? dialoguePanel.name : "null")}  Text={(dialogueText ? dialogueText.name : "null")}", this);
    }
}
