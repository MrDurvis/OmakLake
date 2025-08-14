using UnityEngine;
using System.Collections;

public class ObjectIlluminator : MonoBehaviour
{
    [SerializeField]
    private GameObject targetObject;  // Assign in inspector
    [SerializeField]
    private Color baseOutlineColor = Color.white;
    private Material targetMaterial;
    private bool playerNearby = false;
    private Coroutine pulseCoroutine;

    void Start()
    {
        if (targetObject != null)
        {
            // Get a unique instance of the material
            targetMaterial = targetObject.GetComponent<Renderer>().material;

            // Debug info
            //Debug.Log("Target material assigned: " + targetMaterial.name);

            
        }
        else
        {
            //Debug.LogError("Target Object not assigned.", this);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player") && pulseCoroutine == null)
        {
            playerNearby = true;
            //Debug.Log("Player entered trigger zone");
            pulseCoroutine = StartCoroutine(PulseOutline());
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player") && pulseCoroutine != null)
        {
            playerNearby = false;
            //Debug.Log("Player exited trigger zone");
            StopCoroutine(pulseCoroutine);
            pulseCoroutine = null;
            SetOutlineColor(0f); // Reset to transparent or default
        }
    }

    private IEnumerator PulseOutline()
    {
        while (playerNearby)
        {
            float alpha = (Mathf.Sin(Time.time * 2f) + 1f) / 2f; // Pulsing alpha
            SetOutlineColor(alpha);
            yield return null;
        }
    }

    private void SetOutlineColor(float alpha)
    {
          if (targetMaterial != null)
    {
        Color newColor = baseOutlineColor;
        newColor.a = alpha;

        //Debug.Log($"Setting _OutlineColor to {newColor}");
        targetMaterial.SetColor("_OutlineColor", newColor);
    }

    }
}
