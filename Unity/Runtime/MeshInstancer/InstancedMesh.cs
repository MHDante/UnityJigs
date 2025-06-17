using Sirenix.OdinInspector;
using UnityEngine;

namespace UnityJigs.Components
{
    [CreateAssetMenu(menuName = "Jigs/Instanced Mesh", fileName = "InstancedMesh", order = 0)]
    public class InstancedMesh : ScriptableObject
    {
        [Required] public Mesh Mesh = null!;
        [Required] public Material Material= null!;
    }
}
