using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace MHDante.UnityUtils.Editor.AssemblyPeeker
{
    public class AssemblyPeekerAsmdefProcessor : AssetPostprocessor
    {
        private void OnPreprocessAsset()
        {
            var settings = AssemblyPeekerSettings.instance;

            var settingsPath = AssetDatabase.GetAssetPath(settings);

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
                AddIvtAttribute(targetPath, peekerName);
            }

            foreach (var peekerName in peekedAssembly.PeekerAssemblyNames)
            {
                AddIvtAttribute(targetPath, peekerName);
            }
        }

        private static void AddIvtAttribute(string assemblyInfoPath, string targetAssemblyName)
        {
            // Check if InternalsVisibleTo is already added
            // Todo: We should remove any attributes that are no longer in use. (maybe....)
            if (DoesIvtAttributeExist(assemblyInfoPath, targetAssemblyName)) return;

            // Add the InternalsVisibleTo attribute to the AssemblyInfo.cs file
            using (var writer = new StreamWriter(assemblyInfoPath, append: true))
            {
                writer.WriteLine(
                    $"[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"{targetAssemblyName}\")]");
            }

            var metaPath = assemblyInfoPath + ".meta";
            if (!File.Exists(metaPath))
            {
                File.WriteAllText(metaPath,$"""
                                   fileFormatVersion: 2
                                   guid: {GUID.Generate().ToString()}
                                   """);
            }

            Debug.Log($"Added InternalsVisibleTo attribute for {targetAssemblyName} to {assemblyInfoPath}");
        }

        private static bool DoesIvtAttributeExist(string assemblyInfoPath, string targetAssemblyName)
        {
            if (!File.Exists(assemblyInfoPath)) return false;
            string[] lines = File.ReadAllLines(assemblyInfoPath);
            var hasLine = lines.Any(line => line.Contains($"InternalsVisibleTo(\"{targetAssemblyName}\")"));
            return hasLine;
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
    }
}
