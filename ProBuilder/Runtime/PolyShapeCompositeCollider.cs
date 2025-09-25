using System.Collections.Generic;
using UnityEngine;
using UnityEngine.ProBuilder;
using UnityEngine.Pool;
using UnityJigs.Colliders;

namespace UnityJigs.ProBuilderAdapters
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

            var rootMc = SourceShape.GetComponent<MeshCollider>();
            if (rootMc) rootMc.enabled = false;

            var stable = new List<Vector2>(pts.Count);
            stable.AddRange(pts);
            return (stable, SourceShape.extrude);
        }

        static float SignedArea(List<Vector2> poly)
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
    }
}
