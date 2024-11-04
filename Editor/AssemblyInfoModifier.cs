using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace MHDante.UnityUtils.Editor
{
    [FilePath(FilePath, FilePathAttribute.Location.ProjectFolder)]
    public class AssemblyPeekerSettings : ScriptableSingleton<AssemblyPeekerSettings>
    {
        public const string FilePath = "ProjectSettings/AssemblyPeeker.asset";
        public PeekedAssembly[] PeekedAssemblies = { };
        private void Awake() => Save(true);
        private void OnValidate() => AssemblyInfoModifier.UpdateAllAssemblies(this);
    }

    [Serializable]
    public class PeekedAssembly
    {
        public AssemblyDefinitionAsset? Asmdef;
        public AssemblyDefinitionAsset?[] PeekerAsmdefs = { };
        public string[] PeekerAssemblyNames = { };
    }

    public class AssemblyInfoModifier : AssetPostprocessor
    {
        private void OnPreprocessAsset()
        {
            var settings = AssemblyPeekerSettings.instance;

            var settingsPath = AssetDatabase.GetAssetPath(settings);
            Debug.Log(settingsPath);

            if (assetPath == settingsPath) UpdateAllAssemblies(settings);

            if (!assetPath.EndsWith("asmdef")) return;

            foreach (var peekedAssembly in settings.PeekedAssemblies)
            {
                if (peekedAssembly.Asmdef == null) continue;
                var asmdefPath = assetPath;
                UpdateAssembly(asmdefPath, peekedAssembly);
                break;
            }
        }

        public static void UpdateAllAssemblies(AssemblyPeekerSettings settings)
        {
            foreach (var peekedAssembly in settings.PeekedAssemblies)
            {
                if (peekedAssembly.Asmdef == null) continue;
                var asmdefPath = AssetDatabase.GetAssetPath(peekedAssembly.Asmdef);
                UpdateAssembly(asmdefPath, peekedAssembly);
            }
        }

        private static void UpdateAssembly(string asmdefPath, PeekedAssembly peekedAssembly)
        {
            var parent = Directory.GetParent(asmdefPath)?.ToString() ?? "";
            var targetPath = Path.Combine(parent, "AssemblyInfoModifier_Injected.g.cs");
            foreach (var peekerAsmdef in peekedAssembly.PeekerAsmdefs)
            {
                if (peekerAsmdef == null) continue;
                var peekerName = AsmdefDeserializer.GetName(peekerAsmdef);
                AddInternalsVisibleToAttribute(targetPath, peekerName);
            }

            foreach (var peekerName in peekedAssembly.PeekerAssemblyNames)
            {
                AddInternalsVisibleToAttribute(targetPath, peekerName);
            }
        }

        private static void AddInternalsVisibleToAttribute(string assemblyInfoPath, string targetAssemblyName)
        {
            string[] lines = File.ReadAllLines(assemblyInfoPath);

            // Check if InternalsVisibleTo is already added
            if (lines.Any(line => line.Contains($"InternalsVisibleTo(\"{targetAssemblyName}\")")))
            {
                return;
            }

            using (StreamWriter writer = new StreamWriter(assemblyInfoPath, append: true))
            {
                writer.WriteLine(
                    $"[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"{targetAssemblyName}\")]");
            }

            Debug.Log($"Added InternalsVisibleTo attribute for {targetAssemblyName} to {assemblyInfoPath}");
        }

        [Serializable]
        private class AsmdefDeserializer
        {
            // ReSharper disable once InconsistentNaming
            public string? name;

            public static string GetName(AssemblyDefinitionAsset asset)
            {
                var obj = JsonUtility.FromJson<AsmdefDeserializer>(asset.text);
                return obj?.name ?? throw new Exception("Bad Asmdef");
            }
        }

// Add the InternalsVisibleTo attribute to the AssemblyInfo.cs file
    }

    public class AssemblyPeekerSettingsProvider : SettingsProvider
    {
        public UnityEditor.Editor? SettingsEditor;

        public override void OnGUI(string searchContext)
        {
            UnityEditor.Editor.CreateCachedEditor(AssemblyPeekerSettings.instance, null, ref SettingsEditor);
            if (SettingsEditor == null) return;
            SettingsEditor.OnInspectorGUI();
        }

        [SettingsProvider]
        public static SettingsProvider Provide() =>
            new AssemblyPeekerSettingsProvider("Assembly Picker", SettingsScope.Project);

        public AssemblyPeekerSettingsProvider(string path, SettingsScope scopes, IEnumerable<string>? keywords = null)
            : base(path, scopes, keywords) { }
    }
}
