using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class PauseMenuController : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject pauseMenuUI;       // main pause list panel
    [SerializeField] private Button[] menuButtons;
    [SerializeField] private Button cognitionButton;

    // Panel that visually contains the board (canvas/panel)
    [SerializeField] private GameObject cognitionBoardUI;

    // The actual board behaviour (may be on a child that auto-disables in Awake)
    [SerializeField] private CognitionBoard cognitionBoard;

    [Header("Selection Colors")]
    [SerializeField] private Color selectedColor = Color.yellow;
    [SerializeField] private Color defaultColor = Color.white;

    [Header("Input (UI action map)")]
    [SerializeField] private InputActionAsset inputActions;

    // NEW: gameplay map name + cached map (your gameplay map is called "Player")
    [Header("Gameplay Action Map")]
    [Tooltip("Name of the gameplay action map to disable while paused.")]
    [SerializeField] private string gameplayMapName = "Player";
    private InputActionMap gameplayMap; // NEW

    private InputActionMap uiActionMap;
    private InputAction navigateAction;
    private InputAction submitAction;
    private InputAction cancelAction;

    private int currentSelectionIndex = 0;

    // Keep existing instance flags…
    private bool isPaused = false;
    private bool isMenuActive = false;
    private bool isInSubMenu = false;

    // NEW: global pause flag other systems can query safely
    public static bool IsPaused { get; private set; } = false;

    private float lastDPadVertical = 0f;
    private float dpadHoldStartTime = 0f;
    [SerializeField] private float moveCooldown = 0.2f;

    void Awake()
    {
        uiActionMap    = inputActions?.FindActionMap("UI", true);
        navigateAction = uiActionMap?.FindAction("Navigate");
        submitAction   = uiActionMap?.FindAction("Submit");
        cancelAction   = uiActionMap?.FindAction("Cancel");

        // NEW: cache the gameplay map ("Player")
        gameplayMap    = inputActions?.FindActionMap(gameplayMapName, false);
        if (gameplayMap == null)
        {
            Debug.LogWarning($"[PauseMenu] Could not find gameplay map '{gameplayMapName}'. " +
                             $"World interactions may remain enabled while paused.");
        }

        uiActionMap?.Disable();

        if (pauseMenuUI)       pauseMenuUI.SetActive(false);
        if (cognitionBoardUI)  cognitionBoardUI.SetActive(false);
        // NOTE: cognitionBoard itself may be inactive because its Awake() sets it false—that’s OK.
        UpdateButtonColors();
    }

    void OnEnable()
    {
        if (navigateAction != null) navigateAction.performed += OnNavigate;
        if (submitAction   != null) submitAction.performed   += OnSubmit;
        if (cancelAction   != null) cancelAction.performed   += OnCancel;
    }

    void OnDisable()
    {
        if (navigateAction != null) navigateAction.performed -= OnNavigate;
        if (submitAction   != null) submitAction.performed   -= OnSubmit;
        if (cancelAction   != null) cancelAction.performed   -= OnCancel;

        uiActionMap?.Disable();
        // NOTE: we do NOT touch gameplayMap here; resume handles re-enabling.
    }

    void Update()
    {
        // Toggle pause (Esc or Start)
        if (Keyboard.current?.escapeKey.wasPressedThisFrame == true ||
            Gamepad.current?.startButton.wasPressedThisFrame == true)
        {
            if (isPaused) ResumeGame();
            else PauseGame();
        }

        if (isPaused && isMenuActive && !isInSubMenu)
            HandleDPadNavigation();
    }

    private void HandleDPadNavigation()
    {
        Vector2 dpadInput = navigateAction?.ReadValue<Vector2>() ?? Vector2.zero;
        float deadZone = 0.5f;
        float currentDPadY = dpadInput.y;
        float currentTime = Time.unscaledTime;
        bool shouldMove = false;

        if (Mathf.Abs(currentDPadY) > deadZone && menuButtons != null && menuButtons.Length > 0)
        {
            float direction = Mathf.Sign(currentDPadY);
            if (direction != lastDPadVertical && lastDPadVertical != 0f)
            {
                shouldMove = true;
                lastDPadVertical = direction;
                dpadHoldStartTime = currentTime;
            }
            else if (direction == lastDPadVertical && currentTime - dpadHoldStartTime > moveCooldown)
            {
                shouldMove = true;
                dpadHoldStartTime = currentTime;
            }
            else if (lastDPadVertical == 0f)
            {
                shouldMove = true;
                lastDPadVertical = direction;
                dpadHoldStartTime = currentTime;
            }
        }
        else
        {
            lastDPadVertical = 0f;
            dpadHoldStartTime = 0f;
        }

        if (shouldMove && menuButtons != null && menuButtons.Length > 0)
        {
            if (currentDPadY > deadZone)
                currentSelectionIndex = (currentSelectionIndex - 1 + menuButtons.Length) % menuButtons.Length;
            else if (currentDPadY < -deadZone)
                currentSelectionIndex = (currentSelectionIndex + 1) % menuButtons.Length;

            UpdateButtonColors();
        }
    }

    private void OnNavigate(InputAction.CallbackContext context) { /* optional */ }

    private void OnSubmit(InputAction.CallbackContext context)
    {
        if (!isPaused || !isMenuActive) return;
        if (isInSubMenu) return; // submit is handled by the sub menu (if any)

        if (menuButtons != null && menuButtons.Length > 0)
        {
            Button currentButton = menuButtons[currentSelectionIndex];
            if (!currentButton)
            {
                Debug.LogWarning($"[PauseMenu] Button at index {currentSelectionIndex} is null.");
                return;
            }

            if (currentButton == cognitionButton) OpenCognition();
            else currentButton.onClick.Invoke();
        }
    }

    public void PauseGame()
    {
        CloseDialogueIfOpen();

        if (pauseMenuUI)      pauseMenuUI.SetActive(true);
        if (cognitionBoardUI) cognitionBoardUI.SetActive(false);

        Time.timeScale = 0f;
        isPaused = true;
        IsPaused = true;     // NEW: update static flag
        isMenuActive = true;
        isInSubMenu = false;

        currentSelectionIndex = 0;
        UpdateButtonColors();

        // Swap maps: disable gameplay, enable UI
        gameplayMap?.Disable(); // NEW: prevents world Interact while paused
        uiActionMap?.Enable();

        ResetNavState();
    }

    public void ResumeGame()
    {
        // If board sub-menu is open, close it first to avoid leaving it enabled
        if (isInSubMenu) CloseCognition();

        if (pauseMenuUI) pauseMenuUI.SetActive(false);
        Time.timeScale = 1f;
        isPaused = false;
        IsPaused = false;    // NEW: update static flag
        isMenuActive = false;

        // Swap back: disable UI, enable gameplay
        uiActionMap?.Disable();
        gameplayMap?.Enable(); // NEW

        ResetNavState();
    }

    private void ResetNavState()
    {
        lastDPadVertical = 0f;
        dpadHoldStartTime = 0f;
    }

    public void OpenCognition()
    {
        if (!isPaused) PauseGame(); // safety

        // Hide the button list while board is up (optional; keeps UI clean)
        if (pauseMenuUI) pauseMenuUI.SetActive(false);

        // Enable the container panel
        if (cognitionBoardUI) cognitionBoardUI.SetActive(true);

        // IMPORTANT: explicitly enable the board object too (it disables itself in Awake)
        if (cognitionBoard && !cognitionBoard.gameObject.activeSelf)
        {
            cognitionBoard.gameObject.SetActive(true);
            // Optional: refresh layout from save when opening
            cognitionBoard.RestoreLayoutFromSave();
        }

        // Clear UI selection so Submit doesn't trigger menu buttons behind the board
        if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);

        isInSubMenu = true;
        Debug.Log("[PauseMenu] Cognition Board opened.");
    }

    public void CloseCognition()
    {
        if (cognitionBoardUI) cognitionBoardUI.SetActive(false);
        if (cognitionBoard)   cognitionBoard.gameObject.SetActive(false);

        // Return to the pause menu list
        if (pauseMenuUI) pauseMenuUI.SetActive(true);

        isInSubMenu = false;
        currentSelectionIndex = 0;
        UpdateButtonColors();
        Debug.Log("[PauseMenu] Cognition Board closed.");
    }

    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void UpdateButtonColors()
    {
        if (menuButtons == null || menuButtons.Length == 0) return;

        for (int i = 0; i < menuButtons.Length; i++)
        {
            var btn = menuButtons[i];
            if (!btn)
            {
                Debug.LogWarning($"[PauseMenu] Button at index {i} is null.");
                continue;
            }

            TMP_Text txt = btn.GetComponentInChildren<TMP_Text>();
            if (txt) txt.color = (i == currentSelectionIndex) ? selectedColor : defaultColor;
        }
    }

    private void OnCancel(InputAction.CallbackContext context)
    {
        if (!isPaused || !isMenuActive) return;

        if (isInSubMenu) CloseCognition();
        else ResumeGame();
    }

    // ---- Helper to avoid static calls ----
    private static void CloseDialogueIfOpen()
    {
        var dm = DialogueManager.Instance;
        if (dm != null && dm.IsActive()) dm.Hide();
    }
}
