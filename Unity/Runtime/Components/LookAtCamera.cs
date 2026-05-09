using UnityEngine;

namespace UnityJigs.Components
{
    /// <summary>
    /// Rotates this transform to face a camera every LateUpdate. Caches the camera
    /// reference (defaults to Camera.main) so we don't poll every frame.
    /// </summary>
    [ExecuteAlways]
    public class LookAtCamera : MonoBehaviour
    {
        [Tooltip("Override camera. Leave null to use Camera.main (cached on first use).")]
        public Camera? Override;

        [Tooltip("Only rotate around the Y axis. Keeps the object upright.")]
        public bool LockUpright;

        private Camera? _cachedMain;

        private void LateUpdate()
        {
            var cam = Override;
            if (cam == null)
            {
                // Re-fetch when the cache is null OR has been destroyed (Unity's
                // == operator returns true for destroyed objects).
                if (_cachedMain == null) _cachedMain = Camera.main;
                cam = _cachedMain;
            }
            if (cam == null) return;

            var dir = cam.transform.position - transform.position;
            if (LockUpright) dir.y = 0;
            if (dir.sqrMagnitude < 1e-6f) return;
            transform.rotation = Quaternion.LookRotation(dir);
        }
    }
}
