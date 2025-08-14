using UnityEngine;
using UnityEngine.SceneManagement; // Needed for scene loading
using System; // Needed for Action
using System.Collections;

public class CutsceneManager : MonoBehaviour
{
    // Reference to the player's Animator component
    public Animator playerAnimator;

    // Duration of the cutscene animation in seconds
    public float cutsceneDuration = 3f;

    /// <summary>
    /// Starts the cutscene animation and executes the optional callback after completion.
    /// </summary>
    /// <param name="onComplete">An optional Action to invoke after the cutscene ends.</param>
    public void PlayCutscene(Action onComplete = null)
    {
        StartCoroutine(CutsceneSequence(onComplete));
    }

    /// <summary>
    /// Coroutine that plays the cutscene animation, waits for its duration, then calls callback.
    /// </summary>
    /// <param name="onComplete">Callback to execute after cutscene</param>
    private IEnumerator CutsceneSequence(Action onComplete)
    {
        Debug.Log("Cutscene starting...");
        // Trigger the door opening animation (make sure this trigger exists in your Animator)
        if (playerAnimator != null)
        {
            playerAnimator.SetTrigger("PlayDoorCutscene");
        }
        else
        {
            Debug.LogWarning("Player Animator not assigned in CutsceneManager.");
        }

        // Wait for the duration of your animation
        yield return new WaitForSeconds(cutsceneDuration);

        Debug.Log("Cutscene ending...");
        // Call the callback to load the scene or perform other actions
        onComplete?.Invoke();
    }
}