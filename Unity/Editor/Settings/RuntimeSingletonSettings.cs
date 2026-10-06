using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityJigs.Types;
using Object = UnityEngine.Object;

namespace UnityJigs.Editor.Settings
{
    /// <summary>
    /// Project Settings pages for every <see cref="RuntimeScriptableSingleton{T}"/> marked [SettingsPage]: the preloaded
    /// asset's inspector drawn inline (edits are saved as they're made), or — when it doesn't exist yet — a Create button
    /// that makes it at the attribute's AssetPath and adds it to Preloaded Assets. An asset that exists but isn't preloaded
    /// (builds can't find it) gets a Preload button.
    /// </summary>
    public static class RuntimeSingletonSettings
    {
        [SettingsProviderGroup]
        public static SettingsProvider[] GetProviders()
        {
            var result = new List<SettingsProvider>();
            foreach (var type in TypeCache.GetTypesWithAttribute<SettingsPageAttribute>())
            {
                if (type.IsAbstract || !IsRuntimeSingleton(type))
                {
                    Debug.LogError($"[SettingsPage] {type.Name} must be a concrete RuntimeScriptableSingleton<T>.");
                    continue;
                }
                var page = (SettingsPageAttribute)Attribute.GetCustomAttribute(type, typeof(SettingsPageAttribute));
                var context = new SettingsContext();
                result.Add(new SettingsProvider(page.Path, SettingsScope.Project)
                {
                    guiHandler = search =>
                    {
                        context.SearchContext = search;
                        Draw(type, page, context);
                    },
                });
            }
            return result.ToArray();
        }

        /// <summary>The preloaded instance of <paramref name="type"/> (null: none is preloaded).</summary>
        public static ScriptableObject? FindPreloaded(Type type)
        {
            foreach (var asset in PlayerSettings.GetPreloadedAssets())
                if (asset && type.IsInstanceOfType(asset))
                    return (ScriptableObject)asset;
            return null;
        }

        /// <summary>Any asset of <paramref name="type"/> in the project, preloaded or not (null: none).</summary>
        public static ScriptableObject? FindAsset(Type type)
        {
            var preloaded = FindPreloaded(type);
            if (preloaded) return preloaded;
            foreach (var guid in AssetDatabase.FindAssets("t:" + type.Name))
            {
                var asset = AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(guid), type);
                if (asset) return (ScriptableObject)asset;
            }
            return null;
        }

        /// <summary>Creates the asset at <paramref name="assetPath"/> (folders included) and preloads it.</summary>
        public static ScriptableObject Create(Type type, string assetPath)
        {
            var folder = Path.GetDirectoryName(assetPath)!.Replace('\\', '/');
            EnsureFolder(folder);
            var asset = ScriptableObject.CreateInstance(type);
            AssetDatabase.CreateAsset(asset, assetPath);
            Preload(asset);
            return asset;
        }

        /// <summary>
        /// Adds <paramref name="asset"/> to Preloaded Assets (Player Settings) and writes that entry to disk at once —
        /// Player Settings otherwise reach disk only on a project save (SaveAssetIfDirty doesn't write them, and
        /// SaveAssets would flush every other dirty asset too). Only the one line is inserted; the rest of the file is
        /// untouched.
        /// </summary>
        public static void Preload(Object asset)
        {
            var list = new List<Object>(PlayerSettings.GetPreloadedAssets());
            list.RemoveAll(it => !it);
            if (!list.Contains(asset))
            {
                list.Add(asset);
                PlayerSettings.SetPreloadedAssets(list.ToArray());
            }
            MirrorPreloadToDisk(asset);
        }

        private const string ProjectSettingsPath = "ProjectSettings/ProjectSettings.asset";

        private static void MirrorPreloadToDisk(Object asset)
        {
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out var guid, out long fileId)) return;
            var text = File.ReadAllText(ProjectSettingsPath);
            if (text.Contains("guid: " + guid)) return;
            var entry = $"  - {{fileID: {fileId}, guid: {guid}, type: {(AssetDatabase.IsNativeAsset(asset) ? 2 : 3)}}}\n";
            const string empty = "  preloadedAssets: []\n";
            const string header = "  preloadedAssets:\n";
            string mirrored;
            if (text.Contains(empty)) mirrored = text.Replace(empty, header + entry);
            else
            {
                var start = text.IndexOf(header, StringComparison.Ordinal);
                if (start < 0)
                {
                    Debug.LogWarning($"[SettingsPage] No preloadedAssets block in {ProjectSettingsPath}: it is written " +
                                     "on the next project save.");
                    return;
                }
                var end = start + header.Length;
                while (text.IndexOf("  - {", end, StringComparison.Ordinal) == end) end = text.IndexOf('\n', end) + 1;
                mirrored = text.Insert(end, entry);
            }
            File.WriteAllText(ProjectSettingsPath, mirrored);
        }

        private static void Draw(Type type, SettingsPageAttribute page, SettingsContext context)
        {
            var asset = FindPreloaded(type);
            if (!asset)
            {
                var loose = FindAsset(type);
                if (loose)
                {
                    EditorGUILayout.HelpBox($"{AssetDatabase.GetAssetPath(loose)} isn't in Preloaded Assets: builds can't " +
                                            "find it.", MessageType.Warning);
                    if (GUILayout.Button("Preload it")) Preload(loose!);
                }
                else
                {
                    EditorGUILayout.HelpBox($"No {ObjectNames.NicifyVariableName(type.Name)} asset yet.", MessageType.Info);
                    if (GUILayout.Button($"Create {page.AssetPath}")) Create(type, page.AssetPath);
                }
                return;
            }

            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField("Asset", asset, type, false);
            EditorGUILayout.Space();
            UnityEditor.Editor.CreateCachedEditor(asset, null, ref context.CachedEditor);
            context.CachedEditor?.OnInspectorGUI();
            if (EditorUtility.IsDirty(asset)) AssetDatabase.SaveAssetIfDirty(asset);
        }

        private static bool IsRuntimeSingleton(Type type)
        {
            for (var t = type.BaseType; t != null; t = t.BaseType)
                if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(RuntimeScriptableSingleton<>))
                    return true;
            return false;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            var parent = Path.GetDirectoryName(folder)!.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
