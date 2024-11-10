using Sirenix.OdinInspector;
using UnityEditor;
using UnityJigs.Editor.Settings;
using FilePathAttribute = UnityEditor.FilePathAttribute;

namespace UnityJigs.Editor.AssemblyPeeker
{
    [FilePath("ProjectSettings/AssemblyPeeker.asset", FilePathAttribute.Location.ProjectFolder)]
    public class AssemblyPeekerSettings : SettingsSingleton<AssemblyPeekerSettings>
    {
        protected override string Title => "Project/Assembly Peeker";
        protected override SettingsScope Scope => SettingsScope.Project;
        public PeekedAssembly[] PeekedAssemblies = { };
        private void OnValidate() => AssemblyPeekerAsmdefProcessor.UpdateAllAssemblies(this);

        [Button]
        public static void UpdateAll() => AssemblyPeekerAsmdefProcessor.UpdateAllAssemblies(instance);
    }
}
