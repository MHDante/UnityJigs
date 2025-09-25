using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

namespace UnityJigs.Geometry
{
    /// <summary>
    /// Decomposes a simple concave 2D polygon into convex polygons.
    /// Works in CCW orientation. Uses ear clipping + greedy triangle merging.
    /// </summary>
    public static class ConvexDecomposition2D
    {
        /// <summary>
        /// Decomposes polygon into convex polygons and appends results to the provided container.
        /// Returns how many convex polygons were added.
        /// </summary>
        public static int Decompose(List<Vector2> polygon, List<List<Vector2>> results)
        {
            if (polygon.Count < 3) return 0;

            if (SignedArea(polygon) < 0) polygon.Reverse();

            using var _1 = ListPool<int>.Get(out var triIdx);
            EarClipTriangulate(polygon, triIdx);

            if (triIdx.Count < 3) return 0;

            using var _2 = ListPool<List<int>>.Get(out var convexIdxPolys);
            MergeTrianglesToConvexPolysIdx(polygon, triIdx, convexIdxPolys);

            var added = 0;
            foreach (var loop in convexIdxPolys)
            {
                using var _3 = ListPool<Vector2>.Get(out var poly);
                for (var i = 0; i < loop.Count; i++) poly.Add(polygon[loop[i]]);

                var copy = new List<Vector2>(poly.Count);
                copy.AddRange(poly);
                results.Add(copy);
                added++;
            }

            return added;
        }

        static void EarClipTriangulate(List<Vector2> poly, List<int> result)
        {
            result.Clear();
            var n = poly.Count;

            using var _1 = ListPool<int>.Get(out var indices);
            for (var i = 0; i < n; i++) indices.Add(i);

            var guard = 0;
            var maxGuard = n * n;

            while (indices.Count >= 3 && guard++ < maxGuard)
            {
                var clipped = false;
                for (var k = 0; k < indices.Count; k++)
                {
                    var i0 = indices[(k - 1 + indices.Count) % indices.Count];
                    var i1 = indices[k];
                    var i2 = indices[(k + 1) % indices.Count];

                    var a = poly[i0];
                    var b = poly[i1];
                    var c = poly[i2];

                    if (Cross(b - a, c - b) <= 0) continue;

                    var anyInside = false;
                    for (var m = 0; m < indices.Count; m++)
                    {
                        var im = indices[m];
                        if (im == i0 || im == i1 || im == i2) continue;
                        if (PointInTriangle(poly[im], a, b, c)) { anyInside = true; break; }
                    }
                    if (anyInside) continue;

                    result.Add(i0);
                    result.Add(i1);
                    result.Add(i2);
                    indices.RemoveAt(k);
                    clipped = true;
                    break;
                }
                if (!clipped) break;
            }
        }

        static void MergeTrianglesToConvexPolysIdx(List<Vector2> pts, List<int> tris, List<List<int>> polys)
        {
            polys.Clear();

            for (var i = 0; i < tris.Count; i += 3)
            {
                var t = new List<int> { tris[i], tris[i + 1], tris[i + 2] };
                if (SignedAreaIdx(pts, t) < 0) t.Reverse();
                polys.Add(t);
            }

            var guard = 0;
            var maxGuard = 8192;
            bool merged;

            do
            {
                merged = false;

                var edgeToPoly = new Dictionary<(int, int), List<(int polyIdx, int a, int b)>>();

                for (var p = 0; p < polys.Count; p++)
                {
                    var poly = polys[p];
                    for (var i = 0; i < poly.Count; i++)
                    {
                        var a = poly[i];
                        var b = poly[(i + 1) % poly.Count];
                        var key = a < b ? (a, b) : (b, a);
                        if (!edgeToPoly.TryGetValue(key, out var list))
                            edgeToPoly[key] = list = new List<(int, int, int)>();
                        list.Add((p, a, b));
                    }
                }

                foreach (var kv in edgeToPoly)
                {
                    var list = kv.Value;
                    if (list.Count != 2) continue;
                    var p0 = list[0].polyIdx;
                    var p1 = list[1].polyIdx;
                    if (p0 == p1) continue;

                    var mergedPoly = TryMergePolysIdx(pts, polys[p0], polys[p1]);
                    if (mergedPoly == null) continue;

                    var keep = Mathf.Min(p0, p1);
                    var remove = Mathf.Max(p0, p1);
                    polys[keep] = mergedPoly;
                    polys.RemoveAt(remove);
                    merged = true;
                    break;
                }
            } while (merged && guard++ < maxGuard);
        }

        static List<int>? TryMergePolysIdx(List<Vector2> pts, List<int> a, List<int> b)
        {
            var ai = -1; var aj = -1;
            for (var i = 0; i < a.Count; i++)
            {
                var a0 = a[i];
                var a1 = a[(i + 1) % a.Count];
                for (var j = 0; j < b.Count; j++)
                {
                    var b0 = b[j];
                    var b1 = b[(j + 1) % b.Count];
                    if (a0 == b1 && a1 == b0) { ai = i; aj = j; break; }
                }
                if (ai != -1) break;
            }
            if (ai == -1) return null;

            var merged = new List<int>(a.Count + b.Count - 2);
            for (var k = 0; k < a.Count - 1; k++)
                merged.Add(a[(ai + 1 + k) % a.Count]);
            var skip = a[(ai + 1) % a.Count];
            for (var k = 0; k < b.Count - 1; k++)
            {
                var idx = b[(aj + 1 + k) % b.Count];
                if (idx == skip) continue;
                merged.Add(idx);
            }

            if (SignedAreaIdx(pts, merged) < 0) merged.Reverse();
            if (!IsConvexPolygonIdx(pts, merged)) return null;
            if (HasSelfIntersectionsIdx(pts, merged)) return null;
            return merged;
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

        static float SignedAreaIdx(List<Vector2> pts, List<int> loop)
        {
            var a = 0f;
            for (var i = 0; i < loop.Count; i++)
            {
                var p = pts[loop[i]];
                var q = pts[loop[(i + 1) % loop.Count]];
                a += p.x * q.y - q.x * p.y;
            }
            return 0.5f * a;
        }

        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            var v0 = c - a;
            var v1 = b - a;
            var v2 = p - a;
            var den = v0.x * v1.y - v1.x * v0.y;
            if (Mathf.Abs(den) < 1e-8f) return false;
            var u = (v2.x * v1.y - v2.x * v1.y) / den;
            var v = (v0.x * v2.y - v2.x * v0.y) / den;
            return (u >= 0f) && (v >= 0f) && (u + v <= 1f);
        }

        static bool IsConvexPolygonIdx(List<Vector2> pts, List<int> loop)
        {
            var n = loop.Count;
            if (n < 3) return false;
            var sign = 0f;
            for (var i = 0; i < n; i++)
            {
                var p0 = pts[loop[i]];
                var p1 = pts[loop[(i + 1) % n]];
                var p2 = pts[loop[(i + 2) % n]];
                var z = Cross(p1 - p0, p2 - p1);
                if (Mathf.Abs(z) < 1e-8f) continue;
                if (sign == 0f) sign = Mathf.Sign(z);
                else if (Mathf.Sign(z) != sign) return false;
            }
            return true;
        }

        static bool HasSelfIntersectionsIdx(List<Vector2> pts, List<int> loop)
        {
            var n = loop.Count;
            for (var i = 0; i < n; i++)
            {
                var i1 = (i + 1) % n;
                var a1 = pts[loop[i]];
                var a2 = pts[loop[i1]];
                for (var j = i + 1; j < n; j++)
                {
                    var j1 = (j + 1) % n;
                    if (i == j || i1 == j || j1 == i) continue;
                    var b1 = pts[loop[j]];
                    var b2 = pts[loop[j1]];
                    if (SegmentsIntersect(a1, a2, b1, b2)) return true;
                }
            }
            return false;
        }

        static bool SegmentsIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            var o1 = Orient(a, b, c);
            var o2 = Orient(a, b, d);
            var o3 = Orient(c, d, a);
            var o4 = Orient(c, d, b);
            return (o1 * o2 < 0 && o3 * o4 < 0);
        }

        static float Orient(Vector2 a, Vector2 b, Vector2 c) => Cross(b - a, c - a);
    }
}
