using System;
using UnityEngine;
using UnityJigs.Extensions;

namespace UnityJigs.Types
{
    [ExecuteAlways]
    public class TargetFollow : MonoBehaviour
    {
        public Transform? Target;

        [Header("Update timing")]
        public bool FollowOnFixedUpdate;
        public bool FollowOnUpdate;
        public bool FollowOnLateUpdate;
        public bool FollowInEditor;

        [Header("Lerp Amount")]
        [Range(0,1)] public float Lerp = 1;

        [Header("If set, will use Rigidbody Interpolation Instead")]
        public Rigidbody? MyRigidbody;

        private void Reset() => MyRigidbody = GetComponent<Rigidbody>();

        private void SyncIf(bool follow)
        {
            if (!follow || !Target || (!Application.IsPlaying(this) && !FollowInEditor)) return;

            if (MyRigidbody)
            {
                MyRigidbody.rotation = Quaternion.Slerp(MyRigidbody.rotation, Target.rotation, Lerp);
                MyRigidbody.position = Vector3.Lerp(MyRigidbody.position, Target.position, Lerp);
            }
            else
            {
                transform.position = Vector3.Lerp(transform.position, Target.position, Lerp);
                transform.rotation = Quaternion.Slerp(transform.rotation, Target.rotation, Lerp);
            }
        }

        private void FixedUpdate() => SyncIf(FollowOnFixedUpdate);
        private void LateUpdate() => SyncIf(FollowOnLateUpdate);
        private void Update() => SyncIf(FollowOnUpdate || (FollowInEditor && !Application.isPlaying));
    }
}
