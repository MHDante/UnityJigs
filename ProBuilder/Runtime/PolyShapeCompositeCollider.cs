using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.ProBuilder;
using UnityEngine.Pool;
using UnityJigs.Types;

namespace UnityJigs.ProBuilder
{
    public class PolyShapeCompositeCollider : CompositeColliderGenerator
    {
        [SerializeField] private PolyShape? SourceShape;

        protected override (List<Vector2>, float) GetSource()
        {
            if (SourceShape == null) SourceShape = GetComponentInParent<PolyShape>();
            if (SourceShape == null || SourceShape.controlPoints == null || SourceShape.controlPoints.Count < 3)
                return (new List<Vector2>(), 0);

            using var _1 = ListPool<Vector2>.Get(out var pts);
            foreach (var p in SourceShape.controlPoints)
                pts.Add(new Vector2(p.x, p.z));

            if (pts.Count >= 3 && SignedArea(pts) < 0)
                pts.Reverse();

            if (Application.IsPlaying(this))
            {
                var rootMc = SourceShape.GetComponent<MeshCollider>();
                if (rootMc) rootMc.enabled = false;
            }

            var stable = new List<Vector2>(pts.Count);
            stable.AddRange(pts);
            return (stable, SourceShape.extrude);
        }

        private static float SignedArea(List<Vector2> poly)
        {
            var a = 0f;
            for (var i = 0; i < poly.Count; i++)
            {
                var p = poly[i];
                var q = poly[(i + 1) % poly.Count];
                a += p.x * q.y - q.x * p.y;
            }
            return 0.5f * a;
        }

        [Button]
        private void PrintPolyShapePoints()
        {
            if (SourceShape == null)
                SourceShape = GetComponentInParent<PolyShape>();
            if (SourceShape == null || SourceShape.controlPoints == null)
            {
                Debug.LogWarning("No PolyShape found.");
                return;
            }

            var msg = "PolyShape points:\n";
            for (int i = 0; i < SourceShape.controlPoints.Count; i++)
            {
                var p = SourceShape.controlPoints[i];
                msg += $"    new Vector2({p.x}f, {p.z}f),\n";
            }

            Debug.Log(msg);
        }

    }
}
