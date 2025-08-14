using UnityEngine;
using UnityEngine.InputSystem;

public class ItemInteraction : MonoBehaviour
{
    public ItemData itemData; // Assign via inspector
    private bool playerInRange = false;
    private bool hasInteracted = false; // Flag to prevent multiple triggers

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = true;
            Debug.Log($"Player entered trigger of {gameObject.name}");
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = false;
            Debug.Log($"Player exited trigger of {gameObject.name}");
            // Reset the interaction flag when player leaves
            hasInteracted = false;
        }
    }

    void Update()
    {
        // Detect joystick 'A' button press
        if (playerInRange && Input.GetKeyDown(KeyCode.JoystickButton0))
        {
            if (hasInteracted)
            {
                Debug.Log("Already interacted with this object, ignoring press");
                return; // Do nothing if it's already handled
            }
            Debug.Log($"Interaction button pressed at {gameObject.name}");
            Interact();
            // Mark as handled so it doesn't trigger again immediately
            hasInteracted = true;
        }
    }

    void Interact()
    {
        if (itemData == null)
        {
            Debug.LogError("ItemData is not set on " + gameObject.name);
            return;
        }

        // Check if dialogue is already active
        if (DialogueManager.Instance != null && DialogueManager.Instance.IsDialogueActive())
        {
            Debug.Log("Dialogue active, hiding now");
            DialogueManager.Instance.HideDialogue();
        }
        else
        {
            Debug.Log("Dialogue not active, showing now");
            DialogueManager.Instance.ShowDialogue(itemData.description);

            // Add clue if relevant
            if (itemData.canBeClue && itemData.clueData != null && ClueManager.Instance != null)
            {
                Debug.Log($"Adding clue node for {itemData.itemName}");
                ClueManager.Instance.AddClueNode(itemData.clueData);
            }
        }
    }
}
