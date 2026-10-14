using System.Collections.Generic;
using UnityEngine;

namespace UnityJigs.Geometry
{
    /// <summary>Douglas–Peucker simplification of closed loops; corners only, no smoothing.</summary>
    public static class PolygonSimplify
    {
        /// <summary>
        /// Simplifies a closed loop so no dropped point lies farther than `epsilon` from the kept outline. Keeps the
        /// loop's first point and the point farthest from it as anchors. `epsilon` &lt;= 0 copies the loop.
        /// </summary>
        public static void SimplifyClosed(IReadOnlyList<Vector2> loop, float epsilon, List<Vector2> result)
        {
            result.Clear();
            var n = loop.Count;
            if (n < 4 || epsilon <= 0f)
            {
                for (var i = 0; i < n; i++) result.Add(loop[i]);
                return;
            }
            var far = 0;
            var farD = -1f;
            for (var i = 1; i < n; i++)
            {
                var d = (loop[i] - loop[0]).sqrMagnitude;
                if (d <= farD) continue;
                farD = d;
                far = i;
            }
            var keep = new bool[n];
            keep[0] = keep[far] = true;
            Recurse(loop, 0, far, epsilon, keep);
            Recurse(loop, far, n, epsilon, keep); // index n wraps to 0
            for (var i = 0; i < n; i++)
                if (keep[i]) result.Add(loop[i]);
        }

        // Marks points strictly between a and b (b may equal n = wrap to 0) whose distance exceeds epsilon.
        private static void Recurse(IReadOnlyList<Vector2> loop, int a, int b, float epsilon, bool[] keep)
        {
            var n = loop.Count;
            if (b - a < 2) return;
            var pa = loop[a];
            var pb = loop[b % n];
            var best = -1;
            var bestD = epsilon;
            for (var i = a + 1; i < b; i++)
            {
                var d = DistanceToSegment(loop[i], pa, pb);
                if (d <= bestD) continue;
                bestD = d;
                best = i;
            }
            if (best < 0) return;
            keep[best] = true;
            Recurse(loop, a, best, epsilon, keep);
            Recurse(loop, best, b, epsilon, keep);
        }

        public static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var len2 = ab.sqrMagnitude;
            if (len2 < 1e-12f) return (p - a).magnitude;
            var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
            return (p - (a + t * ab)).magnitude;
        }
    }
}
