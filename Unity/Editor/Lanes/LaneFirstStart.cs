using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Compilation;
using UnityJigs.Lanes;

namespace UnityJigs.Editor.Lanes
{
    /// <summary>Repairs what a Library copied from another checkout gets wrong (Lanes~/README.md "Seeding
    /// the Library"): the import records of some assemblies' scripts come stale, so every script of
    /// theirs resolves to no class (<c>MonoScript.GetClass()</c> null) until reimported. Assets of those
    /// classes then load as broken, and code that recreates a "missing" settings asset (FMOD) saves
    /// defaults over it. Runs by itself once per Library in a lane, after the editor has loaded; the lane
    /// script calls <see cref="Repair"/> too.</summary>
    [InitializeOnLoad]
    public static class LaneFirstStart
    {
        // Lane A never runs this, so a Library copied from it arrives without the marker.
        private const string MarkerPath = "Library/UnityJigs.LaneFirstStart";

        static LaneFirstStart()
        {
            if (Lane.Id == null || File.Exists(MarkerPath)) return;
            EditorApplication.delayCall += () =>
            {
                var fixedAssemblies = Repair();
                if (fixedAssemblies != "none")
                    UnityEngine.Debug.Log($"[Lanes] First start in lane {Lane.Id}: reimported the scripts of {fixedAssemblies}");
            };
        }

        /// <summary>Force-reimports the scripts of every assembly (3+ scripts) whose scripts all resolve to
        /// no class, and writes the marker. Returns the assemblies' names, or "none". The editor recompiles
        /// and reloads after a reimport.</summary>
        public static string Repair()
        {
            var byAssembly = new Dictionary<string, List<string>>();
            var classless = new Dictionary<string, int>();
            foreach (var guid in AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".cs")) continue;
                var assembly = CompilationPipeline.GetAssemblyNameFromScriptPath(path);
                if (assembly == null) continue;
                if (!byAssembly.TryGetValue(assembly, out var paths)) byAssembly[assembly] = paths = new List<string>();
                paths.Add(path);
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script != null && script.GetClass() == null)
                    classless[assembly] = classless.TryGetValue(assembly, out var n) ? n + 1 : 1;
            }

            var broken = new List<string>();
            foreach (var (assembly, paths) in byAssembly)
                if (paths.Count >= 3 && classless.TryGetValue(assembly, out var n) && n == paths.Count)
                    broken.Add(assembly);

            File.WriteAllText(MarkerPath, System.DateTime.Now.ToString("u"));
            if (broken.Count == 0) return "none";

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var assembly in broken)
                foreach (var path in byAssembly[assembly])
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            return string.Join(", ", broken);
        }
    }
}
