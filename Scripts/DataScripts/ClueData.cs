// ClueData.cs (keep your fields; add GUID + helper)
using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "ClueData", menuName = "Game Data/Clue Data")]
public class ClueData : ScriptableObject
{
    [SerializeField, HideInInspector] private string guid;
    public string Guid => string.IsNullOrEmpty(guid) ? (guid = System.Guid.NewGuid().ToString()) : guid;

    public string clueName;
    [TextArea] public string description;
    public Sprite icon;
    public ClueCategory category;
    public List<string> relatedClueGuids; // swap to string GUIDs

    public enum ClueCategory { Person, Object, Location, Event }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (string.IsNullOrEmpty(guid))
            guid = System.Guid.NewGuid().ToString();
    }
#endif
}
