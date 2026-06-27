#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace UnityJigs.Assistant.Editor
{
    /// <summary>
    /// Injects package assembly references into the Unity AI Assistant's RunCommand compiler
    /// so that RunCommand scripts can access types from package assemblies.
    /// </summary>
    [InitializeOnLoad]
    static class McpAssemblyInjector
    {
        // Set once the refs have actually been injected this domain; reset by the next domain reload.
        // Guards against the two hooks below both firing in one domain (which would double-inject + double-log).
        static bool _injected;

        static McpAssemblyInjector()
        {
            // afterAssemblyReload fires at the end of every domain reload, on the main thread, independent
            // of focus — unlike delayCall, which is starved while the Editor window is unfocused (e.g.
            // headless MCP automation): that was the bug, the injected refs weren't re-applied until a click.
            // delayCall is kept only as a cold-open fallback (the initial editor load may not raise
            // afterAssemblyReload). The _injected guard makes the work run at most once per domain.
            AssemblyReloadEvents.afterAssemblyReload += InjectPackageAssemblies;
            EditorApplication.delayCall += InjectPackageAssemblies;
        }

        static void InjectPackageAssemblies()
        {
            if (_injected) return; // already done for this domain (the other hook beat us to it)
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

                // Get the Builder property. NOTE: include NonPublic — it was public static in
                // com.unity.ai.assistant 2.9 but became `internal static` in 2.13; harden the lookup
                // so accessibility flips don't silently break the injector again.
                var builderProp = runCommandUtilsType.GetProperty("Builder",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (builderProp == null)
                {
                    AssistantHackGuard.ReportMissing("McpAssemblyInjector", "RunCommandUtils.Builder");
                    return;
                }

                var builder = builderProp.GetValue(null);
                if (builder == null)
                {
                    AssistantHackGuard.ReportMissing("McpAssemblyInjector", "RunCommandUtils.Builder (resolved null)");
                    return;
                }

                // Get the AddReferences method (public as of 2.13; NonPublic-tolerant defensively)
                var addRefsMethod = builder.GetType().GetMethod("AddReferences",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (addRefsMethod == null)
                {
                    AssistantHackGuard.ReportMissing("McpAssemblyInjector", "DynamicAssemblyBuilder.AddReferences");
                    return;
                }

                // Collect assembly paths from Packages/ that aren't already covered
                var packageAssemblyPaths = GetPackageAssemblyPaths();
                if (packageAssemblyPaths.Count == 0) return;

                addRefsMethod.Invoke(builder, new object[] { packageAssemblyPaths });
                _injected = true; // success — earlier returns leave it false so a later hook can retry
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

            // Beyond the dependency walk, inject every package assembly (asmdef outside Assets/,
            // non-curated) so RunCommand can reach package editor-tooling assemblies that no Assets/
            // asmdef depends on — e.g. UnityJigs.Fmod.Editor, UnityJigs.Behaviour.Editor. Unity's own
            // packages are skipped by curatedPrefixes; Assets/Plugins packages are already seeds.
            foreach (var kvp in allAssemblies)
            {
                if (curatedPrefixes.Any(p => kvp.Key.StartsWith(p))) continue;
                var pkgAsmdef = CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(kvp.Key);
                if (string.IsNullOrEmpty(pkgAsmdef) || pkgAsmdef.StartsWith("Assets/")) continue;
                toInject.Add(kvp.Key);
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

            // The walk above only reaches asmdef-based assemblies. The precompiled
            // references the project actually compiles against — the BCL framework
            // (System.dll, System.Core.dll, netstandard, …) plus precompiled plugin DLLs —
            // have no asmdef and aren't packages, so nothing above reaches them. RunCommand
            // compiles against the same world as the game code and needs them (e.g. ISet<>
            // lives in System.dll, so HashSet<T> won't resolve without it). Mirror them
            // straight from the compilation graph rather than hand-picking types.
            var seen = new HashSet<string>(paths);
            foreach (var kvp in allAssemblies)
            foreach (var compiledRef in kvp.Value.compiledAssemblyReferences)
                if (seen.Add(compiledRef))
                    paths.Add(compiledRef);

            return paths;
        }
    }
}
#endif
