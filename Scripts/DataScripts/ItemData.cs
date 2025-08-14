// ItemData.cs (link by GUID too)
using UnityEngine;

[CreateAssetMenu(fileName = "ItemData", menuName = "Game Data/Item Data")]
public class ItemData : ScriptableObject
{
    [SerializeField, HideInInspector] private string guid;
    public string Guid => string.IsNullOrEmpty(guid) ? (guid = System.Guid.NewGuid().ToString()) : guid;

    public string itemName;
    [TextArea] public string description;
    public Sprite icon;
    public bool canBeClue;
    public ClueData clueData;

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (string.IsNullOrEmpty(guid))
            guid = System.Guid.NewGuid().ToString();
    }
#endif
}
