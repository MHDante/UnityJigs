using System.Collections.Generic;
using UnityEngine;

namespace UnityJigs.Geometry
{
    /// <summary>
    /// A 2D triangulation of a polygon (holes allowed) kept CONSTRAINED Delaunay: <see cref="Reset"/> takes any
    /// triangulation (e.g. <see cref="PolygonTriangulator.TriangulateIndices"/>'s), <see cref="MakeDelaunay"/> flips it to
    /// the constrained Delaunay triangulation (Lawson), and <see cref="Insert"/> adds interior points one at a time,
    /// keeping it so. The outline — every edge used by one triangle — is a constraint: it is never flipped or split, so
    /// the polygon's boundary vertices and edges come out exactly as they went in. Triangles are counter-clockwise index
    /// triples into <see cref="Points"/>; inserted points are appended.
    /// </summary>
    public sealed class DelaunayMesh
    {
        public readonly List<Vector2> Points = new();
        public readonly List<int> Triangles = new();
        private readonly List<int> _twin = new(); // per half-edge (triangle · 3 + k, from corner k to k + 1): its twin, or −1
        private readonly Dictionary<long, int> _build = new(EdgeKeys.Comparer);
        private readonly Stack<int> _legalize = new();
        private int _last;

        public int TriangleCount => Triangles.Count / 3;

        public void Reset(List<Vector2> points, List<int> triangles)
        {
            Points.Clear();
            Points.AddRange(points);
            Triangles.Clear();
            Triangles.AddRange(triangles);
            _twin.Clear();
            _build.Clear();
            for (var h = 0; h < Triangles.Count; h++)
            {
                _twin.Add(-1);
                _build[Key(Triangles[h], Triangles[Next(h)])] = h;
            }
            for (var h = 0; h < Triangles.Count; h++)
                if (_build.TryGetValue(Key(Triangles[Next(h)], Triangles[h]), out var t))
                    _twin[h] = t;
            _last = 0;
        }

        /// <summary>Lawson flips until every inner edge is locally Delaunay. Returns the number of flips.</summary>
        public int MakeDelaunay()
        {
            for (var h = 0; h < Triangles.Count; h++)
                if (_twin[h] > h)
                    _legalize.Push(h);
            return Legalize();
        }

        /// <summary>Adds an interior point (keeping the triangulation constrained Delaunay). False — nothing added — if it
        /// lies outside the polygon, on its outline, or on an existing point.</summary>
        public bool Insert(Vector2 p)
        {
            var t = Locate(p);
            if (t < 0) return false;
            int a = Triangles[t * 3], b = Triangles[t * 3 + 1], c = Triangles[t * 3 + 2];
            Vector2 pa = Points[a], pb = Points[b], pc = Points[c];
            var area = Orient(pa, pb, pc);
            double wa = Orient(pb, pc, p), wb = Orient(pc, pa, p), wc = Orient(pa, pb, p);
            var eps = 1e-9 * area;
            var onEdge = -1; // the half-edge p lies on
            if (wc <= eps) onEdge = t * 3;
            else if (wa <= eps) onEdge = t * 3 + 1;
            else if (wb <= eps) onEdge = t * 3 + 2;
            var tooClose = 1e-10 * area;
            if ((p - pa).sqrMagnitude <= tooClose || (p - pb).sqrMagnitude <= tooClose || (p - pc).sqrMagnitude <= tooClose)
                return false;
            var v = Points.Count;
            Points.Add(p);
            if (onEdge < 0) SplitTriangle(t, v);
            else if (_twin[onEdge] < 0)
            {
                Points.RemoveAt(v); // on the outline: never split it
                return false;
            }
            else SplitEdge(onEdge, v);
            Legalize();
            return true;
        }

        private void SplitTriangle(int t, int p)
        {
            int a = Triangles[t * 3], b = Triangles[t * 3 + 1], c = Triangles[t * 3 + 2];
            int xbc = _twin[t * 3 + 1], xca = _twin[t * 3 + 2];
            var t1 = AddTriangle(b, c, p);
            var t2 = AddTriangle(c, a, p);
            Triangles[t * 3 + 2] = p; // t = (a, b, p)
            Link(t * 3 + 1, t1 * 3 + 2); // b → p | p → b
            Link(t * 3 + 2, t2 * 3 + 1); // p → a | a → p
            Link(t1 * 3 + 1, t2 * 3 + 2); // c → p | p → c
            Link(t1 * 3, xbc);
            Link(t2 * 3, xca);
            _legalize.Push(t * 3);
            _legalize.Push(t1 * 3);
            _legalize.Push(t2 * 3);
            _last = t;
        }

        private void SplitEdge(int h, int p)
        {
            var g = _twin[h];
            int t = h / 3, u = g / 3;
            int a = Triangles[h], b = Triangles[Next(h)], c = Triangles[Prev(h)], d = Triangles[Prev(g)];
            int xbc = _twin[Next(h)], xca = _twin[Prev(h)], xad = _twin[Next(g)], xdb = _twin[Prev(g)];
            // t = (p, b, c), t2 = (a, p, c), u = (p, a, d), t3 = (b, p, d)
            SetTriangle(t, p, b, c);
            SetTriangle(u, p, a, d);
            var t2 = AddTriangle(a, p, c);
            var t3 = AddTriangle(b, p, d);
            Link(t * 3, t3 * 3); // p → b | b → p
            Link(t * 3 + 1, xbc);
            Link(t * 3 + 2, t2 * 3 + 1); // c → p | p → c
            Link(t2 * 3, u * 3); // a → p | p → a
            Link(t2 * 3 + 2, xca);
            Link(u * 3 + 1, xad);
            Link(u * 3 + 2, t3 * 3 + 1); // d → p | p → d
            Link(t3 * 3 + 2, xdb);
            _legalize.Push(t * 3 + 1);
            _legalize.Push(t2 * 3 + 2);
            _legalize.Push(u * 3 + 1);
            _legalize.Push(t3 * 3 + 2);
            _last = t;
        }

        private int Legalize()
        {
            var flips = 0;
            var guard = 0;
            while (_legalize.Count > 0 && guard++ < 4000000)
            {
                var h = _legalize.Pop();
                var g = _twin[h];
                if (g < 0) continue;
                int a = Triangles[h], b = Triangles[Next(h)], c = Triangles[Prev(h)], d = Triangles[Prev(g)];
                Vector2 pa = Points[a], pb = Points[b], pc = Points[c], pd = Points[d];
                if (!InCircle(pa, pb, pc, pd)) continue;
                if (Orient(pa, pd, pc) <= 0 || Orient(pd, pb, pc) <= 0) continue; // not convex: can't flip
                int t = h / 3, u = g / 3;
                int xbc = _twin[Next(h)], xca = _twin[Prev(h)], xad = _twin[Next(g)], xdb = _twin[Prev(g)];
                // (a, b, c) + (b, a, d) → t = (c, a, d), u = (d, b, c)
                SetTriangle(t, c, a, d);
                SetTriangle(u, d, b, c);
                Link(t * 3, xca);
                Link(t * 3 + 1, xad);
                Link(t * 3 + 2, u * 3 + 2); // d → c | c → d
                Link(u * 3, xdb);
                Link(u * 3 + 1, xbc);
                _legalize.Push(t * 3);
                _legalize.Push(t * 3 + 1);
                _legalize.Push(u * 3);
                _legalize.Push(u * 3 + 1);
                flips++;
            }
            _legalize.Clear();
            return flips;
        }

        // A visibility walk from the last touched triangle; a full scan if it wanders (never expected).
        private int Locate(Vector2 p)
        {
            var n = TriangleCount;
            if (n == 0) return -1;
            var t = Mathf.Clamp(_last, 0, n - 1);
            for (var step = 0; step < n + 16; step++)
            {
                var moved = false;
                for (var j = 0; j < 3; j++)
                {
                    var k = (j + step) % 3;
                    var h = t * 3 + k;
                    if (Orient(Points[Triangles[h]], Points[Triangles[Next(h)]], p) >= 0) continue;
                    var g = _twin[h];
                    if (g < 0) return Scan(p); // the outline is in the way (a concave polygon): look everywhere
                    t = g / 3;
                    moved = true;
                    break;
                }
                if (!moved) return t;
            }
            return Scan(p);
        }

        private int Scan(Vector2 p)
        {
            var n = TriangleCount;
            for (var t = 0; t < n; t++)
            {
                Vector2 a = Points[Triangles[t * 3]], b = Points[Triangles[t * 3 + 1]], c = Points[Triangles[t * 3 + 2]];
                if (Orient(a, b, p) >= 0 && Orient(b, c, p) >= 0 && Orient(c, a, p) >= 0) return t;
            }
            return -1;
        }

        private int AddTriangle(int a, int b, int c)
        {
            var t = TriangleCount;
            Triangles.Add(a);
            Triangles.Add(b);
            Triangles.Add(c);
            _twin.Add(-1);
            _twin.Add(-1);
            _twin.Add(-1);
            return t;
        }

        private void SetTriangle(int t, int a, int b, int c)
        {
            Triangles[t * 3] = a;
            Triangles[t * 3 + 1] = b;
            Triangles[t * 3 + 2] = c;
        }

        private void Link(int h, int g)
        {
            _twin[h] = g;
            if (g >= 0) _twin[g] = h;
        }

        private static int Next(int h) => h / 3 * 3 + (h % 3 + 1) % 3;
        private static int Prev(int h) => h / 3 * 3 + (h % 3 + 2) % 3;
        private static long Key(int u, int v) => (long)u << 32 | (uint)v;

        private static double Orient(Vector2 a, Vector2 b, Vector2 c) =>
            ((double)b.x - a.x) * ((double)c.y - a.y) - ((double)b.y - a.y) * ((double)c.x - a.x);

        // d strictly inside the circumcircle of counter-clockwise (a, b, c), beyond a relative tolerance (co-circular
        // points — a lattice — never flip back and forth).
        private static bool InCircle(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            double adx = a.x - d.x, ady = a.y - d.y, bdx = b.x - d.x, bdy = b.y - d.y, cdx = c.x - d.x, cdy = c.y - d.y;
            double ad = adx * adx + ady * ady, bd = bdx * bdx + bdy * bdy, cd = cdx * cdx + cdy * cdy;
            var det = adx * (bdy * cd - bd * cdy) - ady * (bdx * cd - bd * cdx) + ad * (bdx * cdy - bdy * cdx);
            var scale = (ad + bd + cd) * (ad + bd + cd);
            return det > 1e-10 * scale;
        }
    }
}
