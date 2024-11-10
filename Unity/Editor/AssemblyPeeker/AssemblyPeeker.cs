using Sirenix.OdinInspector;
using UnityEditorInternal;
using UnityEngine;

namespace UnityJigs.Editor.AssemblyPeeker
{
    [CreateAssetMenu(menuName = "Assembly Peeker")]
    public class AssemblyPeeker : ScriptableObject
    {
        public AssemblyDefinitionAsset? Peeker;
        public AssemblyDefinitionAsset?[] PeekedAssemblies = { };


        [Button]
        public void Apply() => AssemblyPeekerAsmdefProcessor.Apply(this);
    }
}
