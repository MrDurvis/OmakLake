using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Collider))]
public class ItemInteraction : MonoBehaviour
{
    [Header("Data")]
    [SerializeField] private ItemData itemData;

    [Header("Input")]
    [Tooltip("Bind to your Gameplay/Interact action (Gamepad South / Keyboard E).")]
    [SerializeField] private InputActionReference interactAction;

    private bool playerInRange;
    private bool pressInProgress; // locks while the same press is held
    private float suppressUntil;  // short cooldown window after dialog closes
    private bool waitForReleaseAfterDialog; // require Interact to be fully released

    private void Reset()
    {
        var col = GetComponent<Collider>();
        if (col) col.isTrigger = true;
    }

    private void Awake()
    {
        Debug.Log($"[ItemInteraction:{name}] Awake. itemData={(itemData ? itemData.itemName : "NULL")} action={(interactAction ? interactAction.action?.name : "NULL")} map='{interactAction?.action?.actionMap?.name}'");
    }

    private void OnEnable()
    {
        if (interactAction?.action == null)
        {
            Debug.LogError($"[ItemInteraction:{name}] No InputActionReference assigned.");
        }
        else
        {
            interactAction.action.started   += OnInteractStarted;
            interactAction.action.performed += OnInteractPerformed;
            interactAction.action.canceled  += OnInteractCanceled;
            interactAction.action.Enable();

            for (int i = 0; i < interactAction.action.bindings.Count; i++)
            {
                var b = interactAction.action.bindings[i];
                Debug.Log($"[ItemInteraction:{name}] Binding[{i}] path='{b.path}' groups='{b.groups}' interactions='{b.interactions}'");
            }
        }

        // Listen for dialog open/close to add a small cooldown + release requirement
        DialogueManager.OnDialogActiveChanged += OnDialogActiveChanged;
    }

    private void OnDisable()
    {
        if (interactAction?.action != null)
        {
            interactAction.action.started   -= OnInteractStarted;
            interactAction.action.performed -= OnInteractPerformed;
            interactAction.action.canceled  -= OnInteractCanceled;
            interactAction.action.Disable();
        }

        DialogueManager.OnDialogActiveChanged -= OnDialogActiveChanged;
        Debug.Log($"[ItemInteraction:{name}] Disabled interact action.");
    }

    private void Start()
    {
        var ss = SaveSystem.Instance;
        Debug.Log($"[ItemInteraction:{name}] Start. SaveSystem present={(ss != null)}");

        if (!itemData)
        {
            Debug.LogError($"[ItemInteraction:{name}] ItemData is NULL.");
            return;
        }

        if (ss != null && ss.IsItemCollected(itemData.Guid))
        {
            Debug.Log($"[ItemInteraction:{name}] Already collected '{itemData.itemName}'. Hiding object.");
            gameObject.SetActive(false);
        }
        else
        {
            Debug.Log($"[ItemInteraction:{name}] Not collected. Visible and waiting.");
        }
    }

    private void Update()
    {
        // After dialog closes, wait until Interact is fully released, then unlock
        if (waitForReleaseAfterDialog && interactAction?.action != null)
        {
            float val = 0f;
            try { val = interactAction.action.ReadValue<float>(); } catch { }
            if (val <= 0.01f)
            {
                waitForReleaseAfterDialog = false;
                pressInProgress = false; // allow the next *fresh* press
                Debug.Log($"[ItemInteraction:{name}] Interact released after dialog; re-arming interaction.");
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = true;
            Debug.Log($"[ItemInteraction:{name}] Player IN range.");
        }
    }
    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = false;
            Debug.Log($"[ItemInteraction:{name}] Player OUT of range.");
        }
    }

    private void OnInteractStarted(InputAction.CallbackContext ctx)
    {
        // Ignore if dialog open or during cooldown or while waiting for release
        if (DialogueManager.Instance?.IsActive() == true) return;
        if (Time.unscaledTime < suppressUntil) return;
        if (waitForReleaseAfterDialog) return;

        TryInteractOnce();
    }

    private void OnInteractPerformed(InputAction.CallbackContext ctx)
    {
        if (DialogueManager.Instance?.IsActive() == true) return;
        if (Time.unscaledTime < suppressUntil) return;
        if (waitForReleaseAfterDialog) return;

        TryInteractOnce();
    }

    private void OnInteractCanceled(InputAction.CallbackContext ctx)
    {
        // Only unlock on release (prevents re-trigger loops)
        pressInProgress = false;
    }

    private void OnDialogActiveChanged(bool active)
    {
        if (!active)
        {
            // Dialog just closed: add a brief cooldown and require release
            suppressUntil = Time.unscaledTime + 0.25f;
            waitForReleaseAfterDialog = true;
            Debug.Log($"[ItemInteraction:{name}] Dialog closed -> suppress interactions until {suppressUntil:0.00} and wait for release.");
        }
    }

    void TryInteractOnce()
    {
        if (pressInProgress) return;
        if (!playerInRange) return;
        if (!itemData) { Debug.LogError("No ItemData"); return; }

        pressInProgress = true; // cleared on Interact.canceled

        // Prefer graph if present
        if (itemData.graph != null)
        {
            DialogueGraphRunner.Play(itemData.graph, result =>
            {
                if (result.pickupApproved)
                    DoPickup();
                // Do NOT unlock here; we wait for canceled or release-after-dialog logic
            });
            return;
        }

        // Otherwise use authored linear list (if any)
        if (itemData.dialogue != null && itemData.dialogue.Count > 0)
        {
            DialogueManager.Instance.StartSequence(itemData.dialogue, result =>
            {
                bool approved = false;
                if (result != null && result.lastChoice != null &&
                    result.lastChoice.semantic == ChoiceSemantic.PickupYes)
                {
                    approved = (result.lastChoiceIndex == result.lastChoice.yesIndex);
                }
                else
                {
                    approved = true; // default if no choice
                }

                if (approved) DoPickup();
                // pressInProgress unlock via canceled / release-after-dialog
            });
            return;
        }

        // Legacy fallback
        DialogueManager.Instance.StartSequence(
            new System.Collections.Generic.List<DialogueBlock> {
                new DialogueBlock { type = BlockType.Text, text = itemData.description }
            },
            _ => { DoPickup(); }
        );
        // unlock handled as above
    }

    private void DoPickup()
    {
        Debug.Log($"[ItemInteraction:{name}] DoPickup called -> hiding object & saving.");
        if (itemData.canBeClue && itemData.clueData != null)
            ClueManager.Instance?.DiscoverClue(itemData.clueData);

        SaveSystem.Instance?.MarkItemCollected(itemData.Guid);
        gameObject.SetActive(false);
    }
}
