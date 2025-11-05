using UnityEngine;

namespace UnityJigs.Types
{
    [ExecuteAlways]
    public class TargetFollow : MonoBehaviour
    {
        public Transform? Target;

        public UpdateTimingFlags UpdatesOn;
        [Header("Lerp Amount")]
        [Range(0, 1)] public float Lerp = 1;

        [Header("If set, will use Rigidbody Interpolation Instead")]
        public Rigidbody? MyRigidbody;
        public float Force = 1f;

        private void Reset() => MyRigidbody = GetComponent<Rigidbody>();

        private void SyncIf(UpdateTimingFlags timing)
        {
            if (!UpdatesOn.Applies(this, timing) || !Target) return;

            if (MyRigidbody)
            {
                var diff = (Target.position - MyRigidbody.position).normalized;
                MyRigidbody.AddForce(diff * Force, ForceMode.Force);
                MyRigidbody.rotation = Quaternion.Slerp(MyRigidbody.rotation, Target.rotation, Lerp);
                //MyRigidbody.position = Vector3.Lerp(MyRigidbody.position, Target.position, Lerp);
            }
            else
            {
                transform.position = Vector3.Lerp(transform.position, Target.position, Lerp);
                transform.rotation = Quaternion.Slerp(transform.rotation, Target.rotation, Lerp);
            }
        }

        private void FixedUpdate() => SyncIf(UpdateTimingFlags.FixedUpdate);
        private void LateUpdate() => SyncIf(UpdateTimingFlags.LateUpdate);
        private void Update() => SyncIf(UpdateTimingFlags.Update);
    }
}
