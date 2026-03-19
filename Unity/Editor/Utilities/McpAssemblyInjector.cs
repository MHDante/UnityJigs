#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace UnityJigs.Editor
{
    /// <summary>
    /// Injects package assembly references into the Unity AI Assistant's RunCommand compiler
    /// so that RunCommand scripts can access types from package assemblies.
    /// </summary>
    [InitializeOnLoad]
    static class McpAssemblyInjector
    {
        static McpAssemblyInjector()
        {
            EditorApplication.delayCall += InjectPackageAssemblies;
        }

        static void InjectPackageAssemblies()
        {
            try
            {
                // Find the RunCommandUtils type in the Unity AI Assistant assembly
                var runCommandUtilsType = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a =>
                    {
                        try { return a.GetTypes(); }
                        catch { return Type.EmptyTypes; }
                    })
                    .FirstOrDefault(t => t.FullName == "Unity.AI.Assistant.Editor.RunCommand.RunCommandUtils");

                if (runCommandUtilsType == null)
                {
                    // AI Assistant package not installed, nothing to do
                    return;
                }

                // Get the Builder property (public static, but on an internal class)
                var builderProp = runCommandUtilsType.GetProperty("Builder",
                    BindingFlags.Public | BindingFlags.Static);
                if (builderProp == null) return;

                var builder = builderProp.GetValue(null);
                if (builder == null) return;

                // Get the AddReferences method
                var addRefsMethod = builder.GetType().GetMethod("AddReferences",
                    BindingFlags.Public | BindingFlags.Instance);
                if (addRefsMethod == null) return;

                // Collect assembly paths from Packages/ that aren't already covered
                var packageAssemblyPaths = GetPackageAssemblyPaths();
                if (packageAssemblyPaths.Count == 0) return;

                addRefsMethod.Invoke(builder, new object[] { packageAssemblyPaths });
                Debug.Log($"[McpAssemblyInjector] Injected {packageAssemblyPaths.Count} package assembly references into RunCommand compiler.");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[McpAssemblyInjector] Failed to inject assemblies: {e.Message}");
            }
        }

        static List<string> GetPackageAssemblyPaths()
        {
            var curatedPrefixes = new[] { "Assembly-CSharp", "UnityEngine", "UnityEditor", "Unity.", "netstandard" };

            // Build a lookup of all compilation pipeline assemblies by name
            var allAssemblies = new Dictionary<string, UnityEditor.Compilation.Assembly>();
            foreach (var asmType in new[] { AssembliesType.PlayerWithoutTestAssemblies, AssembliesType.Editor })
            {
                foreach (var asm in CompilationPipeline.GetAssemblies(asmType))
                    allAssemblies[asm.name] = asm;
            }

            // Find seed assemblies: those with asmdefs in Assets/ (already referenced by the compiler)
            var seeds = new HashSet<string>();
            foreach (var kvp in allAssemblies)
            {
                var asmdefPath = CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(kvp.Key);
                if (!string.IsNullOrEmpty(asmdefPath) && asmdefPath.StartsWith("Assets/"))
                    seeds.Add(kvp.Key);
            }

            // Walk dependency graph from seeds, collecting package assemblies that aren't already covered
            var toInject = new HashSet<string>();
            var visited = new HashSet<string>();
            var queue = new Queue<string>(seeds);

            while (queue.Count > 0)
            {
                var name = queue.Dequeue();
                if (!visited.Add(name)) continue;
                if (!allAssemblies.TryGetValue(name, out var asm)) continue;

                foreach (var dep in asm.assemblyReferences)
                {
                    if (visited.Contains(dep.name)) continue;

                    // Already covered by curated prefixes or Assets/ — skip injection but still walk its deps
                    var depAsmdef = CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(dep.name);
                    bool isAssets = !string.IsNullOrEmpty(depAsmdef) && depAsmdef.StartsWith("Assets/");
                    bool isCurated = curatedPrefixes.Any(p => dep.name.StartsWith(p));

                    if (!isAssets && !isCurated)
                        toInject.Add(dep.name);

                    queue.Enqueue(dep.name);
                }
            }

            // Resolve to DLL paths from loaded assemblies
            var paths = new List<string>();
            foreach (var name in toInject)
            {
                var loaded = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == name);

                if (loaded != null && !loaded.IsDynamic)
                {
                    try
                    {
                        var location = loaded.Location;
                        if (!string.IsNullOrEmpty(location))
                            paths.Add(location);
                    }
                    catch { /* skip assemblies without a location */ }
                }
            }

            return paths;
        }
    }
}
#endif
