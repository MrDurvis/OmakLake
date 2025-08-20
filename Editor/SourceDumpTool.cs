// Assets/SourceDumpTool/Editor/SourceDumpTool.cs
// Tools → SourceDumpTool
//
// Scopes:
//   - Files: export selected .cs files to JSON (per file). Packaging: None, ZipEach, ZipAll.
//   - Folder: scan a folder for .cs, export Joined or Split. Optional zip.
//   - Scenes: snapshot .unity scenes (hierarchy + components + serialized fields) to compact JSON (FLAT LIST).
//
// Notes:
//   - Scene snapshot now includes stable IDs (GlobalObjectId, instanceID, componentId),
//     prefab info, richer ObjectReference metadata (GUID/localFileId/globalObjectId),
//     and typed values for props (numbers as numbers, arrays structured).
//   - Output folder must be under Assets/.
//
// Tested in Unity 2021.3–2025.x

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;

using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;

// For ZipFileExtensions.CreateEntryFromFile (extension methods)
using System.IO.Compression;
// Alias to avoid clash with UnityEngine.CompressionLevel
using IOComp = System.IO.Compression;

public static class SourceDumpTool
{
    // =========================================================
    // Script dump DTOs
    // =========================================================
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
        public string filePath;     // Unity path (Assets/…)
        public string guid;
        public string source;
        public List<FieldInfoLite> serializedFields;
        public string[] declaredTypes;
    }

    [Serializable] public class Dump
    {
        public string scannedFolder; // For single file: its path
        public string generatedAt;
        public string mode;          // "All", "MonoBehaviours", "SingleFile (Any)", etc.
        public List<ScriptEntry> scripts;
    }

    // =========================================================
    // Scene snapshot DTOs (FLAT — no recursive children)
    // =========================================================
    [Serializable] public class SceneSnapshot
    {
        public string scenePath;
        public string sceneName;
        public string unityVersion;
        public string generatedAt;
        public int rootCount;
        public int totalObjects;
        public bool includesInactive;
        public bool includesTransforms;
        public string toolVersion = "1.2.0"; // bump when schema changes
        public List<GOFlat> objects = new List<GOFlat>(); // flat list of all objects
    }

    [Serializable] public class GOFlat
    {
        public string name;
        public string path;        // e.g. "Root/Child/Leaf"
        public string parentPath;  // "" for roots
        public bool active;
        public string tag;
        public int layer;

        // Stable IDs
        public int instanceId;
        public string globalObjectId; // GlobalObjectId.ToString()

        // Prefab info
        public bool isPrefabInstance;
        public string prefabAssetPath;
        public string prefabSourceGlobalId; // GlobalObjectId of source (if resolvable)
        public string prefabOverridesSummary;

        public TransformLite transform; // optional
        public List<ComponentEntry> components = new List<ComponentEntry>();
    }

    [Serializable] public class TransformLite
    {
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Vector3 localScale;
    }

    [Serializable] public class ComponentEntry
    {
        public int componentId;       // index on GameObject (stable within GO)
        public string type;           // simple name e.g. "MeshRenderer", "PlayerMover"
        public string typeFullName;   // System.Type.FullName when available
        public bool missingScript;    // true if script is missing
        public List<PropEntry> props; // compact serialized props
    }

    [Serializable] public class ArrayItem
    {
        // We keep the same flexible shape as PropEntry but only a subset is typical.
        public string kind;
        public string stringValue;
        public int? intValue;
        public float? floatValue;
        public bool? boolValue;
        public Vector4? v4;
        public Vector3? v3;
        public Vector2? v2;
        public Color? color;
        public string enumValue;

        // Object refs in arrays
        public string refName;
        public string refType;
        public string refAssetPath;
        public string refScenePath;
        public string refGuid;
        public long? refLocalFileId;
        public string refGlobalObjectId;
    }

    [Serializable] public class PropEntry
    {
        public string name;  // Display name (Unity)
        public string path;  // SerializedProperty.propertyPath for precision
        public string kind;  // "float","int","bool","string","Vector3","Color","Enum","ObjectReference","Array", ...

        // Typed values (mutually exclusive in practice)
        public string stringValue;
        public int? intValue;
        public float? floatValue;
        public bool? boolValue;
        public string enumValue;

        // Numeric structs as native values (no stringified JSON)
        public Vector2? v2;
        public Vector3? v3;
        public Vector4? v4;      // used for Vector4 & Quaternion
        public Color? color;
        public Rect? rect;

        // Arrays (sampled)
        public int? arrayCount;
        public int? arraySampleCount;
        public List<ArrayItem> arrayItems;

        // For ObjectReference
        public string refName;
        public string refType;
        public string refAssetPath;   // if asset
        public string refScenePath;   // if scene object (its hierarchy path)
        public string refGuid;        // Asset GUID when available
        public long? refLocalFileId;  // Asset local file id when available
        public string refGlobalObjectId; // for scene refs / components
        public string inputActionMap; // if InputActionReference
        public string inputActionName;// if InputActionReference
    }

    // =========================================================
    // Public APIs – called by the GUI
    // =========================================================
    public enum FilesPackaging { None, ZipEach, ZipAll }

    // ---------- Folder (scripts) ----------
    public static void GenerateForFolderAtPath(
        string folderUnity,
        string outputFolderUnity,
        bool includeAllCs,
        bool split,
        bool zip,
        string timestamp)
    {
        if (!IsAssetsPath(folderUnity) || !IsAssetsPath(outputFolderUnity))
        {
            EditorUtility.DisplayDialog("Source Dump", "Input and Output folders must be inside Assets/…", "OK");
            return;
        }

        string folderAbs = Path.GetFullPath(folderUnity);
        if (!Directory.Exists(folderAbs))
        {
            EditorUtility.DisplayDialog("Source Dump", $"Folder not found:\n{folderUnity}", "OK");
            return;
        }

        string outRootAbs = EnsureOutputFolder(outputFolderUnity);

        var allCsPathsAbs = Directory.GetFiles(folderAbs, "*.cs", SearchOption.AllDirectories);
        var entries = new List<ScriptEntry>();

        foreach (var absPath in allCsPathsAbs)
        {
            string unityPath = ToAssetsPath(absPath);
            if (TryAnalyzeScript(absPath, unityPath, out var entry, out _, out _))
            {
                if (!includeAllCs && entry.kind != ScriptKind.MonoBehaviour) continue;
                entries.Add(entry);
            }
        }

        entries = OrderEntries(entries);

        if (split)
        {
            string splitFolderName = $"SourceDump_Split_{timestamp}";
            string outDirAbs = Path.Combine(outRootAbs, splitFolderName);
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

                string safeName = MakeSafeFilename(string.IsNullOrEmpty(e.fullName)
                    ? Path.GetFileNameWithoutExtension(e.filePath)
                    : e.fullName);

                string outPathAbs = Path.Combine(outDirAbs, $"{safeName}_{timestamp}.json");
                TryWriteJson(outPathAbs, singleDump);
            }

            AssetDatabase.Refresh();

            if (zip)
            {
                string zipPathAbs = Path.Combine(outRootAbs, $"{splitFolderName}.zip");
                TryCreateZipFromDirectory(outDirAbs, zipPathAbs);
                Info($"Wrote {entries.Count} JSON files to:\n{ToUnityPath(outDirAbs)}\n\nZipped to:\n{ToUnityPath(zipPathAbs)}");
            }
            else
            {
                Info($"Wrote {entries.Count} JSON files to:\n{ToUnityPath(outDirAbs)}");
            }
        }
        else
        {
            string baseName = includeAllCs ? "AllCsSourceDump" : "MonoBehaviourSourceDump";
            string outPathAbs = Path.Combine(outRootAbs, $"{baseName}_{timestamp}.json");

            var dump = new Dump
            {
                scannedFolder = folderUnity,
                generatedAt = DateTime.UtcNow.ToString("o"),
                mode = includeAllCs ? "All" : "MonoBehaviours",
                scripts = entries
            };

            if (TryWriteJson(outPathAbs, dump))
            {
                if (zip)
                {
                    string zipPathAbs = Path.ChangeExtension(outPathAbs, ".zip");
                    TryZipSingleFile(outPathAbs, zipPathAbs);
                    Info($"Wrote:\n{ToUnityPath(outPathAbs)}\n\nZipped to:\n{ToUnityPath(zipPathAbs)}\n\nEntries: {entries.Count}");
                }
                else
                {
                    Info($"Wrote:\n{ToUnityPath(outPathAbs)}\n\nEntries: {entries.Count}");
                }
            }
        }
    }

    // ---------- Files (scripts) ----------
    public static void GenerateForFilesAtPaths(
        List<string> unityPaths,
        string outputFolderUnity,
        bool monoOnly,
        FilesPackaging filesPackaging,
        string timestamp)
    {
        if (unityPaths == null || unityPaths.Count == 0)
        {
            EditorUtility.DisplayDialog("Source Dump", "No files to export.", "OK");
            return;
        }
        if (!IsAssetsPath(outputFolderUnity))
        {
            EditorUtility.DisplayDialog("Source Dump", "Output folder must be inside Assets/…", "OK");
            return;
        }

        string outAbs = EnsureOutputFolder(outputFolderUnity);

        List<string> createdJsonsAbs = new List<string>();
        int written = 0;

        foreach (var unityPath in unityPaths.Distinct())
        {
            if (!unityPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) continue;

            string absPath = Path.GetFullPath(unityPath);
            if (!File.Exists(absPath)) continue;

            if (!TryAnalyzeScript(absPath, unityPath, out var entry, out _, out _)) continue;
            if (monoOnly && entry.kind != ScriptKind.MonoBehaviour) continue;

            var dump = new Dump
            {
                scannedFolder = unityPath,
                generatedAt = DateTime.UtcNow.ToString("o"),
                mode = monoOnly ? "SingleFile (MonoBehaviourOnly)" : "SingleFile (Any)",
                scripts = new List<ScriptEntry> { entry }
            };

            string baseName = string.IsNullOrEmpty(entry.fullName)
                ? Path.GetFileNameWithoutExtension(unityPath)
                : entry.fullName;

            string outJsonAbs = Path.Combine(outAbs, $"{MakeSafeFilename(baseName)}_{timestamp}.json");

            if (TryWriteJson(outJsonAbs, dump))
            {
                createdJsonsAbs.Add(outJsonAbs);
                written++;

                if (filesPackaging == FilesPackaging.ZipEach)
                {
                    string zipAbs = Path.ChangeExtension(outJsonAbs, ".zip");
                    TryZipSingleFile(outJsonAbs, zipAbs);
                }
            }
        }

        if (filesPackaging == FilesPackaging.ZipAll && createdJsonsAbs.Count > 0)
        {
            string zipAbs = Path.Combine(outAbs, $"FilesSourceDump_{timestamp}.zip");
            TryCreateZipFromFiles(createdJsonsAbs, zipAbs);
        }

        AssetDatabase.Refresh();
        Info($"Wrote {written} file(s) to:\n{outputFolderUnity}");
    }

    // ---------- Scenes (flat snapshot) ----------
    public static void GenerateForScenesAtPaths(
        List<string> sceneUnityPaths,
        string outputFolderUnity,
        bool includeInactive,
        bool includeTransforms,
        int maxStringLength,
        int maxArrayItems,
        FilesPackaging packaging,
        string timestamp)
    {
        if (sceneUnityPaths == null || sceneUnityPaths.Count == 0)
        {
            EditorUtility.DisplayDialog("Scene Snapshot", "No scenes selected.", "OK");
            return;
        }
        if (!IsAssetsPath(outputFolderUnity))
        {
            EditorUtility.DisplayDialog("Scene Snapshot", "Output folder must be inside Assets/…", "OK");
            return;
        }

        string outAbs = EnsureOutputFolder(outputFolderUnity);

        var created = new List<string>();
        foreach (var scenePath in sceneUnityPaths.Distinct())
        {
            if (!scenePath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)) continue;

            string abs = Path.GetFullPath(scenePath);
            if (!File.Exists(abs)) continue;

            var snap = SnapshotScene(scenePath, includeInactive, includeTransforms, maxStringLength, maxArrayItems);
            string name = Path.GetFileNameWithoutExtension(scenePath);
            string outJsonAbs = Path.Combine(outAbs, $"{MakeSafeFilename(name)}_SceneSnapshot_{timestamp}.json");
            if (TryWriteJson(outJsonAbs, snap))
            {
                created.Add(outJsonAbs);
                if (packaging == FilesPackaging.ZipEach)
                {
                    string zipAbs = Path.ChangeExtension(outJsonAbs, ".zip");
                    TryZipSingleFile(outJsonAbs, zipAbs);
                }
            }
        }

        if (packaging == FilesPackaging.ZipAll && created.Count > 0)
        {
            string zipAbs = Path.Combine(outAbs, $"ScenesSnapshot_{timestamp}.zip");
            TryCreateZipFromFiles(created, zipAbs);
        }

        AssetDatabase.Refresh();
        Info($"Wrote {created.Count} scene snapshot(s) to:\n{outputFolderUnity}");
    }

    // =========================================================
    // Script analysis
    // =========================================================
    private static bool TryAnalyzeScript(string absPath, string unityPath, out ScriptEntry entry, out bool isMB, out bool isSO)
    {
        entry = null;
        isMB = false;
        isSO = false;

        string sourceText;
        try { sourceText = File.ReadAllText(absPath); }
        catch (Exception e) { sourceText = $"// ERROR reading file: {e.Message}\n"; }

        var mono = AssetDatabase.LoadAssetAtPath<MonoScript>(unityPath);

        Type type = null;
        try { if (mono != null) type = mono.GetClass(); } catch { }

        isMB = type != null && typeof(MonoBehaviour).IsAssignableFrom(type) && !type.IsAbstract;
        isSO = type != null && typeof(ScriptableObject).IsAssignableFrom(type) && !type.IsAbstract;

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

        entry = new ScriptEntry
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
        };
        return true;
    }

    private static List<ScriptEntry> OrderEntries(List<ScriptEntry> entries)
    {
        return entries
            .OrderBy(e => e.kind.ToString(), StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => string.IsNullOrEmpty(e.fullName) ? e.filePath : e.fullName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // =========================================================
    // Scene snapshot (FLAT) + safe open/close
    // =========================================================
    private static SceneSnapshot SnapshotScene(
        string sceneUnityPath,
        bool includeInactive,
        bool includeTransforms,
        int maxStringLen,
        int maxArrayItems)
    {
        var snap = new SceneSnapshot
        {
            scenePath = sceneUnityPath,
            sceneName = Path.GetFileNameWithoutExtension(sceneUnityPath),
            unityVersion = Application.unityVersion,
            generatedAt = DateTime.UtcNow.ToString("o"),
            includesInactive = includeInactive,
            includesTransforms = includeTransforms
        };

        // If already loaded, don't open/close it.
        var existing = EditorSceneManager.GetSceneByPath(sceneUnityPath);
        bool alreadyLoaded = existing.IsValid() && existing.isLoaded;

        // Remember current scene setup so we can safely restore.
        var setup = EditorSceneManager.GetSceneManagerSetup();

        Scene sceneToUse;
        if (alreadyLoaded)
        {
            sceneToUse = existing;
        }
        else
        {
            sceneToUse = EditorSceneManager.OpenScene(sceneUnityPath, OpenSceneMode.Additive);
        }

        try
        {
            snap.rootCount = sceneToUse.rootCount;

            var roots = sceneToUse.GetRootGameObjects();
            int total = 0;

            // Non-recursive traversal → flat list
            var stack = new Stack<(Transform t, string parentPath)>();
            foreach (var go in roots)
                stack.Push((go.transform, "")); // root

            while (stack.Count > 0)
            {
                var (t, parentPath) = stack.Pop();
                var go = t.gameObject;

                if (!includeInactive && !go.activeInHierarchy) continue;

                string myPath = string.IsNullOrEmpty(parentPath) ? go.name : $"{parentPath}/{go.name}";

                var flat = new GOFlat
                {
                    name = go.name,
                    path = myPath,
                    parentPath = parentPath,
                    active = go.activeSelf,
                    tag = SafeTag(go),
                    layer = go.layer,
                    instanceId = go.GetInstanceID(),
                    globalObjectId = TryGlobalId(go)
                };

                // Prefab info
                try
                {
                    flat.isPrefabInstance = PrefabUtility.IsPartOfPrefabInstance(go);
                    if (flat.isPrefabInstance)
                    {
                        var src = PrefabUtility.GetCorrespondingObjectFromSource(go);
                        if (src != null)
                        {
                            flat.prefabAssetPath = AssetDatabase.GetAssetPath(src);
                            flat.prefabSourceGlobalId = TryGlobalId(src);
                        }
                        flat.prefabOverridesSummary = SummarizePrefabOverrides(go);
                    }
                }
                catch { /* ignore */ }

                if (includeTransforms)
                {
                    flat.transform = new TransformLite
                    {
                        localPosition = t.localPosition,
                        localRotation = t.localRotation,
                        localScale = t.localScale
                    };
                }

                // Components (including missing-script detection)
                var comps = go.GetComponents<Component>();
                for (int ci = 0; ci < comps.Length; ci++)
                {
                    var comp = comps[ci];
                    if (comp == null)
                    {
                        flat.components.Add(new ComponentEntry
                        {
                            componentId = ci,
                            type = "MissingScript",
                            typeFullName = null,
                            missingScript = true,
                            props = null
                        });
                        continue;
                    }

                    var ctype = comp.GetType();
                    flat.components.Add(new ComponentEntry
                    {
                        componentId = ci,
                        type = ctype.Name,
                        typeFullName = ctype.FullName,
                        missingScript = false,
                        props = CollectProps(comp, maxStringLen, maxArrayItems)
                    });
                }

                snap.objects.Add(flat);
                total++;

                // Push children
                for (int i = 0; i < t.childCount; i++)
                {
                    var child = t.GetChild(i);
                    stack.Push((child, myPath));
                }
            }

            snap.totalObjects = total;
        }
        finally
        {
            // Only close the scene if we opened it ourselves
            if (!alreadyLoaded)
            {
                // If closing would leave zero scenes, open a temp empty one first
                if (SceneManager.sceneCount <= 1)
                {
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                }

                EditorSceneManager.CloseScene(sceneToUse, removeScene: true);

                // Restore the previous setup
                if (setup != null && setup.Length > 0)
                {
                    EditorSceneManager.RestoreSceneManagerSetup(setup);
                }
            }
        }

        return snap;
    }

    private static string SummarizePrefabOverrides(GameObject go)
{
    try
    {
        int objectOverrides = 0;
        int addedComponents = 0;
        int propertyMods    = 0;

        // Object overrides (rename, replace, etc.)
        try
        {
            var objs = PrefabUtility.GetObjectOverrides(go);
            objectOverrides = objs?.Count ?? 0;
        } catch { /* ignore */ }

        // Added components on instance
        try
        {
            var adds = PrefabUtility.GetAddedComponents(go);
            addedComponents = adds?.Count ?? 0;
        } catch { /* ignore */ }

        // Property modifications on this instance
        try
        {
            var mods = PrefabUtility.GetPropertyModifications(go);
            propertyMods = mods?.Length ?? 0;
        } catch { /* ignore */ }

        return $"objects:{objectOverrides}, addedComponents:{addedComponents}, propertyMods:{propertyMods}";
    }
    catch
    {
        return null;
    }
}

    private static string TryGlobalId(UnityEngine.Object o)
    {
        try
        {
#if UNITY_2020_3_OR_NEWER
            var gid = GlobalObjectId.GetGlobalObjectIdSlow(o);
            return gid.ToString();
#else
            return null;
#endif
        }
        catch { return null; }
    }

    // =========================================================
    // Serialized property capture (typed)
    // =========================================================
    private static List<PropEntry> CollectProps(Component comp, int maxStringLen, int maxArrayItems)
    {
        var so = new SerializedObject(comp);
        var it = so.GetIterator();
        var list = new List<PropEntry>();

        bool enterChildren = true;
        while (it.NextVisible(enterChildren))
        {
            enterChildren = false;
            if (it.propertyPath == "m_Script") continue;

            var p = ConvertProperty(it, maxStringLen, maxArrayItems, comp.gameObject.scene);
            if (p != null) list.Add(p);
        }

        return list.Count == 0 ? null : list;
    }

    private static PropEntry ConvertProperty(SerializedProperty sp, int maxStringLen, int maxArrayItems, Scene sceneCtx)
    {
        var prop = new PropEntry
        {
            name = sp.displayName,
            path = sp.propertyPath
        };

        switch (sp.propertyType)
        {
            case SerializedPropertyType.Integer:
                prop.kind = "int";
                prop.intValue = sp.intValue;
                return prop;

            case SerializedPropertyType.Boolean:
                prop.kind = "bool";
                prop.boolValue = sp.boolValue;
                return prop;

            case SerializedPropertyType.Float:
                prop.kind = "float";
                prop.floatValue = sp.floatValue;
                return prop;

            case SerializedPropertyType.Enum:
                prop.kind = "enum";
                prop.enumValue =
                    (sp.enumDisplayNames != null && sp.enumValueIndex >= 0 && sp.enumValueIndex < sp.enumDisplayNames.Length)
                        ? sp.enumDisplayNames[sp.enumValueIndex]
                        : sp.intValue.ToString();
                return prop;

            case SerializedPropertyType.String:
                prop.kind = "string";
                prop.stringValue = TrimString(sp.stringValue, maxStringLen);
                return prop;

            case SerializedPropertyType.Color:
                prop.kind = "Color";
                prop.color = sp.colorValue;
                return prop;

            case SerializedPropertyType.Vector2:
                prop.kind = "Vector2";
                prop.v2 = sp.vector2Value;
                return prop;

            case SerializedPropertyType.Vector3:
                prop.kind = "Vector3";
                prop.v3 = sp.vector3Value;
                return prop;

            case SerializedPropertyType.Vector4:
                prop.kind = "Vector4";
                prop.v4 = sp.vector4Value;
                return prop;

            case SerializedPropertyType.Quaternion:
                prop.kind = "Quaternion";
                {
                    var q = sp.quaternionValue;
                    prop.v4 = new Vector4(q.x, q.y, q.z, q.w);
                }
                return prop;

            case SerializedPropertyType.Rect:
                prop.kind = "Rect";
                prop.rect = sp.rectValue;
                return prop;

            case SerializedPropertyType.ObjectReference:
                return FillObjectRef(prop, sp, sceneCtx);

            case SerializedPropertyType.ArraySize:
                return null; // ignore size nodes; arrays handled via isArray below

            default:
                if (sp.isArray && sp.propertyType == SerializedPropertyType.Generic)
                {
                    prop.kind = "Array";
                    prop.arrayCount = sp.arraySize;
                    int n = Math.Min(sp.arraySize, Math.Max(0, maxArrayItems));
                    prop.arraySampleCount = n;
                    var items = new List<ArrayItem>(n);
                    for (int i = 0; i < n; i++)
                    {
                        var el = sp.GetArrayElementAtIndex(i);
                        var ai = ConvertArrayElement(el, maxStringLen, maxArrayItems, sceneCtx);
                        if (ai != null) items.Add(ai);
                    }
                    prop.arrayItems = items.Count > 0 ? items : null;
                    return prop;
                }
                return null;
        }
    }

    private static ArrayItem ConvertArrayElement(SerializedProperty el, int maxStringLen, int maxArrayItems, Scene sceneCtx)
    {
        var a = new ArrayItem();

        switch (el.propertyType)
        {
            case SerializedPropertyType.Integer:
                a.kind = "int"; a.intValue = el.intValue; return a;
            case SerializedPropertyType.Boolean:
                a.kind = "bool"; a.boolValue = el.boolValue; return a;
            case SerializedPropertyType.Float:
                a.kind = "float"; a.floatValue = el.floatValue; return a;
            case SerializedPropertyType.Enum:
                a.kind = "enum"; a.enumValue =
                    (el.enumDisplayNames != null && el.enumValueIndex >= 0 && el.enumValueIndex < el.enumDisplayNames.Length)
                        ? el.enumDisplayNames[el.enumValueIndex]
                        : el.intValue.ToString(); return a;
            case SerializedPropertyType.String:
                a.kind = "string"; a.stringValue = TrimString(el.stringValue, maxStringLen); return a;
            case SerializedPropertyType.Color:
                a.kind = "Color"; a.color = el.colorValue; return a;
            case SerializedPropertyType.Vector2:
                a.kind = "Vector2"; a.v2 = el.vector2Value; return a;
            case SerializedPropertyType.Vector3:
                a.kind = "Vector3"; a.v3 = el.vector3Value; return a;
            case SerializedPropertyType.Vector4:
                a.kind = "Vector4"; a.v4 = el.vector4Value; return a;
            case SerializedPropertyType.Quaternion:
                a.kind = "Quaternion";
                {
                    var q = el.quaternionValue;
                    a.v4 = new Vector4(q.x, q.y, q.z, q.w);
                }
                return a;
            case SerializedPropertyType.ObjectReference:
                {
                    var p = FillObjectRef(new PropEntry(), el, sceneCtx);
                    a.kind = "ObjectReference";
                    a.refName = p.refName;
                    a.refType = p.refType;
                    a.refAssetPath = p.refAssetPath;
                    a.refScenePath = p.refScenePath;
                    a.refGuid = p.refGuid;
                    a.refLocalFileId = p.refLocalFileId;
                    a.refGlobalObjectId = p.refGlobalObjectId;
                    return a;
                }
            default:
                return null;
        }
    }

    private static PropEntry FillObjectRef(PropEntry target, SerializedProperty sp, Scene sceneCtx)
    {
        target.kind = "ObjectReference";
        UnityEngine.Object o = sp.objectReferenceValue;
        if (o == null)
        {
            target.stringValue = "null";
            return target;
        }

        target.refName = o.name;
        target.refType = o.GetType().Name;
        target.refAssetPath = AssetDatabase.GetAssetPath(o);

        // GUID/localFileId (assets)
        if (!string.IsNullOrEmpty(target.refAssetPath))
        {
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o, out string guid, out long localId))
            {
                target.refGuid = guid;
                target.refLocalFileId = localId;
            }
        }

        // Scene path + GlobalObjectId
        if (string.IsNullOrEmpty(target.refAssetPath))
        {
            if (o is Component c && c != null && c.gameObject.scene == sceneCtx)
            {
                target.refScenePath = GetHierarchyPath(c.transform);
                target.refGlobalObjectId = TryGlobalId(c);
            }
            else if (o is GameObject go && go != null && go.scene == sceneCtx)
            {
                target.refScenePath = GetHierarchyPath(go.transform);
                target.refGlobalObjectId = TryGlobalId(go);
            }
        }

        // InputActionReference extras
        if (o is InputActionReference iar && iar != null)
        {
            try
            {
                var act = iar.action;
                if (act != null)
                {
                    target.inputActionMap = act.actionMap?.name;
                    target.inputActionName = act.name;
                }
            }
            catch { /* ignore */ }
        }

        return target;
    }

    private static string GetHierarchyPath(Transform t)
    {
        var stack = new Stack<string>();
        while (t != null)
        {
            stack.Push(t.name);
            t = t.parent;
        }
        return string.Join("/", stack);
    }

    private static string TrimString(string s, int maxLen)
    {
        if (s == null) return null;
        if (maxLen <= 0) return "";
        if (s.Length <= maxLen) return s;
        return s.Substring(0, maxLen) + "…";
    }

    private static string SafeTag(GameObject go)
    {
        try { return go.tag; } catch { return "Untagged"; }
    }

    // =========================================================
    // Shared utilities
    // =========================================================
    private static bool IsAssetsPath(string unityPath) =>
        !string.IsNullOrEmpty(unityPath) && unityPath.StartsWith("Assets", StringComparison.Ordinal);

    private static string EnsureOutputFolder(string outputFolderUnity)
    {
        string outAbs = Path.GetFullPath(outputFolderUnity);
        Directory.CreateDirectory(outAbs);
        return outAbs;
    }

    private static string ToUnityPath(string abs)
    {
        abs = abs.Replace('\\', '/');
        string root = Application.dataPath.Replace('\\', '/');
        if (abs.StartsWith(root)) return "Assets" + abs.Substring(root.Length);
        return abs;
    }

    private static string ToAssetsPath(string absPath)
    {
        absPath = absPath.Replace('\\', '/');
        string root = Application.dataPath.Replace('\\', '/');
        return absPath.StartsWith(root) ? "Assets" + absPath.Substring(root.Length) : absPath;
    }

    private static bool TryWriteJson<T>(string absPath, T payload)
    {
        try
        {
            File.WriteAllText(absPath, JsonUtility.ToJson(payload, true));
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[SourceDump] Failed to write {absPath}: {e.Message}");
            EditorUtility.DisplayDialog("Source Dump", $"Failed to write:\n{absPath}\n\n{e.Message}", "OK");
            return false;
        }
    }

    private static void TryCreateZipFromDirectory(string dirAbs, string zipAbs)
    {
        try
        {
            if (File.Exists(zipAbs)) File.Delete(zipAbs);
            IOComp.ZipFile.CreateFromDirectory(
                dirAbs,
                zipAbs,
                IOComp.CompressionLevel.Optimal,
                includeBaseDirectory: false
            );
            Debug.Log($"[SourceDump] Zipped folder to: {zipAbs}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SourceDump] Failed to zip folder: {ex.Message}");
        }
    }

    private static void TryCreateZipFromFiles(List<string> filesAbs, string zipAbs)
    {
        try
        {
            if (File.Exists(zipAbs)) File.Delete(zipAbs);
            using (var zip = IOComp.ZipFile.Open(zipAbs, IOComp.ZipArchiveMode.Create))
            {
                foreach (var f in filesAbs)
                {
                    System.IO.Compression.ZipFileExtensions.CreateEntryFromFile(
                        zip,
                        f,
                        Path.GetFileName(f),
                        IOComp.CompressionLevel.Optimal
                    );
                }
            }
            Debug.Log($"[SourceDump] Zipped {filesAbs.Count} files to: {zipAbs}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SourceDump] Failed to create zip: {ex.Message}");
        }
    }

    private static void TryZipSingleFile(string fileAbs, string zipAbs)
    {
        try
        {
            if (File.Exists(zipAbs)) File.Delete(zipAbs);
            using (var zip = IOComp.ZipFile.Open(zipAbs, IOComp.ZipArchiveMode.Create))
            {
                System.IO.Compression.ZipFileExtensions.CreateEntryFromFile(
                    zip,
                    fileAbs,
                    Path.GetFileName(fileAbs),
                    IOComp.CompressionLevel.Optimal
                );
            }
            Debug.Log($"[SourceDump] Zipped file to: {zipAbs}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SourceDump] Failed to zip file: {ex.Message}");
        }
    }

    private static string MakeTimestamp() => DateTime.Now.ToString("yyyy-MM-dd_HH-mm");

    private static string MakeSafeFilename(string s)
    {
        foreach (var ch in Path.GetInvalidFileNameChars()) s = s.Replace(ch, '_');
        return s.Replace('<', '_').Replace('>', '_').Replace(':', '_').Replace('/', '_').Replace('\\', '_');
    }

    private static string[] TryExtractDeclaredTypes(string source)
    {
        if (string.IsNullOrEmpty(source)) return Array.Empty<string>();
        try
        {
            var rx = new Regex(@"\b(class|struct|interface|enum)\s+([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);
            var m = rx.Matches(source);
            if (m.Count == 0) return Array.Empty<string>();
            var names = new List<string>(m.Count);
            foreach (Match mm in m) if (mm.Groups.Count >= 3) names.Add(mm.Groups[2].Value);
            return names.Distinct().ToArray();
        }
        catch { return Array.Empty<string>(); }
    }

    private static void Info(string msg) => EditorUtility.DisplayDialog("Source Dump", msg, "OK");

    // =========================================================
    // GUI – one menu item
    // =========================================================
    private class SourceDumpToolWindow : EditorWindow
    {
        private enum Scope { Files, Folder, Scenes }
        private enum Filter { MonoBehavioursOnly, AllCs }
        private enum OutputKind { JoinedJson, SplitJson }
        private enum FolderPackaging { None, Zip }

        private Scope scope = Scope.Files;
        private Filter filter = Filter.AllCs;
        private OutputKind outputKind = OutputKind.JoinedJson; // Folder only
        private FolderPackaging folderPackaging = FolderPackaging.None;
        private FilesPackaging filesPackaging = FilesPackaging.None;

        // Files
        private readonly List<string> filesUnity = new List<string>();

        // Folder
        private string folderUnityPath = "Assets";

        // Scenes
        private readonly List<string> scenesUnity = new List<string>();
        private bool includeInactive = true;
        private bool includeTransforms = true;
        private int maxStringLength = 200;
        private int maxArrayItems = 10;

        // Output
        private string outputFolderUnity = "Assets";

        [MenuItem("Tools/SourceDumpTool")]
        private static void Open()
        {
            var win = GetWindow<SourceDumpToolWindow>("SourceDumpTool");
            win.minSize = new Vector2(600, 480);
            win.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Source Dump", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            scope = (Scope)EditorGUILayout.EnumPopup(
                new GUIContent("Scope",
                    "Files: export selected .cs files.\n" +
                    "Folder: scan a folder recursively for .cs.\n" +
                    "Scenes: snapshot .unity scenes (hierarchy + components + fields)."),
                scope);

            if (scope != Scope.Scenes)
            {
                filter = (Filter)EditorGUILayout.EnumPopup(
                    new GUIContent("Filter",
                        "AllCs: include every .cs file.\n" +
                        "MonoBehavioursOnly: only non-abstract MonoBehaviour classes."),
                    filter);
            }

            if (scope == Scope.Folder)
            {
                outputKind = (OutputKind)EditorGUILayout.EnumPopup(
                    new GUIContent("Output (Folder)",
                        "JoinedJson: one JSON with all scripts.\n" +
                        "SplitJson: one JSON per script (goes into a timestamped subfolder)."),
                    outputKind);

                folderPackaging = (FolderPackaging)EditorGUILayout.EnumPopup(
                    new GUIContent("Packaging (Folder)",
                        "None: JSON only.\nZip: zip the joined JSON or the split folder."),
                    folderPackaging);
            }
            else
            {
                filesPackaging = (FilesPackaging)EditorGUILayout.EnumPopup(
                    new GUIContent(scope == Scope.Scenes ? "Packaging (Scenes)" : "Packaging (Files)",
                        "None: JSON only.\nZipEach: zip each JSON.\nZipAll: one zip containing all JSONs."),
                    filesPackaging);
            }

            EditorGUILayout.Space(6);
            switch (scope)
            {
                case Scope.Files:  DrawFilesPicker();  break;
                case Scope.Folder: DrawFolderPicker(); break;
                case Scope.Scenes: DrawScenesPicker(); break;
            }

            EditorGUILayout.Space(8);
            DrawOutputFolderPicker();

            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(!CanRun()))
            {
                if (GUILayout.Button("Run Export", GUILayout.Height(32)))
                {
                    Run();
                }
            }

            EditorGUILayout.Space(6);
            DrawHelp();
        }

        private void DrawFilesPicker()
        {
            EditorGUILayout.LabelField("Files", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add File…", GUILayout.Width(120)))
            {
                var abs = EditorUtility.OpenFilePanel("Choose C# file", Application.dataPath, "cs");
                AddIfInsideAssets(abs, filesUnity);
            }
            if (GUILayout.Button("Add Selected from Project", GUILayout.Width(210)))
            {
                foreach (var o in Selection.objects)
                {
                    string p = AssetDatabase.GetAssetPath(o);
                    if (!string.IsNullOrEmpty(p) && p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                        if (!filesUnity.Contains(p)) filesUnity.Add(p);
                }
            }
            if (GUILayout.Button("Clear", GUILayout.Width(80))) filesUnity.Clear();
            EditorGUILayout.EndHorizontal();

            if (filesUnity.Count == 0)
                EditorGUILayout.HelpBox("No files selected. Use 'Add File…' or select .cs in Project and click 'Add Selected from Project'.", MessageType.Info);
            else
                DrawList(filesUnity);
        }

        private void DrawFolderPicker()
        {
            EditorGUILayout.LabelField("Target Folder", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            folderUnityPath = EditorGUILayout.TextField(new GUIContent("Assets-relative"), folderUnityPath);
            if (GUILayout.Button("Browse…", GUILayout.Width(90)))
            {
                var abs = EditorUtility.OpenFolderPanel("Choose folder (inside Assets)", Application.dataPath, "");
                var unity = ToUnityFromDialog(abs);
                if (unity != null) folderUnityPath = unity;
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox(folderUnityPath, MessageType.None);
        }

        private void DrawScenesPicker()
        {
            EditorGUILayout.LabelField("Scenes", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add Scene…", GUILayout.Width(120)))
            {
                var abs = EditorUtility.OpenFilePanel("Choose Scene", Application.dataPath, "unity");
                AddIfInsideAssets(abs, scenesUnity);
            }
            if (GUILayout.Button("Add Selected from Project", GUILayout.Width(210)))
            {
                foreach (var o in Selection.objects)
                {
                    string p = AssetDatabase.GetAssetPath(o);
                    if (!string.IsNullOrEmpty(p) && p.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                        if (!scenesUnity.Contains(p)) scenesUnity.Add(p);
                }
            }
            if (GUILayout.Button("Clear", GUILayout.Width(80))) scenesUnity.Clear();
            EditorGUILayout.EndHorizontal();

            if (scenesUnity.Count == 0)
                EditorGUILayout.HelpBox("No scenes selected. Use 'Add Scene…' or select scenes in Project and click 'Add Selected from Project'.", MessageType.Info);
            else
                DrawList(scenesUnity);

            EditorGUILayout.Space(6);
            includeInactive = EditorGUILayout.Toggle(new GUIContent("Include Inactive Objects",
                "If ON: snapshot also includes inactive GameObjects."), includeInactive);
            includeTransforms = EditorGUILayout.Toggle(new GUIContent("Include Transforms",
                "If ON: includes local position/rotation/scale for each object."), includeTransforms);
            maxStringLength = EditorGUILayout.IntField(new GUIContent("Max String Length",
                "Strings longer than this are trimmed with an ellipsis."), Mathf.Max(0, maxStringLength));
            maxArrayItems = EditorGUILayout.IntField(new GUIContent("Max Array Items",
                "Only the first N items of arrays/lists are included."), Mathf.Max(0, maxArrayItems));
        }

        private void DrawOutputFolderPicker()
        {
            EditorGUILayout.LabelField("Output Folder", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            outputFolderUnity = EditorGUILayout.TextField(new GUIContent("Assets-relative"), outputFolderUnity);
            if (GUILayout.Button("Browse…", GUILayout.Width(90)))
            {
                var abs = EditorUtility.OpenFolderPanel("Choose output folder (inside Assets)", Application.dataPath, "");
                var unity = ToUnityFromDialog(abs);
                if (unity != null) outputFolderUnity = unity;
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox(outputFolderUnity, MessageType.None);
        }

        private void DrawList(List<string> items)
        {
            foreach (var p in items.ToList())
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(p, GUILayout.MaxHeight(18));
                if (GUILayout.Button("X", GUILayout.Width(22)))
                {
                    items.Remove(p);
                    EditorGUILayout.EndHorizontal();
                    continue;
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        private void Run()
        {
            try
            {
                string timestamp = MakeTimestamp();

                if (scope == Scope.Files)
                {
                    bool monoOnly = (filter == Filter.MonoBehavioursOnly);
                    SourceDumpTool.GenerateForFilesAtPaths(
                        new List<string>(filesUnity),
                        outputFolderUnity,
                        monoOnly,
                        filesPackaging,
                        timestamp);
                }
                else if (scope == Scope.Folder)
                {
                    bool includeAll = (filter == Filter.AllCs);
                    bool split = (outputKind == OutputKind.SplitJson);
                    bool zip = (folderPackaging == FolderPackaging.Zip);

                    SourceDumpTool.GenerateForFolderAtPath(
                        folderUnityPath,
                        outputFolderUnity,
                        includeAll,
                        split,
                        zip,
                        timestamp);
                }
                else // Scenes
                {
                    SourceDumpTool.GenerateForScenesAtPaths(
                        new List<string>(scenesUnity),
                        outputFolderUnity,
                        includeInactive,
                        includeTransforms,
                        maxStringLength,
                        maxArrayItems,
                        filesPackaging,
                        timestamp);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SourceDumpTool] Export failed: {ex.Message}");
                EditorUtility.DisplayDialog("Source Dump", $"Export failed:\n{ex.Message}", "OK");
            }
        }

        private bool CanRun()
        {
            if (!IsAssetsPath(outputFolderUnity)) return false;

            switch (scope)
            {
                case Scope.Files:   return filesUnity.Any(p => p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase));
                case Scope.Folder:  return IsAssetsPath(folderUnityPath) && Directory.Exists(Path.GetFullPath(folderUnityPath));
                case Scope.Scenes:  return scenesUnity.Any(p => p.EndsWith(".unity", StringComparison.OrdinalIgnoreCase));
            }
            return false;
        }

        private static void AddIfInsideAssets(string abs, List<string> list)
        {
            if (string.IsNullOrEmpty(abs)) return;
            abs = abs.Replace('\\', '/');
            var assets = Application.dataPath.Replace('\\', '/');
            if (abs.StartsWith(assets))
            {
                string unity = "Assets" + abs.Substring(assets.Length);
                if (!list.Contains(unity)) list.Add(unity);
            }
            else
            {
                EditorUtility.DisplayDialog("Source Dump", "Please choose items inside your project's Assets folder.", "OK");
            }
        }

        private static string ToUnityFromDialog(string abs)
        {
            if (string.IsNullOrEmpty(abs)) return null;
            abs = abs.Replace('\\', '/');
            var assets = Application.dataPath.Replace('\\', '/');
            if (!abs.StartsWith(assets))
            {
                EditorUtility.DisplayDialog("Source Dump", "Please choose a path inside Assets/…", "OK");
                return null;
            }
            string unity = "Assets" + abs.Substring(assets.Length);
            return string.IsNullOrEmpty(unity) ? "Assets" : unity;
        }

        private void DrawHelp()
        {
            EditorGUILayout.HelpBox(
                "Files: add .cs files → JSON per file (zip each/all optional).\n" +
                "Folder: scan folder for .cs → joined or split JSON (optional zip).\n" +
                "Scenes: snapshot .unity scenes → compact FLAT JSON with hierarchy paths, stable IDs, prefab info, and typed fields.\n" +
                "Use the Output Folder picker to choose where files go. Filenames include a timestamp.", MessageType.Info);
        }
    }
}
