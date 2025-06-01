using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityJigs.Editor.Utilities;

namespace UnityJigs.Editor.AssemblyPeeker
{
    public class AssemblyPeekerAsmdefProcessor : AssetPostprocessor
    {
        private void OnPreprocessAsset()
        {
            if (!assetPath.EndsWith("asmdef"))
            {
                if (context.mainObject is AssemblyPeeker assPeeker) Apply(assPeeker);
                return;
            }

            var assemblyPeekers = EditorUtils.FindAllAssetsOfType<AssemblyPeeker>();
            foreach (var assPeeker in assemblyPeekers)
            {
                foreach (var peekedAssembly in assPeeker.PeekedAssemblies)
                {
                    if (peekedAssembly == null) continue;
                    if (assPeeker.Peeker == null) continue;
                    var peekedPath = AssetDatabase.GetAssetPath(peekedAssembly);
                    if (peekedPath != assetPath) continue;
                    UpdateAssembly(peekedPath, assPeeker.Peeker);
                    break;
                }

            }
        }

        public static void Apply(AssemblyPeeker settings)
        {
            foreach (var peekedAssembly in settings.PeekedAssemblies)
            {
                if (peekedAssembly == null) continue;
                var asmdefPath = AssetDatabase.GetAssetPath(peekedAssembly);
                UpdateAssembly(asmdefPath, peekedAssembly);
            }
        }

        private static void UpdateAssembly(string peekedPath, AssemblyDefinitionAsset peekerAsmdef)
        {
            var targetPath = GetInfoPath(peekedPath);
            var peekerName = AsmdefDeserializer.GetName(peekerAsmdef.text);
            AddIvtAttribute(targetPath, peekerName);
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
                File.WriteAllText(metaPath, $"fileFormatVersion: 2\nguid: {GUID.Generate().ToString()}");
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
