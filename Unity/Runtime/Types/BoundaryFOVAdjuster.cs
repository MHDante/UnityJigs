using Sirenix.OdinInspector;
using UnityEngine;
using UnityJigs.Attributes;

namespace UnityJigs.Types
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class BoundaryFOVAdjuster : MonoBehaviour
    {
        private static readonly Vector2 FovRange = new(0.1f, 180f);

        [Required, SerializeField] private Camera Camera = null!;
        [SerializeField, MinValue(0f), ExtentsHandle] private Vector3 Extents = Vector3.one;
        [SerializeField, Range(0f, 0.5f)] private float Margin = 0.05f;

        private void LateUpdate()
        {
            if (Camera == null || Extents == Vector3.zero)
                return;

            AdjustFOV();
        }

        private void AdjustFOV()
        {
            var corners = new Vector3[8];
            var local = transform.localToWorldMatrix;
            var camMatrix = Camera.transform.worldToLocalMatrix;

            var i = 0;
            for (var x = -1; x <= 1; x += 2)
            for (var y = -1; y <= 1; y += 2)
            for (var z = -1; z <= 1; z += 2)
                corners[i++] = local.MultiplyPoint3x4(Vector3.Scale(Extents, new Vector3(x, y, z)));

            var viewMin = new Vector2(float.MaxValue, float.MaxValue);
            var viewMax = new Vector2(float.MinValue, float.MinValue);

            foreach (var c in corners)
            {
                var v = camMatrix.MultiplyPoint3x4(c);
                if (v.z <= 0f) continue;
                var proj = new Vector2(v.x / v.z, v.y / v.z);
                viewMin = Vector2.Min(viewMin, proj);
                viewMax = Vector2.Max(viewMax, proj);
            }

            var halfVertical = Mathf.Max(Mathf.Abs(viewMin.y), Mathf.Abs(viewMax.y)) * (1f + Margin);
            var halfHorizontal = Mathf.Max(Mathf.Abs(viewMin.x), Mathf.Abs(viewMax.x)) * (1f + Margin);

            var neededVertical = Mathf.Atan(halfVertical) * Mathf.Rad2Deg * 2f;
            var neededHorizontal = Mathf.Atan(halfHorizontal) * Mathf.Rad2Deg * 2f;

            var aspect = Camera.aspect;
            var horizFov = Mathf.Atan(Mathf.Tan(neededHorizontal * Mathf.Deg2Rad * 0.5f) / aspect) * Mathf.Rad2Deg * 2f;
            var useHorizontalFOV = neededHorizontal / aspect > neededVertical;

            Camera.usePhysicalProperties = false;
            Camera.fieldOfView = Mathf.Clamp(useHorizontalFOV ? horizFov : neededVertical, FovRange.x, FovRange.y);

            if (!Application.IsPlaying(this))
                Camera.ResetProjectionMatrix();
        }

    }
}
