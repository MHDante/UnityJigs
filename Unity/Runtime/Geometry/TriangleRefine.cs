using System.Collections.Generic;
using UnityEngine;

namespace UnityJigs.Geometry
{
    /// <summary>
    /// Conforming longest-edge bisection (Rivara) of a 2D triangulation: every triangle whose longest edge is longer than
    /// `maxEdge` is split at that edge's midpoint, together with the triangle across it (which is first split along its
    /// own longest edge if that is a different one), until none is. No T-junction is ever made, and boundary edges no
    /// longer than `maxEdge` are never split — so a triangulation whose outline is already finely split keeps its outline
    /// exactly (used by <see cref="TerraceMesher"/> for sloped tops, so the slope is sampled at well-shaped triangles).
    /// Triangles are index triples into `vertices` (any winding, kept); new vertices are appended.
    /// </summary>
    public static class TriangleRefine
    {
        /// <summary>
        /// Refines a triangulation of a height field only where it needs it: a triangle is bisected while the flat triangle
        /// misses `height` by more than `tolerance` (at its inner edges' midpoints or its centre) or is steeper than `maxSlope`
        /// (rise / run; ≤ 0 = no limit). A surface the triangles already follow gets no new point at all, however long they
        /// are. Bisection is along the longest INNER edge, the neighbour first when that edge isn't its own longest (Rivara):
        /// splitting the worst edge instead makes needles, which read as steep and refine without end. Never splits the outline
        /// (no T-junction with what lies beyond) or an edge of `minEdge` or less. Conforming; new vertices are appended; stops
        /// at `maxVertices`.
        /// </summary>
        public static int RefineToHeight(List<Vector2> vertices, List<int> triangles, System.Func<Vector2, float> height,
            float tolerance, float minEdge, float maxSlope, int maxVertices, float thinLimit = 0.5f)
        {
            var heights = new List<float>(vertices.Count);
            foreach (var p in vertices) heights.Add(height(p));
            var edges = new Dictionary<long, (int a, int b)>(EdgeKeys.Comparer);
            var settled = new HashSet<long>(EdgeKeys.Comparer); // inner edges measured and found to follow the surface
            long Key(int u, int v) => u < v ? (long)u << 32 | (uint)v : (long)v << 32 | (uint)u;
            void Link(int t)
            {
                for (var k = 0; k < 3; k++)
                {
                    var key = Key(triangles[t * 3 + k], triangles[t * 3 + (k + 1) % 3]);
                    edges[key] = edges.TryGetValue(key, out var e) ? (e.a, t) : (t, -1);
                }
            }
            void Unlink(int t)
            {
                for (var k = 0; k < 3; k++)
                {
                    var key = Key(triangles[t * 3 + k], triangles[t * 3 + (k + 1) % 3]);
                    if (!edges.TryGetValue(key, out var e)) continue;
                    if (e.a == t) e = (e.b, -1);
                    else if (e.b == t) e = (e.a, -1);
                    if (e.a < 0) edges.Remove(key);
                    else edges[key] = e;
                }
            }
            var count = triangles.Count / 3;
            // An edge of three triangles (a fold in a degenerate corner) can't be split conformingly: it stays.
            var locked = new HashSet<long>(EdgeKeys.Comparer);
            for (var t = 0; t < count; t++)
            {
                for (var k = 0; k < 3; k++)
                {
                    var key = Key(triangles[t * 3 + k], triangles[t * 3 + (k + 1) % 3]);
                    if (edges.TryGetValue(key, out var e) && e.b >= 0) locked.Add(key);
                }
                Link(t);
            }
            var min2 = minEdge * minEdge;
            bool Inner(int u, int v) => !locked.Contains(Key(u, v)) && edges.TryGetValue(Key(u, v), out var e) && e.a >= 0 && e.b >= 0;

            // An inner edge of t whose midpoint is off the surface.
            bool EdgeOff(int t)
            {
                for (var k = 0; k < 3; k++)
                {
                    int u = triangles[t * 3 + k], v = triangles[t * 3 + (k + 1) % 3];
                    var key = Key(u, v);
                    if (settled.Contains(key) || !Inner(u, v)) continue;
                    if ((vertices[v] - vertices[u]).sqrMagnitude > min2 &&
                        Mathf.Abs(height((vertices[u] + vertices[v]) * 0.5f) - (heights[u] + heights[v]) * 0.5f) > tolerance)
                        return true;
                    settled.Add(key);
                }
                return false;
            }

            int LongestInner(int t)
            {
                var best = -1;
                var bestLen = min2;
                var bestKey = long.MinValue;
                for (var k = 0; k < 3; k++)
                {
                    int u = triangles[t * 3 + k], v = triangles[t * 3 + (k + 1) % 3];
                    var len = (vertices[v] - vertices[u]).sqrMagnitude;
                    var key = Key(u, v);
                    if (len < bestLen || len <= min2 || len == bestLen && key <= bestKey || !Inner(u, v)) continue;
                    bestLen = len;
                    bestKey = key;
                    best = k;
                }
                return best;
            }

            // Narrower than half the shortest edge allowed: bisecting it (its long side is usually the outline, which stays)
            // only makes thinner ones.
            bool Sliver(int t)
            {
                Vector2 pa = vertices[triangles[t * 3]], pb = vertices[triangles[t * 3 + 1]], pc = vertices[triangles[t * 3 + 2]];
                var doubled = Mathf.Abs((pb.x - pa.x) * (pc.y - pa.y) - (pb.y - pa.y) * (pc.x - pa.x));
                var longest2 = Mathf.Max((pb - pa).sqrMagnitude, Mathf.Max((pc - pb).sqrMagnitude, (pa - pc).sqrMagnitude));
                return doubled * doubled < longest2 * minEdge * minEdge * thinLimit * thinLimit;
            }

            // Would splitting t's edge (u, v) at mid leave a child far narrower than the shortest edge allowed?
            bool MakesSliver(int t, int u, int v, Vector2 mid)
            {
                var c = 0;
                for (var j = 0; j < 3; j++)
                    if (triangles[t * 3 + j] != u && triangles[t * 3 + j] != v)
                        c = triangles[t * 3 + j];
                bool Thin(Vector2 a, Vector2 b, Vector2 o)
                {
                    var doubled = Mathf.Abs((b.x - a.x) * (o.y - a.y) - (b.y - a.y) * (o.x - a.x));
                    var longest2 = Mathf.Max((b - a).sqrMagnitude, Mathf.Max((o - b).sqrMagnitude, (a - o).sqrMagnitude));
                    return doubled * doubled < longest2 * minEdge * minEdge * thinLimit * thinLimit;
                }
                return Thin(vertices[u], mid, vertices[c]) || Thin(mid, vertices[v], vertices[c]);
            }

            bool Wrong(int t)
            {
                int a = triangles[t * 3], b = triangles[t * 3 + 1], c = triangles[t * 3 + 2];
                Vector2 pa = vertices[a], pb = vertices[b], pc = vertices[c];
                var mean = (heights[a] + heights[b] + heights[c]) / 3f;
                if (Mathf.Abs(height((pa + pb + pc) / 3f) - mean) > tolerance) return true;
                if (maxSlope <= 0f) return false;
                var ab = pb - pa;
                var ac = pc - pa;
                var det = ab.x * ac.y - ab.y * ac.x;
                if (Mathf.Abs(det) < 1e-10f) return false;
                float du = heights[b] - heights[a], dv = heights[c] - heights[a];
                var gx = (du * ac.y - dv * ab.y) / det;
                var gy = (dv * ab.x - du * ac.x) / det;
                return gx * gx + gy * gy > maxSlope * maxSlope;
            }

            void SplitOne(int t, int u, int v, int m)
            {
                var k = 0;
                for (var j = 0; j < 3; j++)
                    if (Key(triangles[t * 3 + j], triangles[t * 3 + (j + 1) % 3]) == Key(u, v))
                        k = j;
                Unlink(t);
                int a = triangles[t * 3 + k], b = triangles[t * 3 + (k + 1) % 3], c = triangles[t * 3 + (k + 2) % 3];
                triangles[t * 3] = a;
                triangles[t * 3 + 1] = m;
                triangles[t * 3 + 2] = c;
                var n = triangles.Count / 3;
                triangles.Add(m);
                triangles.Add(b);
                triangles.Add(c);
                Link(t);
                Link(n);
            }

            var added = 0;
            var work = new Stack<int>();
            var path = new Stack<int>();
            var once = new HashSet<Vector2>(vertices);
            for (var t = count - 1; t >= 0; t--) work.Push(t);
            while (work.Count > 0 && vertices.Count < maxVertices)
            {
                var t = work.Pop();
                if (Sliver(t) || !EdgeOff(t) && !Wrong(t)) continue;
                path.Clear();
                path.Push(t);
                var split = false;
                while (path.Count > 0 && vertices.Count < maxVertices)
                {
                    var cur = path.Peek();
                    var k = LongestInner(cur);
                    if (k < 0)
                    {
                        path.Pop();
                        continue;
                    }
                    int u = triangles[cur * 3 + k], v = triangles[cur * 3 + (k + 1) % 3];
                    edges.TryGetValue(Key(u, v), out var pair);
                    var across = pair.a == cur ? pair.b : pair.a;
                    var ka = LongestInner(across);
                    if (ka >= 0 && Key(triangles[across * 3 + ka], triangles[across * 3 + (ka + 1) % 3]) != Key(u, v))
                    {
                        path.Push(across);
                        continue;
                    }
                    // A midpoint that is already a vertex means a fold in the input: leave it be.
                    var mid = (vertices[u] + vertices[v]) * 0.5f;
                    // The point would sit nearly on an edge of what's left (a sliver, which reads as steep), or is already a
                    // vertex (a fold in the input): this edge stays.
                    if (MakesSliver(cur, u, v, mid) || MakesSliver(across, u, v, mid) || !once.Add(mid))
                    {
                        locked.Add(Key(u, v));
                        continue;
                    }
                    var m = vertices.Count;
                    vertices.Add(mid);
                    heights.Add(height(mid));
                    added++;
                    var made = triangles.Count / 3;
                    SplitOne(cur, u, v, m);
                    SplitOne(across, u, v, m);
                    work.Push(cur);
                    work.Push(across);
                    work.Push(made);
                    work.Push(made + 1);
                    path.Pop();
                    split = true;
                }
                if (split) work.Push(t);
            }
            return added;
        }

        public static int Refine(List<Vector2> vertices, List<int> triangles, float maxEdge, int maxVertices = 200000)
        {
            if (maxEdge <= 0f) return 0;
            var max2 = maxEdge * maxEdge;
            // Edge → the triangles using it (at most two).
            var edges = new Dictionary<long, (int a, int b)>(EdgeKeys.Comparer);
            long Key(int u, int v) => u < v ? (long)u << 32 | (uint)v : (long)v << 32 | (uint)u;
            void Link(int t)
            {
                for (var k = 0; k < 3; k++)
                {
                    var key = Key(triangles[t * 3 + k], triangles[t * 3 + (k + 1) % 3]);
                    edges[key] = edges.TryGetValue(key, out var e) ? (e.a, t) : (t, -1);
                }
            }
            void Unlink(int t)
            {
                for (var k = 0; k < 3; k++)
                {
                    var key = Key(triangles[t * 3 + k], triangles[t * 3 + (k + 1) % 3]);
                    if (!edges.TryGetValue(key, out var e)) continue;
                    if (e.a == t) e = (e.b, -1);
                    else if (e.b == t) e = (e.a, -1);
                    if (e.a < 0) edges.Remove(key);
                    else edges[key] = e;
                }
            }
            var count = triangles.Count / 3;
            for (var t = 0; t < count; t++) Link(t);

            // The longest edge of triangle t: its corner index k (edge k → k+1). Ties (a lattice is full of them) are broken
            // by the edge's key, so "longer" is a strict order on edges: the propagation path only ever climbs it, and ends.
            int Longest(int t)
            {
                var best = 0;
                var bestLen = -1f;
                var bestKey = long.MinValue;
                for (var k = 0; k < 3; k++)
                {
                    int u = triangles[t * 3 + k], v = triangles[t * 3 + (k + 1) % 3];
                    var len = (vertices[v] - vertices[u]).sqrMagnitude;
                    var key = Key(u, v);
                    if (len < bestLen || len == bestLen && key <= bestKey) continue;
                    bestLen = len;
                    bestKey = key;
                    best = k;
                }
                return best;
            }
            float LongestLen(int t)
            {
                var k = Longest(t);
                return (vertices[triangles[t * 3 + (k + 1) % 3]] - vertices[triangles[t * 3 + k]]).sqrMagnitude;
            }

            // Splits triangle t at edge k (its corners k, k+1) with midpoint m: t keeps (k, m, k+2), a new one (m, k+1, k+2).
            void SplitAt(int t, int k, int m)
            {
                Unlink(t);
                int a = triangles[t * 3 + k], b = triangles[t * 3 + (k + 1) % 3], c = triangles[t * 3 + (k + 2) % 3];
                triangles[t * 3] = a;
                triangles[t * 3 + 1] = m;
                triangles[t * 3 + 2] = c;
                var n = triangles.Count / 3;
                triangles.Add(m);
                triangles.Add(b);
                triangles.Add(c);
                Link(t);
                Link(n);
            }

            // Its longest edge is too long and inside the triangulation (an outline edge is never split).
            bool Splittable(int t)
            {
                if (LongestLen(t) <= max2) return false;
                var k = Longest(t);
                edges.TryGetValue(Key(triangles[t * 3 + k], triangles[t * 3 + (k + 1) % 3]), out var e);
                return e.a >= 0 && e.b >= 0;
            }

            var added = 0;
            var guard = 0;
            var work = new Stack<int>();
            for (var t = 0; t < triangles.Count / 3; t++)
            {
                if (!Splittable(t)) continue;
                work.Push(t);
                while (work.Count > 0 && vertices.Count < maxVertices && guard++ < 4 * maxVertices)
                {
                    var cur = work.Peek();
                    var k = Longest(cur);
                    int a = triangles[cur * 3 + k], b = triangles[cur * 3 + (k + 1) % 3];
                    if ((vertices[b] - vertices[a]).sqrMagnitude <= max2)
                    {
                        work.Pop();
                        continue;
                    }
                    edges.TryGetValue(Key(a, b), out var pair);
                    var across = pair.a == cur ? pair.b : pair.a;
                    if (across < 0)
                    {
                        work.Pop(); // the outline: never split (it would leave a T-junction with what lies beyond)
                        continue;
                    }
                    if (across >= 0)
                    {
                        var ka = Longest(across);
                        int aa = triangles[across * 3 + ka], ab = triangles[across * 3 + (ka + 1) % 3];
                        if (Key(aa, ab) != Key(a, b))
                        {
                            work.Push(across); // split the neighbour along its own longest edge first
                            continue;
                        }
                    }
                    // Both share the longest edge (or it is on the boundary — never, for a split outline): bisect both.
                    var m = vertices.Count;
                    vertices.Add((vertices[a] + vertices[b]) * 0.5f);
                    added++;
                    SplitAt(cur, k, m);
                    if (across >= 0)
                    {
                        var ka = Longest(across);
                        // The shared edge in the neighbour (it may now be stored the other way round).
                        for (var j = 0; j < 3; j++)
                            if (Key(triangles[across * 3 + j], triangles[across * 3 + (j + 1) % 3]) == Key(a, b))
                                ka = j;
                        SplitAt(across, ka, m);
                    }
                }
                work.Clear();
                if (guard >= 4 * maxVertices || vertices.Count >= maxVertices) break; // a safety stop (never expected)
                if (Splittable(t)) t--; // the triangle kept its slot: look at it again
            }
            return added;
        }
    }
}
