using UnityEditor;
using UnityUtils.Editor.Settings;

namespace UnityUtils.Editor.AssemblyPeeker
{
    [FilePath("ProjectSettings/AssemblyPeeker.asset", FilePathAttribute.Location.ProjectFolder)]
    public class AssemblyPeekerSettings : SettingsSingleton<AssemblyPeekerSettings>
    {
        protected override string Title => "Project/Assembly Peeker";
        protected override SettingsScope Scope => SettingsScope.Project;
        public PeekedAssembly[] PeekedAssemblies = { };
        private void OnValidate() => AssemblyPeekerAsmdefProcessor.UpdateAllAssemblies(this);
    }
}
