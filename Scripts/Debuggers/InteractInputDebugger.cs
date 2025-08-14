using UnityEngine;
using UnityEngine.InputSystem;

public class InteractInputDebugger : MonoBehaviour
{
    [SerializeField] private InputActionReference interactAction;

    private void OnEnable()
    {
        if (interactAction == null) { Debug.LogError("[InteractInputDebugger] No action assigned."); return; }
        interactAction.action.started   += OnStarted;
        interactAction.action.performed += OnPerf;
        interactAction.action.canceled  += OnCanceled;
        interactAction.action.Enable();
        Debug.Log($"[InteractInputDebugger] Enabled '{interactAction.action.name}' (map: {interactAction.action.actionMap?.name})");
        for (int i = 0; i < interactAction.action.bindings.Count; i++)
        {
            var b = interactAction.action.bindings[i];
            Debug.Log($"[InteractInputDebugger] Binding[{i}] path='{b.path}' groups='{b.groups}' interactions='{b.interactions}'");
        }
    }
    private void OnDisable()
    {
        if (interactAction == null) return;
        interactAction.action.started   -= OnStarted;
        interactAction.action.performed -= OnPerf;
        interactAction.action.canceled  -= OnCanceled;
        interactAction.action.Disable();
    }
    private void OnStarted(InputAction.CallbackContext c)  { Debug.Log($"[InteractInputDebugger] STARTED {c.control?.path}"); }
    private void OnPerf(InputAction.CallbackContext c)     { Debug.Log($"[InteractInputDebugger] PERFORMED {c.control?.path}"); }
    private void OnCanceled(InputAction.CallbackContext c) { Debug.Log($"[InteractInputDebugger] CANCELED"); }
}
