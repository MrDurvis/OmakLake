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
    private bool pressInProgress; // prevents double-fire while the same button press is held

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
            return;
        }

        interactAction.action.started   += OnInteractStarted;   // fire here for Press/Tap/Hold
        interactAction.action.performed += OnInteractPerformed; // still listen—will be a no-op if already handled
        interactAction.action.canceled  += OnInteractCanceled;
        interactAction.action.Enable();

        // Dump bindings for clarity
        for (int i = 0; i < interactAction.action.bindings.Count; i++)
        {
            var b = interactAction.action.bindings[i];
            Debug.Log($"[ItemInteraction:{name}] Binding[{i}] path='{b.path}' groups='{b.groups}' interactions='{b.interactions}'");
        }
    }

    private void OnDisable()
    {
        if (interactAction?.action == null) return;
        interactAction.action.started   -= OnInteractStarted;
        interactAction.action.performed -= OnInteractPerformed;
        interactAction.action.canceled  -= OnInteractCanceled;
        interactAction.action.Disable();
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
        // Occasional phase/value spam for visibility
        if (interactAction?.action != null && Time.frameCount % 30 == 0)
        {
            float v = 0f;
            try { v = interactAction.action.ReadValue<float>(); } catch {}
            Debug.Log($"[ItemInteraction:{name}] Action phase={interactAction.action.phase} value={v:0.00}");
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        Debug.Log($"[ItemInteraction:{name}] OnTriggerEnter '{other.name}' tag='{other.tag}'.");
        if (other.CompareTag("Player"))
        {
            playerInRange = true;
            Debug.Log($"[ItemInteraction:{name}] Player IN range.");
        }
    }
    private void OnTriggerExit(Collider other)
    {
        Debug.Log($"[ItemInteraction:{name}] OnTriggerExit '{other.name}' tag='{other.tag}'.");
        if (other.CompareTag("Player"))
        {
            playerInRange = false;
            Debug.Log($"[ItemInteraction:{name}] Player OUT of range.");
        }
    }

    private void OnInteractStarted(InputAction.CallbackContext ctx)
    {
        Debug.Log($"[ItemInteraction:{name}] Interact STARTED. control={ctx.control?.path} device={ctx.control?.device?.displayName}");
        try { TryInteractOnce(); }
        catch (System.Exception ex) { Debug.LogException(ex, this); }  // shows the exact line/file
    }

    private void OnInteractPerformed(InputAction.CallbackContext ctx)
    {
        Debug.Log($"[ItemInteraction:{name}] Interact PERFORMED (if not already handled).");
        TryInteractOnce();
    }

    private void OnInteractCanceled(InputAction.CallbackContext ctx)
    {
        Debug.Log($"[ItemInteraction:{name}] Interact CANCELED.");
        pressInProgress = false; // ready for the next press
    }

    private void TryInteractOnce()
    {
        if (pressInProgress)
        {
            Debug.Log($"[ItemInteraction:{name}] Press already handled; ignoring.");
            return;
        }

        if (!playerInRange)
        {
            Debug.Log($"[ItemInteraction:{name}] Not in range; ignoring.");
            return;
        }

        if (!itemData)
        {
            Debug.LogError($"[ItemInteraction:{name}] No ItemData.");
            return;
        }

        pressInProgress = true;

        // Show dialogue
        if (DialogueManager.Instance != null)
        {
            DialogueManager.Instance.ShowDialogue(itemData.description);
            Debug.Log($"[ItemInteraction:{name}] Showing dialogue.");
        }
        else Debug.LogError($"[ItemInteraction:{name}] DialogueManager.Instance is NULL.");

        // Create clue if applicable
        if (itemData.canBeClue && itemData.clueData != null)
        {
            if (ClueManager.Instance != null)
            {
                ClueManager.Instance.DiscoverClue(itemData.clueData);
                Debug.Log($"[ItemInteraction:{name}] Discovered clue '{itemData.clueData.clueName}'.");
            }
            else Debug.LogError($"[ItemInteraction:{name}] ClueManager.Instance is NULL.");
        }

        // Persist and hide
        if (SaveSystem.Instance != null)
        {
            SaveSystem.Instance.MarkItemCollected(itemData.Guid);
            Debug.Log($"[ItemInteraction:{name}] Marked collected.");
        }
        else
        {
            Debug.LogWarning($"[ItemInteraction:{name}] SaveSystem missing; cannot persist.");
        }

        gameObject.SetActive(false);
        Debug.Log($"[ItemInteraction:{name}] Pickup deactivated.");
    }
}
