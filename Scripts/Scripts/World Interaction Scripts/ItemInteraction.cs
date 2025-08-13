using UnityEngine;

public class ItemInteraction : MonoBehaviour
{
    public ItemData itemData; // Assign via inspector

    private bool playerInRange = false;

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = true;
            // Optional: highlight object or show interaction UI
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = false;
            // Remove highlight or hide UI
        }
    }

    void Update()
    {
        if (playerInRange && Input.GetKeyDown(KeyCode.E)) // Or your interaction key
        {
            Interact();
        }
    }

    void Interact()
    {
        // Show dialogue with item info (using your dialogue system)
        DialogueManager.Instance.ShowDialogue(itemData.description);

        // If this item can be a clue, add it to the clue system
        if (itemData.canBeClue && itemData.clueData != null)
        {
            // Call your ClueManager to add the clue node
            ClueManager.Instance.AddClueNode(itemData.clueData);
        }

        // Show UI message/icons about new clue (handled elsewhere)
    }
}

