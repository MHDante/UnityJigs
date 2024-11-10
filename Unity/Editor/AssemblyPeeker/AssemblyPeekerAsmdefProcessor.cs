using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityJigs.Attributes;
using UnityJigs.Extensions;

namespace UnityJigs.Editor.AssemblyPeeker
{
    public class AssemblyPeekerAsmdefProcessor : AssetPostprocessor
    {
        private void OnPreprocessAsset()
        {
            var settings = AssemblyPeekerSettings.instance;

            var settingsPath = AssetDatabase.GetAssetPath(settings);

            if (assetPath == settingsPath)
            {
                UpdateAllAssemblies(settings);
                return;
            }

            if (!assetPath.EndsWith("asmdef")) return;

            foreach (var peekedAssembly in settings.PeekedAssemblies)
            {
                if (peekedAssembly.Asmdef == null) continue;
                var peekedPath = AssetDatabase.GetAssetPath(peekedAssembly.Asmdef);
                if (peekedPath != assetPath) continue;
                UpdateAssembly(peekedPath, peekedAssembly);
                break;
            }

            var text = File.ReadAllText(assetPath);
            var name = AsmdefDeserializer.GetName(text);
            var peekedDict = GetAttributeAssemblies();
            if(peekedDict.TryGetValue(name, out var peekers))
            {
                foreach (var peeker in peekers)
                    AddIvtAttribute(GetInfoPath(assetPath), peeker);
            }
        }

        public static Dictionary<string, List<string>> GetAttributeAssemblies()
        {
            var result = new Dictionary<string, List<string>>();

            foreach (var ass in AppDomain.CurrentDomain.GetAssemblies())
            {
                var attr = ass.GetCustomAttribute<PeekAssemblyAttribute>();
                if (attr == null) continue;
                var list = result.TryGetValue(attr.AssemblyName, out var v) ? v :
                    result[attr.AssemblyName] = new List<string>();

                list.Add(ass.GetName().Name);
            }

            return result;
        }

        public static void UpdateAllAssemblies(AssemblyPeekerSettings settings)
        {
            var peekedDict = GetAttributeAssemblies();
            foreach (var peekedAssembly in settings.PeekedAssemblies)
            {
                if (peekedAssembly.Asmdef == null) continue;
                var asmdefPath = AssetDatabase.GetAssetPath(peekedAssembly.Asmdef);
                UpdateAssembly(asmdefPath, peekedAssembly);

                var name = AsmdefDeserializer.GetName(peekedAssembly.Asmdef.text);
                if (!peekedDict.TryGetValue(name, out var peekers)) continue;
                foreach (var peeker in peekers)
                    AddIvtAttribute(GetInfoPath(asmdefPath), peeker);
            }
        }

        private static void UpdateAssembly(string asmdefPath, PeekedAssembly peekedAssembly)
        {
            var targetPath = GetInfoPath(asmdefPath);
            foreach (var peekerAsmdef in peekedAssembly.PeekerAsmdefs)
            {
                if (peekerAsmdef == null) continue;
                var peekerName = AsmdefDeserializer.GetName(peekerAsmdef.text);
                AddIvtAttribute(targetPath, peekerName);

            }

            foreach (var peekerName in peekedAssembly.PeekerAssemblyNames)
            {
                AddIvtAttribute(targetPath, peekerName);
            }
        }

        private static string GetInfoPath(string asmdefPath)
        {
            var parent = Directory.GetParent(asmdefPath)?.ToString() ?? "";
            var targetPath = Path.Combine(parent, "AssemblyInfoModifier_Injected.g.cs");
            return targetPath;
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
                File.WriteAllText(metaPath, $"""
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

            public static string GetName(string text)
            {
                var obj = JsonUtility.FromJson<AsmdefDeserializer>(text);
                return obj?.name ?? throw new Exception("Bad Asmdef");
            }
        }
    }
}
