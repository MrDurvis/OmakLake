using UnityEngine;
using System.Collections.Generic;

public class ClueManager : MonoBehaviour
{
    public static ClueManager Instance;

    // Reference to your CognitionBoard script
    public CognitionBoard cognitionBoard;

    private Dictionary<int, ClueData> cluesByID = new Dictionary<int, ClueData>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Optional: Persist across scenes
        // DontDestroyOnLoad(gameObject);
    }

    /// <summary>
    /// Adds a clue to the system and creates a node if it hasn't been added already.
    /// </summary>
    public void AddClueNode(ClueData clueData)
    {
        // Check if clue is already added
        if (cluesByID.ContainsKey(clueData.clueID))
        {
            Debug.LogWarning($"Clue with ID {clueData.clueID} already exists. Skipping addition.");
            return;
        }

        // Store the clue data
        cluesByID[clueData.clueID] = clueData;

        // Add node to the Cognition Board
        if (cognitionBoard != null)
        {
            cognitionBoard.AddNode(clueData);
            Debug.Log("Added clue node: " + clueData.clueName);
        }
        else
        {
            Debug.LogWarning("CognitionBoard reference not set in ClueManager");
        }
    }

    // Optional: retrieve clues, check if a clue exists, etc.
    public bool HasClue(int clueID)
    {
        return cluesByID.ContainsKey(clueID);
    }
}
