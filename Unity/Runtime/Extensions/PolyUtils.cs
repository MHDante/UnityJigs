using System.Collections.Generic;
using UnityEngine;

namespace UnityJigs.Extensions
{
    public static class PolyUtils
    {

        public static float SignedArea(List<Vector2> poly)
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

        public static bool IsConvex(List<Vector2> poly)
        {
            var n = poly.Count;
            if (n < 3) return false;

            var sign = 0f;
            for (var i = 0; i < n; i++)
            {
                var p0 = poly[i];
                var p1 = poly[(i + 1) % n];
                var p2 = poly[(i + 2) % n];
                var z = (p1 - p0).Cross(p2 - p1);
                if (Mathf.Abs(z) < 1e-8f) continue; // allow collinear
                var s = Mathf.Sign(z);
                if (sign == 0f) sign = s;
                else if (s != sign) return false;
            }

            return true;
        }

    }
}
