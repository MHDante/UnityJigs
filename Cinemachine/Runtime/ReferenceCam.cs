using Unity.Cinemachine;
using UnityEngine;

namespace UnityJigs.Cinemachine.Runtime
{
    public class ReferenceCam : CinemachineVirtualCameraBase
    {
        public CinemachineVirtualCameraBase? TargetCamera = null!;

        public override void InternalUpdateCameraState(Vector3 worldUp, float deltaTime) =>
            TargetCamera?.InternalUpdateCameraState(worldUp, deltaTime);

        public override Transform? Follow
        {
            get => TargetCamera?.Follow;
            set
            {
                if (TargetCamera) TargetCamera.Follow = value;
            }
        }

        public override CameraState State => TargetCamera ? TargetCamera.State : default;

        public override Transform? LookAt
        {
            get => TargetCamera?.LookAt;
            set
            {
                if (TargetCamera) TargetCamera.LookAt = value;
            }
        }
    }
}
