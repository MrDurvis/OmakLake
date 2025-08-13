using UnityEngine;
using UnityEngine.SceneManagement;

public class DoorInteraction : MonoBehaviour
{
    [SerializeField]
    public GameObject doorObject; // Assign your door object with Animator

    [SerializeField]
    private GameObject playerInputController; // Assign the player input/controller object that should be disabled during cutscene

    [SerializeField]
    private CutsceneManager cutsceneManager; // Assign your scene manager object here

    private DoorTriggerZone doorTriggerZone;

    [SerializeField]
    private string nextSceneName; // Set your next scene's name here

    private Animator doorAnimator;
    public static bool isPlayerNear = false;

    void Start()
    {
        if (doorObject != null)
        {
            doorAnimator = doorObject.GetComponent<Animator>();
            if (doorAnimator == null)
            {
                Debug.LogError("Animator component not found on the door object.", this);
            }
        }
        else
        {
            Debug.LogError("doorObject not assigned.");
        }

        if (cutsceneManager == null)
        {
            Debug.LogError("CutsceneManager not assigned.");
        }

        if (playerInputController == null)
        {
            Debug.LogError("Player input controller not assigned.");
        }
        if (isPlayerNear && (Input.GetKeyDown(KeyCode.JoystickButton0) || Input.GetKeyDown(KeyCode.Return)))
        {
            // Call the sequence
            if (doorTriggerZone != null)
            {
                doorTriggerZone.ActivateDoorSequence();
            }
            else
            {
                Debug.LogError("doorTriggerZone instance not assigned.");
            }
        }
    }

    // Called by Trigger Zone script when player enters
    public void OnPlayerEnterRange()
    {
        isPlayerNear = true;
        Debug.Log("isPlayerNear=True");
    }

    // Called by Trigger Zone script when player leaves
    public void OnPlayerExitRange()
    {
        isPlayerNear = false;
        Debug.Log("isPlayerNear=False");
    }

    void Update()
    {
        // Check for button press (Joystick 'A' or Return key)
        if (isPlayerNear && ((Input.GetKeyDown(KeyCode.JoystickButton0) || Input.GetKeyDown(KeyCode.Return))))
        {
            Debug.Log("Button detected, triggering door and cutscene...");
            if (doorAnimator != null)
            {
                doorAnimator.SetTrigger("Open");
                Debug.Log("Door 'Open' trigger set");
            }

            // Trigger the cutscene, disable input, and transition
            if (cutsceneManager != null)
            {
                cutsceneManager.PlayCutscene(() =>
                {
                    // Callback after cutscene completes, load new scene
                    SceneManager.LoadScene(nextSceneName);

                    isPlayerNear = false;
                });
            }
        }
    }
}