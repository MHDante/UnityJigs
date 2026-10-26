using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Sirenix.OdinInspector;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityJigs.Editor.Settings;
using UnityJigs.Types;
using FilePathAttribute = UnityEditor.FilePathAttribute;

namespace UnityJigs.Editor.Utilities
{
    // Flips package dependencies in Packages/manifest.json between a local clone (a `file:` ref) and their remote ref.
    // Mappings are per user (Preferences > Package Switcher) and match by MEANING, not text:
    //   - a package is identified by its name; its local clone by an absolute folder (per user — never committed);
    //   - Switch To Local writes a `file:` ref RELATIVE to this project's Packages folder (right from the main checkout
    //     and from any worktree; relative refs also pass the pre-commit hook that rejects absolute ones);
    //   - Switch To Remote recognises any `file:` ref of a mapped package, however it is spelled;
    //   - "already set" compares canonical paths (full, normalised separators, case-insensitive on Windows / macOS).
    // Old text-pair mappings (LocalToRemotePackages: "file:<path>" → remote) migrate on first use.
    [FilePath("PackageSwitcher.asset", FilePathAttribute.Location.PreferencesFolder)]
    public class PackageSwitcherSettings : SettingsSingleton<PackageSwitcherSettings>
    {
        protected override string Title => "Preferences/Package Switcher";
        protected override SettingsScope Scope => SettingsScope.User;

        [Serializable]
        public class PackageMapping
        {
            [Tooltip("The package name (package.json \"name\"), e.g. com.mhdante.unity-jigs. Empty = read from the local " +
                     "folder's package.json.")]
            public string Name = "";

            [FolderPath(AbsolutePath = true)]
            [Tooltip("The local clone: the folder holding the package.json (absolute — this setting is per user and never " +
                     "committed; the manifest gets a path relative to each project).")]
            public string LocalFolder = "";

            [Tooltip("The remote ref written by Switch To Remote, e.g. https://github.com/me/repo.git")]
            public string Remote = "";
        }

        [ListDrawerSettings(ShowFoldout = false)]
        public List<PackageMapping> Packages = new();

        // The old text-pair format ("file:<path>" → remote): migrated into Packages on first use, then cleared.
        [HideInInspector]
        public SerializedDict<string, string> LocalToRemotePackages = new();

        private const string ManifestPath = "Packages/manifest.json";

        [Button, HorizontalGroup, MenuItem("Utils/Package/Package Switch To Local")]
        public static void SwitchToLocal() => Switch(toLocal: true);

        [Button, HorizontalGroup, MenuItem("Utils/Package/Package Switch To Remote")]
        public static void SwitchToRemote() => Switch(toLocal: false);

        [Button, HorizontalGroup, MenuItem("Utils/Package/Package Status")]
        public static void LogStatus()
        {
            if (!Ready()) return;
            var manifest = File.ReadAllText(ManifestPath);
            foreach (var m in instance.Packages)
                Debug.Log($"[PackageSwitcher] {m.Name}: {Describe(m, CurrentRef(manifest, m.Name))}");
        }

        private static void Switch(bool toLocal)
        {
            if (!Ready()) return;
            var manifest = File.ReadAllText(ManifestPath);
            var original = manifest;
            foreach (var m in instance.Packages)
            {
                var current = CurrentRef(manifest, m.Name);
                if (current == null)
                {
                    Debug.LogWarning($"[PackageSwitcher] {m.Name} is not a dependency in {ManifestPath} — skipped.");
                    continue;
                }
                string target;
                if (toLocal)
                {
                    if (IsLocal(current, m)) continue;
                    var relative = RelativeLocalRef(m);
                    if (relative == null) continue;
                    target = relative;
                }
                else
                {
                    if (current == m.Remote) continue;
                    if (!current.StartsWith("file:", StringComparison.Ordinal))
                    {
                        Debug.LogWarning($"[PackageSwitcher] {m.Name} is '{current}' — neither local nor the mapped " +
                                         $"remote '{m.Remote}'; left alone.");
                        continue;
                    }
                    target = m.Remote;
                }
                manifest = SetRef(manifest, m.Name, target);
                Debug.Log($"[PackageSwitcher] {m.Name}: {current} → {target}");
            }

            if (manifest == original)
            {
                Debug.LogWarning($"[PackageSwitcher] Every mapped package is already {(toLocal ? "local" : "remote")}.");
                return;
            }
            File.WriteAllText(ManifestPath, manifest);
            Client.Resolve();
        }

        // ---- mappings ---------------------------------------------------------------------------------------------

        // Migrates the old format, fills missing names from package.json, and reports what can't be used.
        private static bool Ready()
        {
            var settings = instance;
            var changed = Migrate(settings);
            foreach (var m in settings.Packages)
                if (string.IsNullOrEmpty(m.Name) && ReadPackageName(m.LocalFolder) is { } name)
                {
                    m.Name = name;
                    changed = true;
                }
            if (changed) settings.Save(true);

            var usable = settings.Packages.Count > 0;
            foreach (var m in settings.Packages)
                if (string.IsNullOrEmpty(m.Name) || string.IsNullOrEmpty(m.LocalFolder) || string.IsNullOrEmpty(m.Remote))
                {
                    Debug.LogError($"[PackageSwitcher] A mapping is incomplete (name '{m.Name}', local '{m.LocalFolder}', " +
                                   $"remote '{m.Remote}') — fix it in Preferences > Package Switcher.");
                    usable = false;
                }
            if (usable) return true;
            if (settings.Packages.Count == 0)
                Debug.LogError("[PackageSwitcher] No package mappings configured. Set them up in " +
                               "<a href=\"Preferences/Package Switcher\">Preferences > Package Switcher</a>");
            SettingsService.OpenUserPreferences("Preferences/Package Switcher");
            return false;
        }

        private static bool Migrate(PackageSwitcherSettings settings)
        {
            if (settings.LocalToRemotePackages.Count == 0) return false;
            foreach (var (local, remote) in settings.LocalToRemotePackages)
            {
                // A legacy entry is "file:<path>" → "<remote>", or a whole manifest line on each side:
                // "\"<name>\": \"file:<path>\"" → "\"<name>\": \"<remote>\"".
                var (name, localRef) = SplitManifestLine(local);
                var path = localRef.StartsWith("file:", StringComparison.Ordinal) ? localRef.Substring(5) : localRef;
                if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || path.Contains('"'))
                {
                    Debug.LogError($"[PackageSwitcher] Can't read the old mapping '{local}': set it up again in Preferences > Package Switcher.");
                    continue;
                }
                // A relative legacy path was relative to some project's Packages folder: try this project's.
                var folder = Path.IsPathRooted(path) ? path : Path.Combine(PackagesFolder, path);
                settings.Packages.Add(new PackageMapping
                {
                    Name = ReadPackageName(folder) ?? name ?? "",
                    LocalFolder = Canonical(folder),
                    Remote = SplitManifestLine(remote).Value,
                });
            }
            Debug.Log($"[PackageSwitcher] Migrated {settings.LocalToRemotePackages.Count} old mapping(s) to " +
                      "name + local folder + remote.");
            settings.LocalToRemotePackages.Clear();
            return true;
        }

        // "\"com.x.y\": \"value\"," → (com.x.y, value); anything else → (null, the text trimmed).
        private static (string? Name, string Value) SplitManifestLine(string text)
        {
            var match = Regex.Match(text, "^\\s*\"([^\"]+)\"\\s*:\\s*\"([^\"]*)\"\\s*,?\\s*$");
            return match.Success ? (match.Groups[1].Value, match.Groups[2].Value) : (null, text.Trim());
        }

        private static string? ReadPackageName(string folder)
        {
            var file = Path.Combine(folder, "package.json");
            if (!File.Exists(file)) return null;
            var match = Regex.Match(File.ReadAllText(file), "\"name\"\\s*:\\s*\"([^\"]+)\"");
            return match.Success ? match.Groups[1].Value : null;
        }

        // ---- manifest refs ----------------------------------------------------------------------------------------

        private static string PackagesFolder => Path.GetFullPath("Packages");

        // A dependency entry: "name": "ref" (the testables list names packages without a colon, so it never matches).
        private static Regex EntryPattern(string name) =>
            new($"(\"{Regex.Escape(name)}\"\\s*:\\s*\")([^\"]*)(\")");

        private static string? CurrentRef(string manifest, string name)
        {
            var match = EntryPattern(name).Match(manifest);
            return match.Success ? match.Groups[2].Value : null;
        }

        private static string SetRef(string manifest, string name, string value) =>
            EntryPattern(name).Replace(manifest, m => m.Groups[1].Value + value + m.Groups[3].Value, 1);

        // A `file:` ref resolves against the project's Packages folder.
        private static string? ResolveFileRef(string reference) =>
            reference.StartsWith("file:", StringComparison.Ordinal)
                ? Canonical(Path.Combine(PackagesFolder, reference.Substring(5)))
                : null;

        private static bool IsLocal(string reference, PackageMapping m) =>
            ResolveFileRef(reference) is { } folder && SamePath(folder, m.LocalFolder);

        private static string? RelativeLocalRef(PackageMapping m)
        {
            if (!Directory.Exists(m.LocalFolder))
            {
                Debug.LogError($"[PackageSwitcher] {m.Name}: the local folder '{m.LocalFolder}' doesn't exist.");
                return null;
            }
            var relative = Path.GetRelativePath(PackagesFolder, Canonical(m.LocalFolder)).Replace('\\', '/');
            if (Path.IsPathRooted(relative))
            {
                Debug.LogError($"[PackageSwitcher] {m.Name}: '{m.LocalFolder}' is on another drive than this project — " +
                               "a relative file: ref is impossible (absolute refs are refused at commit).");
                return null;
            }
            return "file:" + relative;
        }

        private static string Describe(PackageMapping m, string? reference)
        {
            if (reference == null) return "not a dependency";
            if (reference == m.Remote) return $"remote ({reference})";
            if (IsLocal(reference, m)) return $"local ({reference} → {m.LocalFolder})";
            if (ResolveFileRef(reference) is { } other) return $"local, but ANOTHER folder ({reference} → {other})";
            return $"other ({reference})";
        }

        // ---- canonical paths ----------------------------------------------------------------------------------------

        private static string Canonical(string path) =>
            Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        private static bool SamePath(string a, string b) =>
            string.Equals(Canonical(a), Canonical(b),
                Application.platform == RuntimePlatform.LinuxEditor ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
    }
}
