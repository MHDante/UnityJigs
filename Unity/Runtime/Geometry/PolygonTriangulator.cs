using System.Collections.Generic;
using UnityEngine;

namespace UnityJigs.Geometry
{
    /// <summary>
    /// Ear-clipping triangulation of a simple polygon with holes (holes are bridged into the outer loop first —
    /// Eberly, "Triangulation by Ear Clipping"). Outer loop counter-clockwise, holes clockwise (any winding is
    /// accepted and fixed). Output triangles are counter-clockwise in 2D, indexing <c>vertices</c>.
    /// </summary>
    public static class PolygonTriangulator
    {
        /// <summary>
        /// Appends the triangulation to `vertices` / `triangles` (indices offset by the vertices already present).
        /// False if it failed (self-intersecting input); nothing is appended then.
        /// </summary>
        public static bool Triangulate(IReadOnlyList<Vector2> outer, IReadOnlyList<IReadOnlyList<Vector2>> holes,
            List<Vector2> vertices, List<int> triangles)
        {
            // Merged ring of positions (bridges duplicate two vertices each).
            var ring = new List<Vector2>(outer.Count);
            for (var i = 0; i < outer.Count; i++) ring.Add(outer[i]);
            if (SignedArea(ring) < 0) ring.Reverse();

            var sorted = new List<List<Vector2>>(holes.Count);
            foreach (var h in holes)
            {
                if (h.Count < 3) continue;
                var copy = new List<Vector2>(h.Count);
                for (var i = 0; i < h.Count; i++) copy.Add(h[i]);
                if (SignedArea(copy) > 0) copy.Reverse();
                sorted.Add(copy);
            }
            sorted.Sort((a, b) => MaxX(b).CompareTo(MaxX(a)));
            foreach (var hole in sorted)
                if (!Bridge(ring, hole)) return false;

            var baseIndex = vertices.Count;
            var tris = new List<int>(ring.Count * 3);
            if (!EarClip(ring, tris)) return false;
            vertices.AddRange(ring);
            foreach (var t in tris) triangles.Add(baseIndex + t);
            return true;
        }

        public static float SignedArea(IReadOnlyList<Vector2> poly)
        {
            var a = 0f;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                a += poly[j].x * poly[i].y - poly[i].x * poly[j].y;
            return 0.5f * a;
        }

        /// <summary>Point in polygon (even-odd), boundary excluded.</summary>
        public static bool Contains(IReadOnlyList<Vector2> poly, Vector2 p)
        {
            var inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                var a = poly[j];
                var b = poly[i];
                if ((a.y > p.y) == (b.y > p.y)) continue;
                if (p.x < a.x + (p.y - a.y) / (b.y - a.y) * (b.x - a.x)) inside = !inside;
            }
            return inside;
        }

        /// <summary>True if any two edges of the given loops properly intersect or touch (excluding shared endpoints of adjacent edges in one loop).</summary>
        public static bool AnyIntersection(IReadOnlyList<IReadOnlyList<Vector2>> loops)
        {
            var edges = new List<(Vector2 a, Vector2 b, int loop, int index, int count)>();
            for (var l = 0; l < loops.Count; l++)
            {
                var loop = loops[l];
                for (var i = 0; i < loop.Count; i++)
                    edges.Add((loop[i], loop[(i + 1) % loop.Count], l, i, loop.Count));
            }
            for (var i = 0; i < edges.Count; i++)
            {
                var e = edges[i];
                var eMinX = Mathf.Min(e.a.x, e.b.x);
                var eMaxX = Mathf.Max(e.a.x, e.b.x);
                var eMinY = Mathf.Min(e.a.y, e.b.y);
                var eMaxY = Mathf.Max(e.a.y, e.b.y);
                for (var j = i + 1; j < edges.Count; j++)
                {
                    var f = edges[j];
                    if (Mathf.Max(f.a.x, f.b.x) < eMinX || Mathf.Min(f.a.x, f.b.x) > eMaxX ||
                        Mathf.Max(f.a.y, f.b.y) < eMinY || Mathf.Min(f.a.y, f.b.y) > eMaxY) continue;
                    if (e.loop == f.loop)
                    {
                        var d = Mathf.Abs(e.index - f.index);
                        if (d == 1 || d == e.count - 1) continue; // adjacent edges share an endpoint
                    }
                    if (SegmentsTouch(e.a, e.b, f.a, f.b)) return true;
                }
            }
            return false;
        }

        private static float MaxX(List<Vector2> poly)
        {
            var m = float.MinValue;
            foreach (var p in poly) m = Mathf.Max(m, p.x);
            return m;
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        // Splices `hole` (clockwise) into `ring` (counter-clockwise) through a mutually visible vertex pair.
        private static bool Bridge(List<Vector2> ring, List<Vector2> hole)
        {
            var mi = 0;
            for (var i = 1; i < hole.Count; i++)
                if (hole[i].x > hole[mi].x || (hole[i].x == hole[mi].x && hole[i].y < hole[mi].y)) mi = i;
            var m = hole[mi];

            // Ray from M toward +x: nearest edge crossing.
            var bestT = float.MaxValue;
            var edge = -1;
            Vector2 hit = default;
            for (var i = 0; i < ring.Count; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % ring.Count];
                if ((a.y > m.y) == (b.y > m.y) && a.y != m.y && b.y != m.y) continue;
                if (a.y == b.y)
                {
                    if (a.y != m.y) continue;
                    var x = Mathf.Min(a.x, b.x);
                    if (x < m.x) continue;
                    if (x - m.x < bestT) { bestT = x - m.x; edge = i; hit = new Vector2(x, m.y); }
                    continue;
                }
                var t = (m.y - a.y) / (b.y - a.y);
                if (t < 0 || t > 1) continue;
                var ix = a.x + t * (b.x - a.x);
                if (ix < m.x) continue;
                if (ix - m.x < bestT) { bestT = ix - m.x; edge = i; hit = new Vector2(ix, m.y); }
            }
            if (edge < 0) return false;

            // Candidate P: the edge endpoint with the larger x (or the hit vertex itself).
            var ia = edge;
            var ib = (edge + 1) % ring.Count;
            int pi;
            if ((ring[ia] - hit).sqrMagnitude < 1e-12f) pi = ia;
            else if ((ring[ib] - hit).sqrMagnitude < 1e-12f) pi = ib;
            else
            {
                pi = ring[ia].x > ring[ib].x ? ia : ib;
                // Reflex vertices strictly inside triangle (M, hit, P) block P; take the one with the smallest angle to the ray.
                var p = ring[pi];
                var bestAngle = float.MaxValue;
                var bestDist = float.MaxValue;
                var blocked = -1;
                for (var i = 0; i < ring.Count; i++)
                {
                    if (i == pi) continue;
                    var v = ring[i];
                    if (!IsReflex(ring, i)) continue;
                    if (!InTriangle(v, m, hit, p)) continue;
                    var dv = v - m;
                    var angle = Mathf.Abs(Mathf.Atan2(dv.y, dv.x));
                    var dist = dv.sqrMagnitude;
                    if (angle < bestAngle - 1e-7f || (Mathf.Abs(angle - bestAngle) <= 1e-7f && dist < bestDist))
                    {
                        bestAngle = angle;
                        bestDist = dist;
                        blocked = i;
                    }
                }
                if (blocked >= 0) pi = blocked;
            }

            // ring[..pi], M, hole from M around, M, ring[pi], ring[pi+1..]
            var splice = new List<Vector2>(hole.Count + 2);
            for (var k = 0; k <= hole.Count; k++) splice.Add(hole[(mi + k) % hole.Count]);
            splice.Add(ring[pi]);
            ring.InsertRange(pi + 1, splice);
            return true;
        }

        private static bool IsReflex(List<Vector2> ring, int i)
        {
            var n = ring.Count;
            var p = ring[(i + n - 1) % n];
            var c = ring[i];
            var q = ring[(i + 1) % n];
            return Cross(c - p, q - c) <= 0;
        }

        private static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            var d1 = Cross(b - a, p - a);
            var d2 = Cross(c - b, p - b);
            var d3 = Cross(a - c, p - c);
            var neg = d1 < 0 || d2 < 0 || d3 < 0;
            var pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        private static bool EarClip(List<Vector2> ring, List<int> tris)
        {
            var n = ring.Count;
            if (n < 3) return false;
            var prev = new int[n];
            var next = new int[n];
            for (var i = 0; i < n; i++)
            {
                prev[i] = (i + n - 1) % n;
                next[i] = (i + 1) % n;
            }
            var remaining = n;
            var cur = 0;
            var sinceEar = 0;
            while (remaining > 3)
            {
                var p = prev[cur];
                var q = next[cur];
                var a = ring[p];
                var b = ring[cur];
                var c = ring[q];
                var cross = Cross(b - a, c - b);
                var isEar = false;
                var degenerate = false;
                if (Mathf.Abs(cross) <= 1e-9f)
                    degenerate = Vector2.Dot(b - a, c - b) >= 0 || (a - c).sqrMagnitude < 1e-12f; // straight run or zero spike
                else if (cross > 0)
                {
                    isEar = true;
                    for (var k = next[q]; k != p; k = next[k])
                    {
                        var v = ring[k];
                        if (v == a || v == b || v == c) continue; // bridge duplicates
                        if (!InTriangle(v, a, b, c)) continue;
                        isEar = false;
                        break;
                    }
                }
                if (isEar || degenerate)
                {
                    if (isEar)
                    {
                        tris.Add(p);
                        tris.Add(cur);
                        tris.Add(q);
                    }
                    next[p] = q;
                    prev[q] = p;
                    remaining--;
                    cur = p;
                    sinceEar = 0;
                    continue;
                }
                cur = q;
                if (++sinceEar > remaining) return false;
            }
            var last = cur;
            var l1 = next[last];
            var l2 = next[l1];
            if (Cross(ring[l1] - ring[last], ring[l2] - ring[l1]) > 1e-9f)
            {
                tris.Add(last);
                tris.Add(l1);
                tris.Add(l2);
            }
            return true;
        }

        private static bool SegmentsTouch(Vector2 p1, Vector2 p2, Vector2 q1, Vector2 q2)
        {
            var d1 = Cross(q2 - q1, p1 - q1);
            var d2 = Cross(q2 - q1, p2 - q1);
            var d3 = Cross(p2 - p1, q1 - p1);
            var d4 = Cross(p2 - p1, q2 - p1);
            if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0))) return true;
            return (d1 == 0 && OnSegment(q1, q2, p1)) || (d2 == 0 && OnSegment(q1, q2, p2)) ||
                   (d3 == 0 && OnSegment(p1, p2, q1)) || (d4 == 0 && OnSegment(p1, p2, q2));
        }

        private static bool OnSegment(Vector2 a, Vector2 b, Vector2 p) =>
            p.x >= Mathf.Min(a.x, b.x) && p.x <= Mathf.Max(a.x, b.x) &&
            p.y >= Mathf.Min(a.y, b.y) && p.y <= Mathf.Max(a.y, b.y);
    }
}
