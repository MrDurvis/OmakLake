using UnityEngine;
using TMPro;
using UnityEngine.InputSystem;
using System.Collections;
using System.Collections.Generic;
using System.Text;

public class DialogueManager : MonoBehaviour
{
    public static DialogueManager Instance { get; private set; }
    public static event System.Action<bool> OnDialogActiveChanged;

    [Header("UI")]
    [SerializeField] private GameObject dialogueUI;
    [SerializeField] private TMP_Text dialogueText;

    [Header("Inputs")]
    [SerializeField] private InputActionReference submitAction;   // A / UI-Submit
    [SerializeField] private InputActionReference cancelAction;   // B / UI-Cancel
    [SerializeField] private InputActionReference navigateAction; // UI-Navigate (for choices)

    [Header("Actions to disable while dialogue is open")]
    [SerializeField] private List<InputActionReference> disableWhileOpen = new();

    [Header("Typewriter")]
    [SerializeField] private float charInterval     = 0.02f;
    [SerializeField] private float shortPunctPause  = 0.06f;  // , ; : — – ) \n
    [SerializeField] private float longPunctPause   = 0.18f;  // . ! ?
    [SerializeField] private float minShowSeconds   = 0.15f;

    [Header("Choice UI Style")]
    [Tooltip("Optional prefab for choice labels. If left null, labels are created at runtime and styled from dialogueText.")]
    [SerializeField] private TextMeshProUGUI choiceTextPrefab;

    // -------- state --------
    private bool isVisible;
    private float shownAt;
    private bool waitForSubmitRelease;
    private readonly List<InputAction> reenableBuffer = new();

    private Coroutine typingCo;
    private bool isTyping;
    private bool fastForwardRequested;

    // sequence
    private Coroutine sequenceCo;
    private System.Action<SequenceResult> onSequenceComplete;

    // choices
    private RectTransform choiceRow;
    private readonly List<TMP_Text> choiceLabels = new();
    private int selectedIndex;
    private int choiceResultIndex = -1;
    private float nextNavTime;
    private const float NAV_REPEAT = 0.15f;

    private const string SHORT_SET = ",;:—–)\n";
    private const string LONG_SET  = ".!?";

    private void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (!dialogueUI)  Debug.LogError("[DialogueManager] dialogueUI not assigned.");
        if (!dialogueText) Debug.LogError("[DialogueManager] dialogueText not assigned.");

        if (dialogueUI) dialogueUI.SetActive(false);

        // Make sure the main dialogue text uses a LOCAL material instance (not a shared preset)
        DecoupleMaterial(dialogueText);
    }

    private void OnEnable()
    {
        if (submitAction?.action != null)
        {
            submitAction.action.started   += OnSubmitStarted;
            submitAction.action.performed += OnSubmitPerformed;
            submitAction.action.canceled  += OnSubmitCanceled;
            submitAction.action.Enable();
        }
        if (cancelAction?.action != null)
        {
            cancelAction.action.started   += OnCancelStarted;
            cancelAction.action.performed += OnCancelPerformed;
            cancelAction.action.Enable();
        }
        if (navigateAction?.action != null)
        {
            navigateAction.action.Enable();
        }
    }

    private void OnDisable()
    {
        if (submitAction?.action != null)
        {
            submitAction.action.started   -= OnSubmitStarted;
            submitAction.action.performed -= OnSubmitPerformed;
            submitAction.action.canceled  -= OnSubmitCanceled;
            submitAction.action.Disable();
        }
        if (cancelAction?.action != null)
        {
            cancelAction.action.started   -= OnCancelStarted;
            cancelAction.action.performed -= OnCancelPerformed;
            cancelAction.action.Disable();
        }
        if (navigateAction?.action != null) navigateAction.action.Disable();

        RestoreBlockedActions();
    }

    private void Update()
    {
        if (isVisible && IsShowingChoices())
        {
            Vector2 nav = navigateAction ? navigateAction.action.ReadValue<Vector2>() : Vector2.zero;
            float now = Time.unscaledTime;

            if (nav.x > 0.5f && now >= nextNavTime)
            {
                MoveChoice(+1);
                nextNavTime = now + NAV_REPEAT;
            }
            else if (nav.x < -0.5f && now >= nextNavTime)
            {
                MoveChoice(-1);
                nextNavTime = now + NAV_REPEAT;
            }
            if (Mathf.Abs(nav.x) < 0.25f) nextNavTime = 0f;
        }
    }

    // -------- Public API --------
    public void ShowDialogue(string message)
    {
        StartSequence(new List<DialogueBlock> {
            new DialogueBlock { type = BlockType.Text, text = message }
        }, null);
    }

    public void StartSequence(List<DialogueBlock> blocks, System.Action<SequenceResult> onComplete)
    {
        if (isVisible)
        {
            Debug.Log("[DialogueManager] Sequence requested while dialog open — ignoring.");
            return;
        }
        if (blocks == null || blocks.Count == 0) { Debug.LogWarning("[DialogueManager] Empty sequence."); return; }
        if (sequenceCo != null) StopCoroutine(sequenceCo);
        onSequenceComplete = onComplete;
        sequenceCo = StartCoroutine(RunSequence(blocks));
    }

    // Aliases used by PauseMenuController
    public bool IsActive() => isVisible;
    public bool IsDialogueActive() => isVisible;
    public void Hide() => ClosePanel();

    // -------- Sequence driver --------
    private IEnumerator RunSequence(List<DialogueBlock> blocks)
    {
        OpenPanel();

        SequenceResult result = new SequenceResult();

        for (int i = 0; i < blocks.Count; i++)
        {
            var b = blocks[i];

            if (b.type == BlockType.Text)
            {
                yield return ShowTextAndWait(b.text);
            }
            else // Choice
            {
                yield return ShowChoiceAndWait(b);
                result.lastChoice = b;
                result.lastChoiceIndex = choiceResultIndex;
                if (b.options != null &&
                    choiceResultIndex >= 0 &&
                    choiceResultIndex < b.options.Count)
                    result.lastChoiceText = b.options[choiceResultIndex];
            }
        }

        ClosePanel();
        onSequenceComplete?.Invoke(result);
        onSequenceComplete = null;
        sequenceCo = null;
    }

    // ---- Text step ----
    private IEnumerator ShowTextAndWait(string message)
    {
        PrepareForText();

        if (typingCo != null) StopCoroutine(typingCo);
        typingCo = StartCoroutine(Typewriter(message));
        while (isTyping) yield return null;

        yield return WaitForClosePress();
    }

    // ---- Choice step ----
    private IEnumerator ShowChoiceAndWait(DialogueBlock block)
    {
        choiceResultIndex = -1;

        string prompt = block.prompt ?? "";
        PrepareForText();

        if (typingCo != null) StopCoroutine(typingCo);
        typingCo = StartCoroutine(Typewriter(prompt));
        while (isTyping) yield return null;

        BuildChoices(block.options, block.defaultIndex);

        bool picked = false;
        while (!picked)
        {
            if (submitAction != null && submitAction.action.triggered)
            {
                choiceResultIndex = selectedIndex;
                picked = true;
            }
            else if (cancelAction != null && cancelAction.action.triggered)
            {
                choiceResultIndex = Mathf.Clamp(block.defaultIndex, 0, (block.options?.Count ?? 1) - 1);
                picked = true;
            }
            yield return null;
        }

        ClearChoices();
        yield return null;
    }

    // -------- Open/Close panel + locks --------
    private void OpenPanel()
    {
        EnsureDialogueInputsEnabled();

        dialogueText.text = string.Empty;
        dialogueUI.SetActive(true);

        isVisible = true;
        shownAt = Time.unscaledTime;
        waitForSubmitRelease = true;
        fastForwardRequested = false;

        BlockActions();
        OnDialogActiveChanged?.Invoke(true);
    }

    private void ClosePanel()
    {
        if (typingCo != null) { StopCoroutine(typingCo); typingCo = null; }
        isTyping = false;
        fastForwardRequested = false;

        ClearChoices();

        dialogueUI.SetActive(false);
        isVisible = false;

        RestoreBlockedActions();
        OnDialogActiveChanged?.Invoke(false);
    }

    // -------- Input handlers (A/B) --------
    private void OnSubmitStarted(InputAction.CallbackContext _)
    {
        if (!isVisible) return;

        // While typing, only allow fast-forward AFTER first release
        if (isTyping)
        {
            if (waitForSubmitRelease) return;
            fastForwardRequested = true;
            return;
        }

        if (waitForSubmitRelease) return;

        if (!IsShowingChoices()) TryCloseBubble();
    }
    private void OnSubmitPerformed(InputAction.CallbackContext _) { }
    private void OnSubmitCanceled (InputAction.CallbackContext _) { if (isVisible) waitForSubmitRelease = false; }

    private void OnCancelStarted(InputAction.CallbackContext _)
    {
        if (!isVisible) return;

        if (isTyping)
        {
            if (waitForSubmitRelease) return;
            fastForwardRequested = true;
            return;
        }

        if (waitForSubmitRelease) return;

        if (!IsShowingChoices()) TryCloseBubble();
    }
    private void OnCancelPerformed(InputAction.CallbackContext _) { }

    private void TryCloseBubble()
    {
        if (Time.unscaledTime - shownAt < minShowSeconds) return;
        // Closed by WaitForClosePress
    }

    private IEnumerator WaitForClosePress()
    {
        float earliest = Time.unscaledTime + minShowSeconds;
        while (Time.unscaledTime < earliest) yield return null;

        bool pressed = false;
        while (!pressed)
        {
            if ((submitAction != null && submitAction.action.triggered) ||
                (cancelAction  != null && cancelAction.action.triggered))
                pressed = true;
            yield return null;
        }
    }

    // -------- Typewriter with punctuation pauses --------
    private IEnumerator Typewriter(string msg)
    {
        isTyping = true;
        fastForwardRequested = false;     // start normal speed
        dialogueText.text = string.Empty;

        var sb = new StringBuilder(msg.Length + 16);
        int i = 0;

        while (i < msg.Length)
        {
            char c = msg[i];

            // Preserve rich-text tags intact
            if (c == '<')
            {
                int close = msg.IndexOf('>', i);
                if (close >= 0)
                {
                    sb.Append(msg, i, close - i + 1);
                    dialogueText.text = sb.ToString();
                    i = close + 1;
                    continue;
                }
            }

            // Ellipsis token "..."
            if (c == '.' && i + 2 < msg.Length && msg[i + 1] == '.' && msg[i + 2] == '.')
            {
                sb.Append("...");
                dialogueText.text = sb.ToString();
                i += 3;
                if (!fastForwardRequested) yield return PauseRealtime(longPunctPause);
                continue;
            }

            // Append single char
            sb.Append(c);
            dialogueText.text = sb.ToString();
            i++;

            // Per-character delay
            if (!fastForwardRequested && charInterval > 0f)
                yield return new WaitForSecondsRealtime(charInterval);

            // Punctuation pauses
            if (!fastForwardRequested)
            {
                if (c == '\n')                      yield return PauseRealtime(shortPunctPause);
                else if (LONG_SET.IndexOf(c) >= 0)  yield return PauseRealtime(longPunctPause);
                else if (SHORT_SET.IndexOf(c) >= 0) yield return PauseRealtime(shortPunctPause);
            }
        }

        isTyping = false;
        typingCo = null;
        fastForwardRequested = false; // ready for next bubble
    }

    private IEnumerator PauseRealtime(float seconds)
    {
        if (seconds <= 0f) yield break;
        float end = Time.unscaledTime + seconds;
        while (Time.unscaledTime < end)
        {
            if (fastForwardRequested) yield break;
            yield return null;
        }
    }

    private void PrepareForText()
    {
        dialogueText.text = string.Empty;
        shownAt = Time.unscaledTime;
        waitForSubmitRelease = true;
        fastForwardRequested = false;
        ClearChoices();
    }

    // -------- Choice UI helpers --------
    private bool IsShowingChoices() => choiceRow != null && choiceRow.gameObject.activeSelf;

    private void BuildChoices(List<string> options, int startIndex)
    {
        if (options == null || options.Count == 0) options = new List<string> { "OK" };

        if (choiceRow == null)
        {
            var rowGO = new GameObject("ChoiceRow", typeof(RectTransform));
            choiceRow = rowGO.GetComponent<RectTransform>();
            choiceRow.SetParent(dialogueUI.transform, false);
            choiceRow.anchorMin = new Vector2(0.5f, 0);
            choiceRow.anchorMax = new Vector2(0.5f, 0);
            choiceRow.pivot     = new Vector2(0.5f, 0);
            choiceRow.anchoredPosition = new Vector2(0, 20);
            choiceRow.sizeDelta = new Vector2(600, 40);
        }

        foreach (var t in choiceLabels) if (t) Destroy(t.gameObject);
        choiceLabels.Clear();

        float spacing = 180f;
        float startX = -spacing * (options.Count - 1) * 0.5f;

        for (int i = 0; i < options.Count; i++)
        {
            TextMeshProUGUI tm;

            if (choiceTextPrefab != null)
            {
                tm = Instantiate(choiceTextPrefab, choiceRow);
            }
            else
            {
                var go = new GameObject("Opt_" + i, typeof(RectTransform), typeof(TextMeshProUGUI));
                tm = go.GetComponent<TextMeshProUGUI>();
                tm.rectTransform.SetParent(choiceRow, false);

                // Copy readable style (size, alignment, vertex color, etc.)
                CopyTextStyle(dialogueText, tm);
            }

            // <<< Ensure choice uses dialogue font + a LOCAL clone of its preset >>>
            ApplyDialogueMaterial(tm);

            // Position
            var r = tm.rectTransform;
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.pivot = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(160, 40);
            r.anchoredPosition = new Vector2(startX + spacing * i, 0);

            // Content
            tm.alignment = TextAlignmentOptions.Center;
            tm.text = options[i] ?? "";

            choiceLabels.Add(tm);
        }

        choiceRow.gameObject.SetActive(true);
        selectedIndex = Mathf.Clamp(startIndex, 0, options.Count - 1);
        RefreshChoiceVisuals();
        nextNavTime = 0f;
    }

    private void ClearChoices()
    {
        if (choiceRow != null) choiceRow.gameObject.SetActive(false);
    }

    private void MoveChoice(int delta)
    {
        if (!IsShowingChoices()) return;
        selectedIndex = Mathf.Clamp(selectedIndex + delta, 0, choiceLabels.Count - 1);
        RefreshChoiceVisuals();
    }

    private void RefreshChoiceVisuals()
    {
        for (int i = 0; i < choiceLabels.Count; i++)
        {
            var tm = choiceLabels[i];
            if (!tm) continue;
            string raw = tm.text.Replace("<b>", "").Replace("</b>", "").Replace("<u>", "").Replace("</u>", "");
            tm.text = (i == selectedIndex) ? $"<b><u>{raw}</u></b>" : raw;
        }
    }

    // -------- Input gating --------
    private void BlockActions()
    {
        reenableBuffer.Clear();

        foreach (var aref in disableWhileOpen)
        {
            var action = aref?.action;
            if (action == null) continue;

            if (submitAction != null && action == submitAction.action) continue;
            if (cancelAction != null && action == cancelAction.action) continue;
            if (navigateAction != null && action == navigateAction.action) continue;

            if (action.enabled)
            {
                action.Disable();
                reenableBuffer.Add(action);
            }
        }
    }

    private void RestoreBlockedActions()
    {
        foreach (var a in reenableBuffer)
            if (a != null && !a.enabled) a.Enable();
        reenableBuffer.Clear();
    }

    // --- ensure Submit/Cancel/Navigate are enabled when opening the dialog ---
    private void EnsureDialogueInputsEnabled()
    {
        try { submitAction?.action?.Enable(); } catch {}
        try { cancelAction?.action?.Enable(); } catch {}
        try { navigateAction?.action?.Enable(); } catch {}

        try { submitAction?.action?.actionMap?.Enable(); } catch {}
        try { cancelAction?.action?.actionMap?.Enable(); } catch {}
        try { navigateAction?.action?.actionMap?.Enable(); } catch {}
    }

    // --- style helpers ---
    private void CopyTextStyle(TMP_Text from, TMP_Text to)
    {
        if (from == null || to == null) return;

        to.font               = from.font;
        to.fontSize           = from.fontSize;
        to.enableAutoSizing   = from.enableAutoSizing;
        to.fontStyle          = from.fontStyle;
        to.alignment          = from.alignment;
        to.color              = from.color; // vertex color (per-instance)
        to.textWrappingMode = from.textWrappingMode;
        to.richText           = from.richText;
        to.lineSpacing        = from.lineSpacing;
        to.wordSpacing        = from.wordSpacing;
        to.characterSpacing   = from.characterSpacing;
        to.overflowMode       = from.overflowMode;
        to.margin             = from.margin;

        if (to is TextMeshProUGUI ugui) ugui.raycastTarget = false;
    }

    // Make sure a TMP label uses the SAME font + a LOCAL clone of the dialogue's preset
    private void ApplyDialogueMaterial(TMP_Text t)
    {
        if (t == null || dialogueText == null) return;

        // Make sure the font asset matches
        t.font = dialogueText.font;

        // Clone the preset used by the dialogue text so this label has its own local instance
        var sharedPreset = dialogueText.fontSharedMaterial;
        if (sharedPreset != null)
            t.fontMaterial = new Material(sharedPreset);

        // also match vertex color (safe)
        t.color = dialogueText.color;
    }

    // Ensure our dialogue text itself is not editing a shared preset at runtime
    private void DecoupleMaterial(TMP_Text t)
    {
        if (t == null) return;
        var shared = t.fontSharedMaterial;
        var local  = t.fontMaterial;
        if (ReferenceEquals(shared, local))
            t.fontMaterial = new Material(shared); // unique per-instance
    }
}

public class SequenceResult
{
    public DialogueBlock lastChoice;
    public int lastChoiceIndex = -1;
    public string lastChoiceText;
}
