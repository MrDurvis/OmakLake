// Assets/Editor/TMPFontRemapper.cs
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine.TextCore.Text; // TMP_Character, Glyph
#endif

public static class TMPFontRemapper
{
#if UNITY_EDITOR
    // Adjust for your project
    private const string SourceAssetPath = "Assets/Fonts/PromptIcons_Src.asset";
    private const string OutputAssetPath = "Assets/Fonts/PromptIcons_QP_AL_AndTopRow.asset";

    [MenuItem("Tools/TMP/Build Remapped Icon Font")]
    public static void BuildRemappedFont()
    {
        var src = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(SourceAssetPath);
        if (src == null)
        {
            Debug.LogError($"Source font asset not found at: {SourceAssetPath}");
            return;
        }

        // Clone so we reuse the same atlas & glyphs
        var dst = UnityEngine.Object.Instantiate(src);
        dst.name = "PromptIcons_QP_AL_AndTopRow";

        // Clear characters (do NOT assign new lists)
        if (dst.characterTable != null) dst.characterTable.Clear();
        if (dst.characterLookupTable != null) dst.characterLookupTable.Clear();

        // Don’t let TMP auto-add glyphs later
        dst.atlasPopulationMode = TMPro.AtlasPopulationMode.Static;

        // --- Mappings ---
        (uint ascii, uint unicode)[] qwertyRemap =
        {
            (0x51, 0x21F1), // Q -> Left Analog Any
            (0x57, 0x21F2), // W -> Right Analog Any
            (0x45, 0x21CE), // E -> Dpad
            (0x52, 0x21D0), // R -> Button X
            (0x54, 0x21D1), // T -> Button Y
            (0x59, 0x21D2), // Y -> Button B
            (0x55, 0x21D3), // U -> Button A
            (0x49, 0x2196), // I -> LT
            (0x4F, 0x2197), // O -> RT
            (0x50, 0x2198), // P -> LB
            (0x41, 0x2199), // A -> RB
            (0x53, 0x21FB), // S -> Start
            (0x44, 0x21FA), // D -> Share
        };

        (uint ascii, uint unicode)[] numberRowRemap =
        {
            (0x31, 0x21F1), // 1
            (0x32, 0x21F2), // 2
            (0x33, 0x21CE), // 3
            (0x34, 0x21D0), // 4
            (0x35, 0x21D1), // 5
            (0x36, 0x21D2), // 6
            (0x37, 0x21D3), // 7
            (0x38, 0x2196), // 8
            (0x39, 0x2197), // 9
            (0x30, 0x2198), // 0
            (0x2D, 0x2199), // -
            (0x3D, 0x21FB), // =
            // If you also want Share on backquote, uncomment:
            // (0x60, 0x21FA), // `
        };

        AddRemap(src, dst, qwertyRemap);
        AddRemap(src, dst, numberRowRemap);

        // --- Rebuild the private lookup dictionary (read-only property) ---
        var field = typeof(TMP_FontAsset).GetField("m_CharacterLookupDictionary",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var dict = field?.GetValue(dst) as Dictionary<uint, TMP_Character>;
        if (dict == null)
        {
            dict = new Dictionary<uint, TMP_Character>();
            field?.SetValue(dst, dict);
        }
        else
        {
            dict.Clear();
        }

        foreach (var ch in dst.characterTable)
            dict[ch.unicode] = ch;

        // Save
        AssetDatabase.CreateAsset(dst, OutputAssetPath);
        EditorUtility.SetDirty(dst);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"Created remapped TMP font asset at: {OutputAssetPath}");
    }

    private static void AddRemap(TMP_FontAsset src, TMP_FontAsset dst, (uint ascii, uint unicode)[] pairs)
    {
        foreach (var (ascii, unicode) in pairs)
        {
            if (src.characterLookupTable != null &&
                src.characterLookupTable.TryGetValue(unicode, out var srcChar) &&
                srcChar.glyph != null)
            {
                // Create a new TMP_Character for the ASCII slot but reuse the source glyph
                var newChar = new TMP_Character(ascii, srcChar.glyph)
                {
                    scale = srcChar.scale
                };
                dst.characterTable.Add(newChar);
            }
            else
            {
                Debug.LogWarning($"Missing U+{unicode:X} in source asset; cannot map ASCII 0x{ascii:X}.");
            }
        }
    }
#endif
}
