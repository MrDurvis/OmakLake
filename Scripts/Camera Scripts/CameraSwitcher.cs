using UnityEngine;
using Unity.Cinemachine;

public class CameraSwitcher : MonoBehaviour
{
    [SerializeField] private CinemachineCamera exteriorCamera;
    [SerializeField] private CinemachineCamera interiorCamera;
    [SerializeField] private Camera mainCamera;

    private CinemachineBrain brain;
    [SerializeField] private float switchCooldown = 1.0f; // seconds
    private float lastSwitchTime = -Mathf.Infinity;
    private bool isInside = false;

    private void Start()
    {
        // Get the CinemachineBrain component
        brain = mainCamera.GetComponent<CinemachineBrain>();
        // Set blink/instant transition
        SetInstantSwitch();

        // Initialize priorities
        exteriorCamera.Priority = 10;
        interiorCamera.Priority = 0;
        mainCamera.orthographic = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player") && Time.time - lastSwitchTime > switchCooldown)
        {
            isInside = true;
            SwitchToInteriorCamera();
            lastSwitchTime = Time.time;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player") && Time.time - lastSwitchTime > switchCooldown)
        {
            isInside = false;
            SwitchToExteriorCamera();
            lastSwitchTime = Time.time;
        }
    }

    private void SetInstantSwitch()
    {
        var blend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0);
        if (brain != null)
        {
            brain.DefaultBlend = blend;
        }
    }

    private void SwitchToInteriorCamera()
    {
        interiorCamera.Priority = 10;
        exteriorCamera.Priority = 0;
        mainCamera.orthographic = false;
        SetInstantSwitch();
    }

    private void SwitchToExteriorCamera()
    {
        exteriorCamera.Priority = 10;
        interiorCamera.Priority = 0;
        mainCamera.orthographic = true;
        SetInstantSwitch();
    }
}