#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

// ---------- Custom inspector for the graph asset ----------
[CustomEditor(typeof(DialogueGraph))]
public class DialogueGraphEditor : Editor
{
    SerializedProperty startGuidProp;
    SerializedProperty nodesProp;

    void OnEnable()
    {
        startGuidProp = serializedObject.FindProperty("startGuid");
        nodesProp     = serializedObject.FindProperty("nodes");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // Title
        EditorGUILayout.LabelField("Dialogue Graph", EditorStyles.boldLabel);
        EditorGUILayout.Space(2);

        // Start Node popup
        DrawStartNodePopup();

        EditorGUILayout.Space(6);

        // Nodes list (DialogueNodeDrawer handles conditional fields)
        EditorGUILayout.PropertyField(nodesProp, new GUIContent("Nodes"), true);

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawStartNodePopup()
    {
        var graph = (DialogueGraph)target;

        // Build labels & map GUIDs to indices
        int count = nodesProp.arraySize;
        string[] labels = new string[count];
        string[] guids  = new string[count];

        int currentIndex = -1;

        for (int i = 0; i < count; i++)
        {
            var elem = nodesProp.GetArrayElementAtIndex(i);
            var guidProp   = elem.FindPropertyRelative("guid");
            var isChoice   = elem.FindPropertyRelative("isChoice").boolValue;
            var textProp   = elem.FindPropertyRelative("text");

            string guid = guidProp.stringValue;
            guids[i] = guid;

            string preview = textProp.stringValue;
            if (string.IsNullOrEmpty(preview)) preview = isChoice ? "(Choice node)" : "(Text node)";
            if (preview.Length > 40) preview = preview.Substring(0, 40) + "…";

            labels[i] = $"{i}: {(isChoice ? "[Choice] " : "")}{preview}";

            if (!string.IsNullOrEmpty(graph.startGuid) && graph.startGuid == guid)
                currentIndex = i;
        }

        int newIndex = EditorGUILayout.Popup(new GUIContent("Start Node"), Mathf.Max(0, currentIndex), labels);

        if (newIndex >= 0 && newIndex < guids.Length)
        {
            string newGuid = guids[newIndex];
            if (startGuidProp.stringValue != newGuid)
            {
                startGuidProp.stringValue = newGuid;
            }
        }
    }
}

// ---------- Property drawer for DialogueNode (conditional fields) ----------
[CustomPropertyDrawer(typeof(DialogueNode))]
public class DialogueNodeDrawer : PropertyDrawer
{
    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        // Calculate height based on which fields are shown
        float h = 0f;
        float line = EditorGUIUtility.singleLineHeight;
        float pad  = EditorGUIUtility.standardVerticalSpacing;

        var guid     = property.FindPropertyRelative("guid");
        var isChoice = property.FindPropertyRelative("isChoice");
        var text     = property.FindPropertyRelative("text");
        var choices  = property.FindPropertyRelative("choices");
        var nextGuid = property.FindPropertyRelative("nextGuid");
        var defIdx   = property.FindPropertyRelative("defaultChoiceIndex");

        h += line + pad; // foldout header by PropertyField
        h += line + pad; // guid (readonly-ish)
        h += line + pad; // isChoice
        h += EditorGUI.GetPropertyHeight(text, true) + pad;

        bool showChoice = isChoice.boolValue;

        if (showChoice)
        {
            h += EditorGUI.GetPropertyHeight(choices, true) + pad;
            h += line + pad; // default idx
        }
        else
        {
            h += EditorGUI.GetPropertyHeight(nextGuid, true) + pad;
        }

        h += pad; // extra bottom spacing
        return h;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        var guid     = property.FindPropertyRelative("guid");
        var isChoice = property.FindPropertyRelative("isChoice");
        var text     = property.FindPropertyRelative("text");
        var choices  = property.FindPropertyRelative("choices");
        var nextGuid = property.FindPropertyRelative("nextGuid");
        var defIdx   = property.FindPropertyRelative("defaultChoiceIndex");

        float line = EditorGUIUtility.singleLineHeight;
        float pad  = EditorGUIUtility.standardVerticalSpacing;
        Rect r = new Rect(position.x, position.y, position.width, line);

        // Box group
        GUI.Box(position, GUIContent.none);
        r.x += 6; r.width -= 12;
        float contentWidth = r.width;

        // Title
        EditorGUI.LabelField(r, "Dialogue Node", EditorStyles.boldLabel);
        r.y += line + pad;

        // GUID (display and "Regenerate" button)
        Rect guidRect = new Rect(r.x, r.y, contentWidth - 110, line);
        Rect regenBtn = new Rect(r.x + contentWidth - 100, r.y, 100, line);

        using (new EditorGUI.DisabledScope(true))
            EditorGUI.TextField(guidRect, "GUID", string.IsNullOrEmpty(guid.stringValue) ? "(auto)" : guid.stringValue);

        if (GUI.Button(regenBtn, "Regenerate"))
            guid.stringValue = System.Guid.NewGuid().ToString();

        r.y += line + pad;

        // Is Choice
        EditorGUI.PropertyField(r, isChoice);
        r.y += line + pad;

        // Text / Prompt
        EditorGUI.PropertyField(new Rect(r.x, r.y, contentWidth, EditorGUI.GetPropertyHeight(text, true)), text, new GUIContent(isChoice.boolValue ? "Prompt" : "Text"), true);
        r.y += EditorGUI.GetPropertyHeight(text, true) + pad;

        if (isChoice.boolValue)
        {
            // Choices
            EditorGUI.PropertyField(new Rect(r.x, r.y, contentWidth, EditorGUI.GetPropertyHeight(choices, true)), choices, true);
            r.y += EditorGUI.GetPropertyHeight(choices, true) + pad;

            // Default Choice Index (clamped)
            int oldIdx = defIdx.intValue;
            int newIdx = EditorGUI.IntField(new Rect(r.x, r.y, contentWidth, line), "Default Choice Index", oldIdx);
            if (choices.arraySize > 0)
                defIdx.intValue = Mathf.Clamp(newIdx, 0, choices.arraySize - 1);
            else
                defIdx.intValue = 0;

            r.y += line + pad;
        }
        else
        {
            // Next GUID
            EditorGUI.PropertyField(new Rect(r.x, r.y, contentWidth, EditorGUI.GetPropertyHeight(nextGuid, true)), nextGuid, true);
            r.y += EditorGUI.GetPropertyHeight(nextGuid, true) + pad;
        }

        EditorGUI.EndProperty();
    }
}
#endif
