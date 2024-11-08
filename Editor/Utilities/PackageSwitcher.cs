using System.IO;
using MHDante.UnityUtils.Editor.Settings;
using MHDante.UnityUtils.Types;
using Sirenix.OdinInspector;
using UnityEditor;
using FilePathAttribute = UnityEditor.FilePathAttribute;

namespace MHDante.UnityUtils.Editor.Utilities
{
    [FilePath("PackageSwitcher.asset", FilePathAttribute.Location.PreferencesFolder)]
    public class PackageSwitcherSettings : SettingsSingleton<PackageSwitcherSettings>
    {
        protected override string Title => "Preferences/Package Switcher";
        protected override SettingsScope Scope => SettingsScope.User;

        [DictionaryDrawerSettings(DisplayMode = DictionaryDisplayOptions.OneLine, KeyLabel = "Local", ValueLabel = "Remote")]
        public SerializedDict<string, string> LocalToRemotePackages = new();

        private const string ManifestPath = "Packages/manifest.json";
        [Button, HorizontalGroup, MenuItem("Utils/Package Switcher/Switch To Local")]
        public static void SwitchToLocal()
        {
            var manifestText = File.ReadAllText(ManifestPath);
            foreach (var (local, remote) in instance.LocalToRemotePackages)
            {
                manifestText = manifestText.Replace(remote, local);
            }
            File.WriteAllText(ManifestPath, manifestText);
            AssetDatabase.ImportAsset(ManifestPath);
        }

        [Button, HorizontalGroup, MenuItem("Utils/Package Switcher/Switch To Remote")]
        public static void SwitchToRemote()
        {
            var manifestText = File.ReadAllText(ManifestPath);
            foreach (var (local, remote) in instance.LocalToRemotePackages)
            {
                manifestText = manifestText.Replace(local, remote);
            }
            File.WriteAllText(ManifestPath, manifestText);
            AssetDatabase.ImportAsset(ManifestPath);
        }



    }
}