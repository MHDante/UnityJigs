using Sirenix.OdinInspector;
using UnityEngine;

namespace MHDante.UnityUtils.Components
{
    [ExecuteAlways]
    public class CameraSync : MonoBehaviour
    {
        [Required] public Camera Target = null!;
        [Required] public Camera Source = null!;
        public float NearClipOffset = 0f;

        private void Update()
        {
            if (Target.fieldOfView != Source.fieldOfView)
                Target.fieldOfView = Source.fieldOfView;

            if (Target.nearClipPlane != Source.nearClipPlane + NearClipOffset)
                Target.nearClipPlane = Source.nearClipPlane + NearClipOffset;

            if (Target.farClipPlane != Source.farClipPlane)
                Target.farClipPlane = Source.farClipPlane;
        }

        private void LateUpdate() => Update();
    }
}
