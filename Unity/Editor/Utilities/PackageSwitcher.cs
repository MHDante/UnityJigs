using System.IO;
using Sirenix.OdinInspector;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityJigs.Editor.Settings;
using UnityJigs.Types;
using FilePathAttribute = UnityEditor.FilePathAttribute;

namespace UnityJigs.Editor.Utilities
{
    [FilePath("PackageSwitcher.asset", FilePathAttribute.Location.PreferencesFolder)]
    public class PackageSwitcherSettings : SettingsSingleton<PackageSwitcherSettings>
    {
        protected override string Title => "Preferences/Package Switcher";
        protected override SettingsScope Scope => SettingsScope.User;

        [DictionaryDrawerSettings(DisplayMode = DictionaryDisplayOptions.OneLine, KeyLabel = "Local", ValueLabel = "Remote")]
        public SerializedDict<string, string> LocalToRemotePackages = new();

        private const string ManifestPath = "Packages/manifest.json";
        [Button, HorizontalGroup, MenuItem("Utils/Package/Package Switch To Local")]
        public static void SwitchToLocal() => Switch("local", (local, remote) => (remote, local));

        [Button, HorizontalGroup, MenuItem("Utils/Package/Package Switch To Remote")]
        public static void SwitchToRemote() => Switch("remote", (local, remote) => (local, remote));

        static void Switch(string target, System.Func<string, string, (string find, string replace)> selector)
        {
            if (instance.LocalToRemotePackages.Count == 0)
            {
                Debug.LogError("No package mappings configured. Set them up in " +
                    "<a href=\"Preferences/Package Switcher\">Preferences > Package Switcher</a>");
                SettingsService.OpenUserPreferences("Preferences/Package Switcher");
                return;
            }

            var manifestText = File.ReadAllText(ManifestPath);
            var original = manifestText;
            foreach (var (local, remote) in instance.LocalToRemotePackages)
            {
                var (find, replace) = selector(local, remote);
                manifestText = manifestText.Replace(find, replace);
            }

            if (manifestText == original)
            {
                Debug.LogWarning($"Packages are already set to {target}.");
                return;
            }

            File.WriteAllText(ManifestPath, manifestText);
            Client.Resolve();
        }
    }
}
