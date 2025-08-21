using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class ClueInfoPanel : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CanvasGroup group;
    [SerializeField] private RectTransform container;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text bodyText;

    [Tooltip("Separate child Image used only for the clue thumbnail. Leave null to ignore icons.")]
    [SerializeField] private Image iconImage;

    [Header("Background (card)")]
    [Tooltip("Background Image on the same GameObject (the card). If left empty, we'll use GetComponent<Image>().")]
    [SerializeField] private Image backgroundImage;
    private Sprite backgroundSpriteAtStart;

    [Header("Icon")]
    [SerializeField] private bool showIcon = false;
    [SerializeField] private bool hideIconGameObjectWhenUnused = true;

    [Header("Typography")]
    [SerializeField] private bool   titleAutoSize   = true;
    [SerializeField] private float  titleSizeDefault = 64f;
    [SerializeField] private float  titleSizeMin     = 36f;
    [SerializeField] private bool   bodyAutoSize    = true;
    [SerializeField] private float  bodySizeDefault  = 36f;
    [SerializeField] private float  bodySizeMin      = 26f;
    [SerializeField] private TextOverflowModes titleOverflow = TextOverflowModes.Truncate;
    [SerializeField] private TextOverflowModes bodyOverflow  = TextOverflowModes.Truncate;

    [Header("Animation")]
    [SerializeField] private float slideInDistance = 400f;
    [SerializeField] private float slideInDuration  = 0.25f;
    [SerializeField] private float slideOutDuration = 0.18f;
    [SerializeField] private float charsPerSecond   = 80f;
    [SerializeField] private bool  retriggerTypeOnRefresh = true;

    public bool IsVisible { get; private set; }

    // NOTE: expose typing state so the board can guard input properly
    public bool IsTyping { get; private set; } = false;                       // NOTE
    public void CompleteTyping()                                              // NOTE
    {
        if (typeRoutine != null) { StopCoroutine(typeRoutine); typeRoutine = null; }
        if (bodyText) bodyText.maxVisibleCharacters = int.MaxValue;
        IsTyping = false;
    }

    private Vector2 baseAnchoredPos;
    private Coroutine showRoutine;
    private Coroutine typeRoutine;

    // queued state when inactive
    private ClueData queuedData;
    private bool queuedVisible;

    private void Awake()
    {
        if (!group)     group = GetComponent<CanvasGroup>();
        if (!container) container = GetComponent<RectTransform>();
        if (!backgroundImage) backgroundImage = GetComponent<Image>();

        baseAnchoredPos = container ? container.anchoredPosition : Vector2.zero;

        if (backgroundImage) backgroundSpriteAtStart = backgroundImage.sprite;

        // Guard: icon must be a separate child image
        if (iconImage && backgroundImage && iconImage == backgroundImage)
        {
            Debug.LogWarning("[ClueInfoPanel] Icon Image was the same as Background Image. Clearing Icon Image reference.", this);
            iconImage = null;
        }

        ApplyTypography();

        if (group) group.alpha = 0f;
        IsVisible = false;
        ApplyIconVisible(false);
    }

    private void LateUpdate()
    {
        if (backgroundImage && backgroundImage.sprite != backgroundSpriteAtStart)
            backgroundImage.sprite = backgroundSpriteAtStart;
    }

    private void OnEnable()
    {
        if (queuedVisible)
        {
            ApplyShowImmediate();
            queuedVisible = false;
        }
    }

    private void OnDisable()
    {
        if (showRoutine != null) { StopCoroutine(showRoutine); showRoutine = null; }
        if (typeRoutine != null) { StopCoroutine(typeRoutine); typeRoutine = null; }
        IsTyping = false; // NOTE
    }

    public void ShowFor(ClueData data, bool immediate)
    {
        if (!data) { Hide(immediate); return; }

        FillFromData(data, immediate);

        if (!isActiveAndEnabled || !gameObject.activeInHierarchy)
        {
            queuedVisible = true;
            ApplyShowImmediate();
            return;
        }

        if (showRoutine != null) StopCoroutine(showRoutine);
        showRoutine = StartCoroutine(ShowRoutine(immediate));
    }

    public void Hide(bool immediate)
    {
        if (!isActiveAndEnabled || !gameObject.activeInHierarchy)
        {
            queuedVisible = false;
            ApplyHideImmediate();
            return;
        }

        if (showRoutine != null) StopCoroutine(showRoutine);
        showRoutine = StartCoroutine(HideRoutine(immediate));
    }

    private void FillFromData(ClueData data, bool immediate)
    {
        if (titleText)
            titleText.text = string.IsNullOrWhiteSpace(data.clueName) ? "" : data.clueName;

        if (bodyText)
        {
            bodyText.textWrappingMode = TextWrappingModes.Normal;
            bodyText.text = string.IsNullOrWhiteSpace(data.description) ? "" : data.description;

            if (retriggerTypeOnRefresh && !immediate && isActiveAndEnabled && gameObject.activeInHierarchy)
            {
                if (typeRoutine != null) StopCoroutine(typeRoutine);
                typeRoutine = StartCoroutine(Typewriter(bodyText, charsPerSecond));
            }
            else
            {
                bodyText.maxVisibleCharacters = int.MaxValue;
                IsTyping = false; // NOTE
            }
        }

        if (showIcon && iconImage)
        {
            iconImage.sprite = data.icon;
            iconImage.preserveAspect = true;
            ApplyIconVisible(data.icon != null);
        }
        else
        {
            if (iconImage) iconImage.sprite = null;
            ApplyIconVisible(false);
        }

        if (backgroundImage && backgroundImage.sprite != backgroundSpriteAtStart)
            backgroundImage.sprite = backgroundSpriteAtStart;
    }

    private void ApplyTypography()
    {
        if (titleText)
        {
            titleText.enableAutoSizing = titleAutoSize;
            titleText.fontSizeMax      = titleSizeDefault;
            titleText.fontSizeMin      = titleSizeMin;
            titleText.fontSize         = titleSizeDefault;
            titleText.overflowMode     = titleOverflow;
        }

        if (bodyText)
        {
            bodyText.enableAutoSizing = bodyAutoSize;
            bodyText.fontSizeMax      = bodySizeDefault;
            bodyText.fontSizeMin      = bodySizeMin;
            bodyText.fontSize         = bodySizeDefault;
            bodyText.overflowMode     = bodyOverflow;
            bodyText.textWrappingMode = TextWrappingModes.Normal;
        }
    }

    private IEnumerator ShowRoutine(bool immediate)
    {
        IsVisible = true;

        Vector2 start = baseAnchoredPos + Vector2.right * slideInDistance;
        Vector2 end   = baseAnchoredPos;

        if (immediate) { ApplyShowImmediate(); yield break; }

        if (group) group.alpha = 0f;
        if (container) container.anchoredPosition = start;

        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.01f, slideInDuration);
            float e = Mathf.SmoothStep(0f, 1f, t);
            if (container) container.anchoredPosition = Vector2.LerpUnclamped(start, end, e);
            if (group)     group.alpha = e;
            yield return null;
        }

        ApplyShowImmediate();
    }

    private IEnumerator HideRoutine(bool immediate)
    {
        if (!IsVisible) yield break;
        IsVisible = false;

        Vector2 start = container ? container.anchoredPosition : baseAnchoredPos;
        Vector2 end   = baseAnchoredPos + Vector2.right * slideInDistance;

        if (immediate) { ApplyHideImmediate(); yield break; }

        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.01f, slideOutDuration);
            float e = Mathf.SmoothStep(0f, 1f, t);
            if (container) container.anchoredPosition = Vector2.LerpUnclamped(start, end, e);
            if (group)     group.alpha = 1f - e;
            yield return null;
        }

        ApplyHideImmediate();
    }

    private void ApplyShowImmediate()
    {
        if (container) container.anchoredPosition = baseAnchoredPos;
        if (group)     group.alpha = 1f;
        IsVisible = true;
    }

    private void ApplyHideImmediate()
    {
        if (container) container.anchoredPosition = baseAnchoredPos + Vector2.right * slideInDistance;
        if (group)     group.alpha = 0f;
        IsVisible = false;
    }

    private IEnumerator Typewriter(TMP_Text text, float cps)
    {
        if (!text) yield break;
        text.ForceMeshUpdate();
        int total = text.textInfo.characterCount;

        if (total <= 0 || cps <= 0f)
        {
            text.maxVisibleCharacters = int.MaxValue;
            IsTyping = false; // NOTE
            yield break;
        }

        text.maxVisibleCharacters = 0;
        float perChar = 1f / cps;
        float acc = 0f;
        int shown = 0;
        IsTyping = true; // NOTE

        while (shown < total)
        {
            acc += Time.unscaledDeltaTime;
            while (acc >= perChar && shown < total)
            {
                acc -= perChar;
                shown++;
                text.maxVisibleCharacters = shown;
            }
            yield return null;
        }

        text.maxVisibleCharacters = int.MaxValue;
        IsTyping = false; // NOTE
    }

    private void ApplyIconVisible(bool on)
    {
        if (!iconImage) return;
        iconImage.enabled = on;
        if (hideIconGameObjectWhenUnused)
            iconImage.gameObject.SetActive(on);
    }
}
