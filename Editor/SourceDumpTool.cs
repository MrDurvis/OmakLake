// Assets/Editor/SourceDumpTool.cs
// Dumps source code to JSON for MonoBehaviours only, or ALL .cs files.
// Each mode offers Joined (single JSON) or Split (one JSON per script), with optional ZIP output.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public static class SourceDumpTool
{
    // ---------- Shared DTOs ----------
    [Serializable] public class FieldInfoLite
    {
        public string name;
        public string type;
        public bool isPublic;
        public bool hasSerializeField;
    }

    public enum ScriptKind { MonoBehaviour, ScriptableObject, Other, Unknown }

    [Serializable] public class ScriptEntry
    {
        public ScriptKind kind;
        public string className;
        public string @namespace;
        public string fullName;
        public string filePath;
        public string guid;
        public string source;
        public List<FieldInfoLite> serializedFields;
        public string[] declaredTypes;
    }

    [Serializable] public class Dump
    {
        public string scannedFolder;
        public string generatedAt;
        public string mode;              // "All" or "MonoBehaviours"
        public List<ScriptEntry> scripts;
    }

    // ---------- Menu Items ----------
    [MenuItem("Tools/Source Dump/MonoBehaviours Only/Joined JSON")]
    public static void GenerateMonoBehavioursJoined() => Generate(includeAllCs: false, split: false, zip: false);

    [MenuItem("Tools/Source Dump/MonoBehaviours Only/Joined JSON (zip)")]
    public static void GenerateMonoBehavioursJoinedZip() => Generate(includeAllCs: false, split: false, zip: true);

    [MenuItem("Tools/Source Dump/MonoBehaviours Only/Split JSON (per file)")]
    public static void GenerateMonoBehavioursSplit() => Generate(includeAllCs: false, split: true, zip: false);

    [MenuItem("Tools/Source Dump/MonoBehaviours Only/Split JSON (zip folder)")]
    public static void GenerateMonoBehavioursSplitZip() => Generate(includeAllCs: false, split: true, zip: true);

    [MenuItem("Tools/Source Dump/All C# Scripts/Joined JSON")]
    public static void GenerateAllCsJoined() => Generate(includeAllCs: true, split: false, zip: false);

    [MenuItem("Tools/Source Dump/All C# Scripts/Joined JSON (zip)")]
    public static void GenerateAllCsJoinedZip() => Generate(includeAllCs: true, split: false, zip: true);

    [MenuItem("Tools/Source Dump/All C# Scripts/Split JSON (per file)")]
    public static void GenerateAllCsSplit() => Generate(includeAllCs: true, split: true, zip: false);

    [MenuItem("Tools/Source Dump/All C# Scripts/Split JSON (zip folder)")]
    public static void GenerateAllCsSplitZip() => Generate(includeAllCs: true, split: true, zip: true);

    // ---------- Core ----------
    private static void Generate(bool includeAllCs, bool split, bool zip)
    {
        // Prompt for folder
        string folderAbs = EditorUtility.OpenFolderPanel("Select folder to scan", Application.dataPath, "");
        if (string.IsNullOrEmpty(folderAbs)) return;

        // Convert to Unity-relative path
        string folderUnity = folderAbs.StartsWith(Application.dataPath)
            ? "Assets" + folderAbs.Replace('\\', '/').Substring(Application.dataPath.Length)
            : null;

        if (string.IsNullOrEmpty(folderUnity))
        {
            EditorUtility.DisplayDialog("Source Dump", "Please choose a folder inside your project's Assets directory.", "OK");
            return;
        }

        // Gather .cs files
        var allCsPathsAbs = Directory.GetFiles(folderAbs, "*.cs", SearchOption.AllDirectories);
        var entries = new List<ScriptEntry>();

        foreach (var absPath in allCsPathsAbs)
        {
            string unityPath = "Assets" + absPath.Replace('\\', '/').Substring(Application.dataPath.Length);

            string sourceText;
            try { sourceText = File.ReadAllText(absPath); }
            catch (Exception e) { sourceText = $"// ERROR reading file: {e.Message}\n"; }

            var mono = AssetDatabase.LoadAssetAtPath<MonoScript>(unityPath);

            Type type = null;
            try { if (mono != null) type = mono.GetClass(); } catch { }

            bool isMB = type != null && typeof(MonoBehaviour).IsAssignableFrom(type) && !type.IsAbstract;
            bool isSO = type != null && typeof(ScriptableObject).IsAssignableFrom(type) && !type.IsAbstract; // <-- fixed

            if (!includeAllCs && !isMB)
                continue;

            ScriptKind kind = ScriptKind.Unknown;
            if (isMB) kind = ScriptKind.MonoBehaviour;
            else if (isSO) kind = ScriptKind.ScriptableObject;
            else if (type != null) kind = ScriptKind.Other;

            List<FieldInfoLite> fields = null;
            if (type != null)
            {
                fields = new List<FieldInfoLite>();
                var flags = System.Reflection.BindingFlags.Instance |
                            System.Reflection.BindingFlags.Public |
                            System.Reflection.BindingFlags.NonPublic |
                            System.Reflection.BindingFlags.DeclaredOnly;

                foreach (var f in type.GetFields(flags))
                {
                    bool isSerialized = f.IsPublic || Attribute.IsDefined(f, typeof(SerializeField));
                    if (!isSerialized) continue;

                    fields.Add(new FieldInfoLite
                    {
                        name = f.Name,
                        type = f.FieldType.FullName,
                        isPublic = f.IsPublic,
                        hasSerializeField = Attribute.IsDefined(f, typeof(SerializeField))
                    });
                }
                if (fields.Count == 0) fields = null;
            }

            string[] declared = TryExtractDeclaredTypes(sourceText);
            string guid = AssetDatabase.AssetPathToGUID(unityPath);

            entries.Add(new ScriptEntry
            {
                kind = kind,
                className = type?.Name ?? "",
                @namespace = type?.Namespace ?? "",
                fullName = type?.FullName ?? "",
                filePath = unityPath,
                guid = string.IsNullOrEmpty(guid) ? "" : guid,
                source = sourceText,
                serializedFields = fields,
                declaredTypes = declared
            });
        }

        entries = entries
            .OrderBy(e => e.kind.ToString(), StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => string.IsNullOrEmpty(e.fullName) ? e.filePath : e.fullName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (split)
        {
            // Write one JSON per script
            string outDirUnity = $"{folderUnity}/SourceDump_Split";
            string outDirAbs = Path.GetFullPath(outDirUnity);
            Directory.CreateDirectory(outDirAbs);

            foreach (var e in entries)
            {
                var singleDump = new Dump
                {
                    scannedFolder = folderUnity,
                    generatedAt = DateTime.UtcNow.ToString("o"),
                    mode = includeAllCs ? "All" : "MonoBehaviours",
                    scripts = new List<ScriptEntry> { e }
                };

                string safeName = string.IsNullOrEmpty(e.fullName) ? Path.GetFileNameWithoutExtension(e.filePath) : e.fullName;
                safeName = MakeSafeFilename(safeName);
                string outPathAbs = Path.Combine(outDirAbs, safeName + ".json");

                try
                {
                    File.WriteAllText(outPathAbs, JsonUtility.ToJson(singleDump, true));
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[SourceDump] Failed writing {outPathAbs}: {ex.Message}");
                }
            }

            AssetDatabase.Refresh();

            if (zip)
            {
                string zipPathAbs = Path.GetFullPath($"{outDirUnity}.zip");
                TryCreateZipFromDirectory(outDirAbs, zipPathAbs);
                EditorUtility.DisplayDialog("Source Dump",
                    $"Wrote {entries.Count} JSON files to:\n{outDirUnity}\n\nZipped to:\n{outDirUnity}.zip",
                    "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("Source Dump",
                    $"Wrote {entries.Count} JSON files to:\n{outDirUnity}",
                    "OK");
            }
        }
        else
        {
            // Write a single joined JSON
            var dump = new Dump
            {
                scannedFolder = folderUnity,
                generatedAt = DateTime.UtcNow.ToString("o"),
                mode = includeAllCs ? "All" : "MonoBehaviours",
                scripts = entries
            };

            string outPathUnity = includeAllCs
                ? $"{folderUnity}/AllCsSourceDump.json"
                : $"{folderUnity}/MonoBehaviourSourceDump.json";

            string outPathAbs = Path.GetFullPath(outPathUnity);

            try
            {
                File.WriteAllText(outPathAbs, JsonUtility.ToJson(dump, true));
                AssetDatabase.Refresh();

                if (zip)
                {
                    string zipPathAbs = Path.ChangeExtension(outPathAbs, ".zip");
                    TryZipSingleFile(outPathAbs, zipPathAbs);

                    EditorUtility.DisplayDialog("Source Dump",
                        $"Wrote:\n{outPathUnity}\n\nZipped to:\n{Path.ChangeExtension(outPathUnity, ".zip")}\n\nEntries: {entries.Count}",
                        "OK");
                }
                else
                {
                    EditorUtility.DisplayDialog("Source Dump",
                        $"Wrote:\n{outPathUnity}\n\nEntries: {entries.Count}",
                        "OK");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[SourceDump] Failed writing JSON: {e.Message}");
                EditorUtility.DisplayDialog("Source Dump", $"Failed writing JSON:\n{e.Message}", "OK");
            }
        }
    }

    // ---------- Helpers ----------
    private static readonly Regex DeclaredTypeRegex =
        new Regex(@"\b(class|struct|interface|enum)\s+([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);

    private static string[] TryExtractDeclaredTypes(string source)
    {
        if (string.IsNullOrEmpty(source)) return Array.Empty<string>();
        try
        {
            var matches = DeclaredTypeRegex.Matches(source);
            if (matches.Count == 0) return Array.Empty<string>();
            var names = new List<string>(matches.Count);
            foreach (Match m in matches)
                if (m.Groups.Count >= 3) names.Add(m.Groups[2].Value);
            return names.Distinct().ToArray();
        }
        catch { return Array.Empty<string>(); }
    }

    private static string MakeSafeFilename(string s)
    {
        foreach (var ch in Path.GetInvalidFileNameChars())
            s = s.Replace(ch, '_');
        // also replace characters that confuse paths in zips
        s = s.Replace('<', '_').Replace('>', '_').Replace(':', '_').Replace('/', '_').Replace('\\', '_');
        return s;
    }

    private static void TryCreateZipFromDirectory(string dirAbs, string zipAbs)
    {
        try
        {
            if (File.Exists(zipAbs)) File.Delete(zipAbs);
            System.IO.Compression.ZipFile.CreateFromDirectory(
    dirAbs, zipAbs, System.IO.Compression.CompressionLevel.Optimal, includeBaseDirectory: false);
            Debug.Log($"[SourceDump] Zipped folder to: {zipAbs}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SourceDump] Failed to zip folder: {ex.Message}");
        }
    }

    private static void TryZipSingleFile(string fileAbs, string zipAbs)
    {
        try
        {
            if (File.Exists(zipAbs)) File.Delete(zipAbs);
           using (var zip = System.IO.Compression.ZipFile.Open(zipAbs, System.IO.Compression.ZipArchiveMode.Create))
{
    zip.CreateEntryFromFile(
        fileAbs,
        System.IO.Path.GetFileName(fileAbs),
        System.IO.Compression.CompressionLevel.Optimal
    );
}
            Debug.Log($"[SourceDump] Zipped file to: {zipAbs}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SourceDump] Failed to zip file: {ex.Message}");
        }
    }
}
