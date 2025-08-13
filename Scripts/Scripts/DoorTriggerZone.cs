using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

public class DoorTriggerZone : MonoBehaviour
{
    [SerializeField]
    private GameObject doorObject; // Door with Animator and DoorInteraction
    
    [SerializeField]
    private GameObject player; // Reference to the Player GameObject
    
    [SerializeField]
    private GameObject cutsceneManagerObject; // Contains your CutsceneManager
    
    [SerializeField]
    private string nextSceneName = "NextScene"; // Scene to load after the cutscene
    
    [SerializeField]
    private float cutsceneDuration = 3f; // How long the door animation/cutscene lasts
    
    [SerializeField]
    private Transform targetPoint; // Where the player should stand (assign in inspector)

    private DoorInteraction doorInteraction;
    private CutsceneManager cutsceneManager;

    void Start()
    {
        // Find the Player if not assigned
        if (player == null)
        {
            player = GameObject.FindGameObjectWithTag("Player");
            Debug.Log("[Start] Player assigned via tag");
        }

        // Get DoorInteraction component
        if (doorObject != null)
        {
            doorInteraction = doorObject.GetComponent<DoorInteraction>();
            if (doorInteraction == null)
                Debug.LogError("[Start] DoorInteraction not found on doorObject");
        }
        else
        {
            Debug.LogError("[Start] doorObject not assigned");
        }

        // Get CutsceneManager component
        if (cutsceneManagerObject != null)
        {
            cutsceneManager = cutsceneManagerObject.GetComponent<CutsceneManager>();
            if (cutsceneManager == null)
                Debug.LogError("[Start] CutsceneManager not found");
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            doorInteraction?.OnPlayerEnterRange();
            Debug.Log("Player entered trigger zone");
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            doorInteraction?.OnPlayerExitRange();
            Debug.Log("Player exited trigger zone");
        }
    }

    // Call this method externally, e.g., from your input logic
    public void ActivateDoorSequence()
    {
        StartCoroutine(DoorSequence());
    }

    private IEnumerator DoorSequence()
    {
        Debug.Log("[DoorSequence] Starting sequence");

        Animator playerAnim = player.GetComponent<Animator>();
        if (playerAnim != null)
        {
            // Play the door opening animation directly
            playerAnim.Play("OpenDoor", 0, 0f); // adjust clip name if different
            Debug.Log("[Fix] Player animation forced to OpenDoor");
        }

        // 1. Disable player input
        var controller = player.GetComponent<ThirdPersonController>();
        if (controller != null)
        {
            Debug.Log("[Controller] Disabling player controller");
            controller.enabled = false;
        }

        // 2. Calculate target position
        Vector3 targetPosition = (targetPoint != null) ? targetPoint.position : doorObject.transform.position + new Vector3(0, 0, -1);
        Debug.Log("[Move] Moving player to: " + targetPosition);

        // 3. Teleport player
        player.transform.position = targetPosition;

        // 4. Rotate the player to match target point's Y rotation
        if (targetPoint != null)
        {
            Vector3 eulerAngles = player.transform.rotation.eulerAngles;
            eulerAngles.y = targetPoint.rotation.eulerAngles.y;
            player.transform.rotation = Quaternion.Euler(eulerAngles);
            Debug.Log("[Rotate] Player Y rotation set to: " + eulerAngles.y);
        }

        // 5. Trigger door opening animation
        var doorAnimator = doorObject.GetComponent<Animator>();
        if (doorAnimator != null)
        {
            Debug.Log("[Door] Triggering 'Open'");
            doorAnimator.SetTrigger("Open");
        }

        // 6. Play cutscene (wait until finished)
        bool cutsceneDone = false;
        if (cutsceneManager != null)
        {
            cutsceneManager.PlayCutscene(() =>
            {
                Debug.Log("[Cutscene] Completed");
                cutsceneDone = true;
            });
        }
        else
        {
            Debug.LogWarning("[Cutscene] No CutsceneManager assigned");
            yield return new WaitForSeconds(cutsceneDuration);
            cutsceneDone = true;
        }

        while (!cutsceneDone)
            yield return null;

        // 7. Re-enable controller
        if (controller != null)
        {
            Debug.Log("[Controller] Re-enabling player controller");
            controller.enabled = true;
        }

        DoorInteraction.isPlayerNear = false;

        // 8. Load scene
        Debug.Log("[Scene] Loading: " + nextSceneName);
        SceneManager.LoadScene(nextSceneName);
        DoorInteraction.isPlayerNear = false;

    }
}
