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
        public static int Refine(List<Vector2> vertices, List<int> triangles, float maxEdge, int maxVertices = 200000)
        {
            if (maxEdge <= 0f) return 0;
            var max2 = maxEdge * maxEdge;
            // Edge → the triangles using it (at most two).
            var edges = new Dictionary<long, (int a, int b)>();
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
