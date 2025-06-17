using Sirenix.OdinInspector;
using UnityEngine;

namespace UnityJigs.Components
{
    [ExecuteAlways]
    public class InstancedMeshRenderer : MonoBehaviour
    {
        [Required] public InstancedMesh Mesh = null!;

        private void OnEnable() => InstancedMeshGroup.Instance.Register(this);
        private void OnDisable() => InstancedMeshGroup.Instance.Unregister(this);
    }
}
