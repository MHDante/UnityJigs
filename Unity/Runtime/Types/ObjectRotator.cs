using UnityEngine;
using UnityJigs.Attributes;
using UnityJigs.Types;

namespace NorthShore
{
    [ExecuteAlways]
    public class ObjectRotator : MonoBehaviour
    {
        [DirectionHandle]
        public Vector3 Axis;
        public float Speed;
        public UpdateTimingFlags UpdatesOn;

        [Header("If set, will use Rigidbody Interpolation Instead")]
        public Rigidbody? MyRigidbody;


        private void SyncIf(UpdateTimingFlags timing)
        {
            if (!UpdatesOn.Applies(this, timing)) return;

            if (MyRigidbody) MyRigidbody.rotation = Quaternion.AngleAxis(Speed * Time.deltaTime, Axis) * MyRigidbody.rotation;
            else transform.Rotate(Axis, Speed * Time.deltaTime, Space.World);
        }

        private void FixedUpdate() => SyncIf(UpdateTimingFlags.FixedUpdate);
        private void LateUpdate() => SyncIf(UpdateTimingFlags.LateUpdate);
        private void Update() => SyncIf(UpdateTimingFlags.Update);
    }
}
