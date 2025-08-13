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

    private bool isInSubMenu = false; // Tracks if in a sub-menu like Cognition

    // Variables for adaptive analog stick scrolling
    private float stickHoldTime = 0f;
    [SerializeField] private float initialDelay = 0.5f;             // Starting delay (seconds)
    private float minDelay = 0.3f;                   // Minimal delay (fastest rate)
    private float delayReductionPerSecond = 0.2f;  // Speed up over time

    // Variables for D-pad edge detection and auto-repeat
    private float lastDPadVertical = 0f; // 1 for up, -1 for down, 0 for none
    private float dpadHoldStartTime = 0f;
    private float moveCooldown = 0.2f; // time between auto-moves while holding

    void Awake()
    {
        // Get the input action map for UI controls
        uiActionMap = inputActions.FindActionMap("UI", true);
        navigateAction = uiActionMap.FindAction("Navigate");
        submitAction = uiActionMap.FindAction("Submit");
        cancelAction = uiActionMap.FindAction("Cancel");

        // Subscribe to input callbacks
        navigateAction.performed += OnNavigate;
        submitAction.performed += OnSubmit;
        cancelAction.performed += OnCancel;
    }

    void Start()
    {
        pauseMenuUI.SetActive(false);
        UpdateButtonColors();

        // Start with UI controls disabled until paused
        uiActionMap.Disable();

        // Initialize variables
        stickHoldTime = 0f;
        lastDPadVertical = 0f;
        dpadHoldStartTime = 0f;
    }

    void Update()
    {
        // Toggle pause (Escape or Start button)
        if (Keyboard.current.escapeKey.wasPressedThisFrame || Gamepad.current.startButton.wasPressedThisFrame)
        {
            if (isPaused) ResumeGame();
            else PauseGame();
        }

        // --- Handle digital D-Pad navigation with edge detection and auto-repeat ---
        Vector2 dpadInput = navigateAction.ReadValue<Vector2>();
        float currentTime = Time.unscaledTime;

        float deadZone = 0.5f;
        bool shouldMove = false;
        float currentDPadY = dpadInput.y;

        if (Mathf.Abs(currentDPadY) > deadZone)
        {
            float direction = Mathf.Sign(currentDPadY);
            if (direction != lastDPadVertical)
            {
                // Edge detected: move once
                shouldMove = true;
                lastDPadVertical = direction;
                dpadHoldStartTime = currentTime;
            }
            else
            {
                // Same direction held
                if (currentTime - dpadHoldStartTime > moveCooldown)
                {
                    // Time to auto-repeat move
                    shouldMove = true;
                    dpadHoldStartTime = currentTime;
                }
            }
        }
        else
        {
            // No input
            lastDPadVertical = 0f;
            dpadHoldStartTime = 0f;
        }

        if (shouldMove)
        {
            if (currentDPadY > deadZone)
            {
                // Up
                currentSelectionIndex = (currentSelectionIndex - 1 + menuButtons.Length) % menuButtons.Length;
                UpdateButtonColors();
            }
            else if (currentDPadY < -deadZone)
            {
                // Down
                currentSelectionIndex = (currentSelectionIndex + 1) % menuButtons.Length;
                UpdateButtonColors();
            }
        }

        // --- Optional: Handle analog stick navigation with adaptive delay ---
        // (Your existing analog stick code can be handled separately if desired)
        // For now, we focus on D-Pad handling as per your reported issue.
    }

    private void OnNavigate(InputAction.CallbackContext context)
    {
        // Can be used for other input methods if needed
        // No changes required here for D-pad auto-repeat logic
    }

    private void OnSubmit(InputAction.CallbackContext context)
    {
        // Check if current button is the cognitionButton
        if (menuButtons[currentSelectionIndex] == cognitionButton)
        {
            OpenCognition();
        }
        else
        {
            // Optionally invoke the button onClick for other buttons
            menuButtons[currentSelectionIndex].onClick.Invoke();
        }
    }
    private void OnCancel(InputAction.CallbackContext context)
    {
        if (isPaused && isMenuActive)
        {
            if (isInSubMenu)
            {
                // Return to main pause menu, close sub-menu like Cognition
                CloseCognition();
            }
            else
            {
                // If in main menu, unpause the game
                ResumeGame();
            }
        }
    }

    void UpdateButtonColors()
    {
        for (int i = 0; i < menuButtons.Length; i++)
        {
            TMP_Text btnText = menuButtons[i].GetComponentInChildren<TMP_Text>();
            if (btnText != null)
            {
                btnText.color = (i == currentSelectionIndex) ? selectedColor : defaultColor;
            }
        }
    }

    public void PauseGame()
    {
        Time.timeScale = 0;
        pauseMenuUI.SetActive(true);
        isPaused = true;
        isMenuActive = true;
        currentSelectionIndex = 0;
        UpdateButtonColors();

        // Enable UI input controls
        uiActionMap.Enable();

        // Reset adaptive variables
        stickHoldTime = 0f;
        lastDPadVertical = 0f;
        dpadHoldStartTime = 0f;
    }

    public void ResumeGame()
    {
        Time.timeScale = 1;
        pauseMenuUI.SetActive(false);
        isPaused = false;
        isMenuActive = false;

        // Disable UI input controls
        uiActionMap.Disable();

        // Reset adaptive variables
        stickHoldTime = 0f;
        lastDPadVertical = 0f;
        dpadHoldStartTime = 0f;
    }

    public GameObject cognitionBoardUI; // Assign via inspector

    public void OpenCognition()
    {
        cognitionBoardUI.SetActive(true);
        isInSubMenu = true; // Now we're in a sub-menu
    }

    public void CloseCognition()
    {
        cognitionBoardUI.SetActive(false);
        isInSubMenu = false; // Back to main menu
    }

    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
    }
}