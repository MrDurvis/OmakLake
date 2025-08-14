using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem;

public class PauseMenuController : MonoBehaviour
{
    public GameObject pauseMenuUI;
    public Button[] menuButtons;
    public Color selectedColor = Color.yellow;
    public Color defaultColor = Color.white;
    public InputActionAsset inputActions;

    public Button cognitionButton;

    private InputActionMap uiActionMap;
    private InputAction navigateAction;
    private InputAction submitAction;
    private InputAction cancelAction;

    private int currentSelectionIndex = 0;
    private bool isPaused = false;
    private bool isMenuActive = false;

    private bool isInSubMenu = false;

    public GameObject cognitionBoardUI;

    private float lastDPadVertical = 0f;
    private float dpadHoldStartTime = 0f;
    private float moveCooldown = 0.2f;

    void Awake()
    {
        uiActionMap = inputActions?.FindActionMap("UI", true);
        navigateAction = uiActionMap?.FindAction("Navigate");
        submitAction = uiActionMap?.FindAction("Submit");
        cancelAction = uiActionMap?.FindAction("Cancel");

        if (navigateAction != null)
            navigateAction.performed += OnNavigate;
        if (submitAction != null)
            submitAction.performed += OnSubmit;
        if (cancelAction != null)
            cancelAction.performed += OnCancel;
    }

    void Start()
    {
        if (pauseMenuUI != null)
            pauseMenuUI.SetActive(false);
        else
            Debug.LogWarning("pauseMenuUI is not assigned.");

        UpdateButtonColors();

        uiActionMap?.Disable();

        isPaused = false;
        isMenuActive = false;

        if (menuButtons == null || menuButtons.Length == 0)
            Debug.LogWarning("menuButtons array is not assigned or empty.");

        lastDPadVertical = 0f;
        dpadHoldStartTime = 0f;
    }

    void Update()
    {
        if (Keyboard.current.escapeKey.wasPressedThisFrame || Gamepad.current?.startButton.wasPressedThisFrame == true)
        {
            if (isPaused) ResumeGame();
            else PauseGame();
        }

        if (!isPaused || !isMenuActive)
            return;

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
                // Edge detected: move once
                shouldMove = true;
                lastDPadVertical = direction;
                dpadHoldStartTime = currentTime;
            }
            else if (direction == lastDPadVertical && currentTime - dpadHoldStartTime > moveCooldown)
            {
                // Auto-repeat
                shouldMove = true;
                dpadHoldStartTime = currentTime;
            }
            else if (lastDPadVertical == 0f)
            {
                // First press
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
            {
                // Move selection up
                currentSelectionIndex = (currentSelectionIndex - 1 + menuButtons.Length) % menuButtons.Length;
                UpdateButtonColors();
            }
            else if (currentDPadY < -deadZone)
            {
                // Move selection down
                currentSelectionIndex = (currentSelectionIndex + 1) % menuButtons.Length;
                UpdateButtonColors();
            }
        }
    }

    private void OnNavigate(InputAction.CallbackContext context)
    {
        // Additional input methods can be handled here
    }
    private void OnSubmit(InputAction.CallbackContext context)
    {
        // Only proceed if game is paused and menu is active
        if (!isPaused || !isMenuActive)
            return;

        if (menuButtons != null && menuButtons.Length > 0)
        {
            Button currentButton = menuButtons[currentSelectionIndex];

            if (currentButton != null)
            {
                if (currentButton == cognitionButton)
                {
                    OpenCognition();
                }
                else
                {
                    // Invoke the button's onClick event
                    currentButton.onClick.Invoke();
                }
            }
            else
            {
                Debug.LogWarning($"Button at index {currentSelectionIndex} is null.");
            }
        }
    }

    public void PauseGame()
    {
        if (pauseMenuUI != null)
            pauseMenuUI.SetActive(true);
        Time.timeScale = 0;
        isPaused = true;
        isMenuActive = true;
        currentSelectionIndex = 0;
        UpdateButtonColors();

        uiActionMap?.Enable();

        // Reset variables
        lastDPadVertical = 0f;
        dpadHoldStartTime = 0f;
    }

    public void ResumeGame()
    {
        if (pauseMenuUI != null)
            pauseMenuUI.SetActive(false);
        Time.timeScale = 1;
        isPaused = false;
        isMenuActive = false;

        uiActionMap?.Disable();

        // Reset variables
        lastDPadVertical = 0f;
        dpadHoldStartTime = 0f;
    }

    public void OpenCognition()
    {
        if (cognitionBoardUI != null)
            cognitionBoardUI.SetActive(true);
        isInSubMenu = true;
    }

    public void CloseCognition()
    {
        if (cognitionBoardUI != null)
            cognitionBoardUI.SetActive(false);
        isInSubMenu = false;
    }

    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
    Application.Quit();
#endif
    }

    /// <summary>
    /// Updates the button label colors based on current selection.
    /// Checks for null references to avoid exceptions.
    /// </summary>
    private void UpdateButtonColors()
    {
        if (menuButtons == null || menuButtons.Length == 0)
            return;

        for (int i = 0; i < menuButtons.Length; i++)
        {
            if (menuButtons[i] != null)
            {
                TMP_Text btnText = menuButtons[i].GetComponentInChildren<TMP_Text>();
                if (btnText != null)
                {
                    btnText.color = (i == currentSelectionIndex) ? selectedColor : defaultColor;
                }
                else
                {
                    Debug.LogWarning($"TMP_Text component not found in children of button at index {i}.");
                }
            }
            else
            {
                Debug.LogWarning($"Button at index {i} is null.");
            }
        }
    }
    private void OnCancel(InputAction.CallbackContext context)
{
    // Check if the game is paused and menu is active
    if (isPaused && isMenuActive)
    {
        if (isInSubMenu)
        {
            CloseCognition();
        }
        else
        {
            ResumeGame();
        }
    }
}
}