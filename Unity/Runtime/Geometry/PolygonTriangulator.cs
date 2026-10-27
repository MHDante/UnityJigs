using System;
using System.Collections.Generic;
using UnityEngine;

// The triangulation is a C# port of earcut 3.0 (https://github.com/mapbox/earcut):
//
// ISC License
// Copyright (c) 2016, Mapbox
// Permission to use, copy, modify, and/or distribute this software for any purpose with or without fee is hereby granted,
// provided that the above copyright notice and this permission notice appear in all copies.
// THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES WITH REGARD TO THIS SOFTWARE INCLUDING ALL
// IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY SPECIAL, DIRECT,
// INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN
// ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF OR IN CONNECTION WITH THE USE OR PERFORMANCE OF
// THIS SOFTWARE.

namespace UnityJigs.Geometry
{
    /// <summary>
    /// Triangulation of a simple polygon with holes — earcut: holes are bridged into the outer loop left to right, then
    /// ears are clipped (z-order hashed on big rings); when a whole lap finds no ear it drops duplicate and collinear
    /// points, then cures local self-intersections, then splits the ring along a valid diagonal. Copes with collinear
    /// runs, duplicate points, holes touching each other or the outer loop at a vertex, and thin slivers. Any winding is
    /// accepted. Output triangles are counter-clockwise in 2D, indexing <c>vertices</c>; the output depends on the input
    /// alone (deterministic).
    /// </summary>
    public static class PolygonTriangulator
    {
        /// <summary>
        /// Appends the triangulation to `vertices` / `triangles` (indices offset by the vertices already present): the
        /// input points the triangles use, in input order (outer, then holes), and no zero-area triangle. False if it
        /// failed — the triangles don't cover the outer minus its holes (self-intersecting input, a hole outside the
        /// outer); nothing is appended then.
        /// </summary>
        public static bool Triangulate(IReadOnlyList<Vector2> outer, IReadOnlyList<IReadOnlyList<Vector2>> holes,
            List<Vector2> vertices, List<int> triangles)
        {
            if (outer.Count < 3) return false;
            var points = new List<Vector2>(outer.Count);
            for (var i = 0; i < outer.Count; i++) points.Add(outer[i]);
            var holeStarts = new List<int>(holes.Count);
            var want = Math.Abs(Area2(points, 0, points.Count));
            foreach (var h in holes)
            {
                if (h.Count < 3) continue;
                var start = points.Count;
                holeStarts.Add(start);
                for (var i = 0; i < h.Count; i++) points.Add(h[i]);
                want -= Math.Abs(Area2(points, start, points.Count));
            }

            var tris = new List<int>(points.Count * 3);
            Earcut(points, holeStarts, tris);

            // Counter-clockwise, zero-area dropped; earcut leaves out what it can't triangulate, so the covered area is
            // the success test.
            var got = 0.0;
            var used = new bool[points.Count];
            var kept = new List<int>(tris.Count);
            for (var t = 0; t < tris.Count; t += 3)
            {
                int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                var cross = Cross2(points[a], points[b], points[c]);
                if (cross == 0) continue;
                if (cross < 0) (b, c) = (c, b);
                got += Math.Abs(cross);
                kept.Add(a);
                kept.Add(b);
                kept.Add(c);
                used[a] = used[b] = used[c] = true;
            }
            if (Math.Abs(got - want) > 1e-6 * want + 1e-9) return false;

            var remap = new int[points.Count];
            for (var i = 0; i < points.Count; i++)
            {
                if (!used[i]) continue;
                remap[i] = vertices.Count;
                vertices.Add(points[i]);
            }
            foreach (var i in kept) triangles.Add(remap[i]);
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

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        private static double Cross2(Vector2 a, Vector2 b, Vector2 c) =>
            ((double)b.x - a.x) * ((double)c.y - a.y) - ((double)b.y - a.y) * ((double)c.x - a.x);

        // Twice the signed area of points[start..end) as a closed loop, positive counter-clockwise.
        private static double Area2(List<Vector2> points, int start, int end)
        {
            var sum = 0.0;
            for (int i = start, j = end - 1; i < end; j = i++)
                sum += (double)points[j].x * points[i].y - (double)points[i].x * points[j].y;
            return sum;
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

        // ---- earcut ----------------------------------------------------------------------------------------------------
        // A ring is a circular doubly linked list of nodes; a bridge duplicates two nodes (same point index I). Area() is
        // earcut's: NEGATIVE for a counter-clockwise turn, so the outer ring runs counter-clockwise and its ears are the
        // nodes with Area < 0.

        private sealed class Node
        {
            public int I; // the point's index
            public double X, Y;
            public Node Prev = null!, Next = null!;
            public int Z; // z-order of the point (hashed rings)
            public Node? PrevZ, NextZ; // the z-order list
            public bool Steiner; // a one-point hole: never filtered out
        }

        // Rings of more points than this are z-order hashed (earcut's threshold).
        private const int HashThreshold = 80;

        // Triangles (point indices) of the outer loop points[0..holeStarts[0]) minus the holes that start at holeStarts.
        private static void Earcut(List<Vector2> points, List<int> holeStarts, List<int> tris)
        {
            var outerEnd = holeStarts.Count > 0 ? holeStarts[0] : points.Count;
            var outer = LinkedList(points, 0, outerEnd, true);
            if (outer == null || outer.Next == outer.Prev) return;
            if (holeStarts.Count > 0) outer = EliminateHoles(points, holeStarts, outer);
            double minX = 0, minY = 0, invSize = 0;
            if (points.Count > HashThreshold)
            {
                minX = points[0].x;
                minY = points[0].y;
                double maxX = minX, maxY = minY;
                for (var i = 1; i < outerEnd; i++)
                {
                    double x = points[i].x, y = points[i].y;
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
                // z-order coordinates are 15 bits.
                invSize = Math.Max(maxX - minX, maxY - minY);
                invSize = invSize != 0 ? 32767 / invSize : 0;
            }
            EarcutLinked(outer, tris, minX, minY, invSize, 0);
        }

        // The ring of points[start..end) wound counter-clockwise (`ccw`) or clockwise; a closing duplicate is dropped.
        private static Node? LinkedList(List<Vector2> points, int start, int end, bool ccw)
        {
            Node? last = null;
            if (ccw == Area2(points, start, end) > 0)
                for (var i = start; i < end; i++) last = InsertNode(i, points[i], last);
            else
                for (var i = end - 1; i >= start; i--) last = InsertNode(i, points[i], last);
            if (last != null && SamePosition(last, last.Next))
            {
                RemoveNode(last);
                last = last.Next;
            }
            return last;
        }

        // Removes duplicate and collinear points from start to end (the whole ring by default).
        private static Node FilterPoints(Node start, Node? end = null)
        {
            end ??= start;
            var p = start;
            bool again;
            do
            {
                again = false;
                if (!p.Steiner && (SamePosition(p, p.Next) || Area(p.Prev, p, p.Next) == 0))
                {
                    RemoveNode(p);
                    p = end = p.Prev;
                    if (p == p.Next) break;
                    again = true;
                }
                else p = p.Next;
            } while (again || p != end);
            return end;
        }

        // Clips the ring's ears. A lap with no ear: pass 0 → filter degenerate points and retry, pass 1 → cure local
        // self-intersections and retry, pass 2 → split the ring in two and start each half over.
        private static void EarcutLinked(Node? ear, List<int> tris, double minX, double minY, double invSize, int pass)
        {
            if (ear == null) return;
            if (pass == 0 && invSize != 0) IndexCurve(ear, minX, minY, invSize);
            var stop = ear;
            while (ear.Prev != ear.Next)
            {
                var prev = ear.Prev;
                var next = ear.Next;
                if (invSize != 0 ? IsEarHashed(ear, minX, minY, invSize) : IsEar(ear))
                {
                    tris.Add(prev.I);
                    tris.Add(ear.I);
                    tris.Add(next.I);
                    RemoveNode(ear);
                    // Skipping the next vertex gives fewer sliver triangles.
                    ear = next.Next;
                    stop = next.Next;
                    continue;
                }
                ear = next;
                if (ear != stop) continue;
                if (pass == 0) EarcutLinked(FilterPoints(ear), tris, minX, minY, invSize, 1);
                else if (pass == 1) EarcutLinked(CureLocalIntersections(FilterPoints(ear), tris), tris, minX, minY, invSize, 2);
                else SplitEarcut(ear, tris, minX, minY, invSize);
                break;
            }
        }

        // A convex corner with no reflex point of the ring inside its triangle.
        private static bool IsEar(Node ear)
        {
            Node a = ear.Prev, b = ear, c = ear.Next;
            if (Area(a, b, c) >= 0) return false;
            double ax = a.X, bx = b.X, cx = c.X, ay = a.Y, by = b.Y, cy = c.Y;
            double x0 = Math.Min(ax, Math.Min(bx, cx)), y0 = Math.Min(ay, Math.Min(by, cy));
            double x1 = Math.Max(ax, Math.Max(bx, cx)), y1 = Math.Max(ay, Math.Max(by, cy));
            for (var p = c.Next; p != a; p = p.Next)
                if (p.X >= x0 && p.X <= x1 && p.Y >= y0 && p.Y <= y1 &&
                    PointInTriangleExceptFirst(ax, ay, bx, by, cx, cy, p.X, p.Y) && Area(p.Prev, p, p.Next) >= 0)
                    return false;
            return true;
        }

        // IsEar, looking only at the points whose z-order lies within the triangle's bounding box.
        private static bool IsEarHashed(Node ear, double minX, double minY, double invSize)
        {
            Node a = ear.Prev, b = ear, c = ear.Next;
            if (Area(a, b, c) >= 0) return false;
            double ax = a.X, bx = b.X, cx = c.X, ay = a.Y, by = b.Y, cy = c.Y;
            double x0 = Math.Min(ax, Math.Min(bx, cx)), y0 = Math.Min(ay, Math.Min(by, cy));
            double x1 = Math.Max(ax, Math.Max(bx, cx)), y1 = Math.Max(ay, Math.Max(by, cy));
            int minZ = ZOrder(x0, y0, minX, minY, invSize), maxZ = ZOrder(x1, y1, minX, minY, invSize);
            Node? p = ear.PrevZ, n = ear.NextZ;
            while (p != null && p.Z >= minZ && n != null && n.Z <= maxZ)
            {
                if (Blocks(p)) return false;
                p = p.PrevZ;
                if (Blocks(n)) return false;
                n = n.NextZ;
            }
            for (; p != null && p.Z >= minZ; p = p.PrevZ)
                if (Blocks(p)) return false;
            for (; n != null && n.Z <= maxZ; n = n.NextZ)
                if (Blocks(n)) return false;
            return true;

            bool Blocks(Node q) =>
                q.X >= x0 && q.X <= x1 && q.Y >= y0 && q.Y <= y1 && q != a && q != c &&
                PointInTriangleExceptFirst(ax, ay, bx, by, cx, cy, q.X, q.Y) && Area(q.Prev, q, q.Next) >= 0;
        }

        // Walks the ring and clips the triangle of every small self-intersection (a, p, p.next, b crossing).
        private static Node CureLocalIntersections(Node start, List<int> tris)
        {
            var p = start;
            do
            {
                Node a = p.Prev, b = p.Next.Next;
                if (!SamePosition(a, b) && Intersects(a, p, p.Next, b) && LocallyInside(a, b) && LocallyInside(b, a))
                {
                    tris.Add(a.I);
                    tris.Add(p.I);
                    tris.Add(b.I);
                    RemoveNode(p);
                    RemoveNode(p.Next);
                    p = start = b;
                }
                p = p.Next;
            } while (p != start);
            return FilterPoints(p);
        }

        // Splits the ring along the first valid diagonal and triangulates each half from scratch.
        private static void SplitEarcut(Node start, List<int> tris, double minX, double minY, double invSize)
        {
            var a = start;
            do
            {
                for (var b = a.Next.Next; b != a.Prev; b = b.Next)
                {
                    if (a.I == b.I || !IsValidDiagonal(a, b)) continue;
                    var c = SplitPolygon(a, b);
                    // Filter the collinear points around the cut.
                    a = FilterPoints(a, a.Next);
                    c = FilterPoints(c, c.Next);
                    EarcutLinked(a, tris, minX, minY, invSize, 0);
                    EarcutLinked(c, tris, minX, minY, invSize, 0);
                    return;
                }
                a = a.Next;
            } while (a != start);
        }

        // Bridges every hole into the outer ring, leftmost hole first.
        private static Node EliminateHoles(List<Vector2> points, List<int> holeStarts, Node outer)
        {
            var queue = new List<Node>(holeStarts.Count);
            for (var i = 0; i < holeStarts.Count; i++)
            {
                var end = i < holeStarts.Count - 1 ? holeStarts[i + 1] : points.Count;
                var list = LinkedList(points, holeStarts[i], end, false);
                if (list == null) continue;
                if (list == list.Next) list.Steiner = true;
                queue.Add(GetLeftmost(list));
            }
            queue.Sort(CompareXYSlope);
            foreach (var hole in queue) outer = EliminateHole(hole, outer);
            return outer;
        }

        // By x, then y; holes whose leftmost points meet go counter-clockwise, so the bridge to the outer is always the
        // point they meet at. The point index breaks what is left (List.Sort isn't stable).
        private static int CompareXYSlope(Node a, Node b)
        {
            var result = a.X.CompareTo(b.X);
            if (result != 0) return result;
            result = a.Y.CompareTo(b.Y);
            if (result != 0) return result;
            var aSlope = (a.Next.Y - a.Y) / (a.Next.X - a.X);
            var bSlope = (b.Next.Y - b.Y) / (b.Next.X - b.X);
            result = aSlope.CompareTo(bSlope);
            return result != 0 ? result : a.I.CompareTo(b.I);
        }

        private static Node EliminateHole(Node hole, Node outer)
        {
            var bridge = FindHoleBridge(hole, outer);
            if (bridge == null) return outer;
            var bridgeReverse = SplitPolygon(bridge, hole);
            // Filter the collinear points around the cuts.
            FilterPoints(bridgeReverse, bridgeReverse.Next);
            return FilterPoints(bridge, bridge.Next);
        }

        // David Eberly's algorithm: the outer point the hole's leftmost point connects to.
        private static Node? FindHoleBridge(Node hole, Node outer)
        {
            var p = outer;
            double hx = hole.X, hy = hole.Y, qx = double.NegativeInfinity;
            Node? m = null;
            // The nearest segment a ray from the hole point to the left crosses; its endpoint with the smaller x is the
            // candidate — the vertex itself when the hole touches the outer there.
            if (SamePosition(hole, p)) return p;
            do
            {
                if (SamePosition(hole, p.Next)) return p.Next;
                if (hy <= p.Y && hy >= p.Next.Y && p.Next.Y != p.Y)
                {
                    var x = p.X + (hy - p.Y) * (p.Next.X - p.X) / (p.Next.Y - p.Y);
                    if (x <= hx && x > qx)
                    {
                        qx = x;
                        m = p.X < p.Next.X ? p : p.Next;
                        if (x == hx) return m; // the hole touches the outer segment: its leftmost end
                    }
                }
                p = p.Next;
            } while (p != outer);
            if (m == null) return null;

            // Points inside the triangle (hole point, ray hit, candidate) block the candidate: the one at the smallest
            // angle to the ray connects instead.
            var stop = m;
            double mx = m.X, my = m.Y, tanMin = double.PositiveInfinity;
            p = m;
            do
            {
                if (hx >= p.X && p.X >= mx && hx != p.X &&
                    PointInTriangle(hy < my ? hx : qx, hy, mx, my, hy < my ? qx : hx, hy, p.X, p.Y))
                {
                    var tan = Math.Abs(hy - p.Y) / (hx - p.X);
                    if (LocallyInside(p, hole) &&
                        (tan < tanMin || (tan == tanMin && (p.X > m.X || (p.X == m.X && SectorContainsSector(m, p))))))
                    {
                        m = p;
                        tanMin = tan;
                    }
                }
                p = p.Next;
            } while (p != stop);
            return m;
        }

        // Whether sector p contains sector m (two bridge candidates at one point).
        private static bool SectorContainsSector(Node m, Node p) => Area(m.Prev, m, p.Prev) < 0 && Area(p.Next, m, m.Next) < 0;

        // The z-order list of the ring, sorted by z.
        private static void IndexCurve(Node start, double minX, double minY, double invSize)
        {
            var p = start;
            do
            {
                if (p.Z == 0) p.Z = ZOrder(p.X, p.Y, minX, minY, invSize);
                p.PrevZ = p.Prev;
                p.NextZ = p.Next;
                p = p.Next;
            } while (p != start);
            p.PrevZ!.NextZ = null;
            p.PrevZ = null;
            SortLinked(p);
        }

        // Simon Tatham's linked-list merge sort, on the z-order list.
        private static void SortLinked(Node? list)
        {
            int inSize = 1, numMerges;
            do
            {
                var p = list;
                list = null;
                Node? tail = null;
                numMerges = 0;
                while (p != null)
                {
                    numMerges++;
                    var q = p;
                    var pSize = 0;
                    for (var i = 0; i < inSize; i++)
                    {
                        pSize++;
                        q = q.NextZ;
                        if (q == null) break;
                    }
                    var qSize = inSize;
                    while (pSize > 0 || (qSize > 0 && q != null))
                    {
                        Node e;
                        if (pSize != 0 && (qSize == 0 || q == null || p!.Z <= q.Z))
                        {
                            e = p!;
                            p = p!.NextZ;
                            pSize--;
                        }
                        else
                        {
                            e = q!;
                            q = q!.NextZ;
                            qSize--;
                        }
                        if (tail != null) tail.NextZ = e;
                        else list = e;
                        e.PrevZ = tail;
                        tail = e;
                    }
                    p = q;
                }
                tail!.NextZ = null;
                inSize *= 2;
            } while (numMerges > 1);
        }

        // The z-order (Morton code) of a point, from its 15-bit coordinates in the ring's bounding box.
        private static int ZOrder(double px, double py, double minX, double minY, double invSize)
        {
            var x = (int)((px - minX) * invSize);
            var y = (int)((py - minY) * invSize);
            x = (x | (x << 8)) & 0x00FF00FF;
            x = (x | (x << 4)) & 0x0F0F0F0F;
            x = (x | (x << 2)) & 0x33333333;
            x = (x | (x << 1)) & 0x55555555;
            y = (y | (y << 8)) & 0x00FF00FF;
            y = (y | (y << 4)) & 0x0F0F0F0F;
            y = (y | (y << 2)) & 0x33333333;
            y = (y | (y << 1)) & 0x55555555;
            return x | (y << 1);
        }

        private static Node GetLeftmost(Node start)
        {
            Node p = start, leftmost = start;
            do
            {
                if (p.X < leftmost.X || (p.X == leftmost.X && p.Y < leftmost.Y)) leftmost = p;
                p = p.Next;
            } while (p != start);
            return leftmost;
        }

        // Inclusive of the edges.
        private static bool PointInTriangle(double ax, double ay, double bx, double by, double cx, double cy, double px,
            double py) =>
            (cx - px) * (ay - py) >= (ax - px) * (cy - py) && (ax - px) * (by - py) >= (bx - px) * (ay - py) &&
            (bx - px) * (cy - py) >= (cx - px) * (by - py);

        // A point at a itself (the other copy of a bridged vertex) doesn't count.
        private static bool PointInTriangleExceptFirst(double ax, double ay, double bx, double by, double cx, double cy,
            double px, double py) =>
            !(ax == px && ay == py) && PointInTriangle(ax, ay, bx, by, cx, cy, px, py);

        // A diagonal a–b that crosses no edge, lies inside the ring and makes no opposite-facing sectors (or joins two
        // copies of one point between two convex corners).
        private static bool IsValidDiagonal(Node a, Node b) =>
            a.Next.I != b.I && a.Prev.I != b.I && !IntersectsPolygon(a, b) &&
            ((LocallyInside(a, b) && LocallyInside(b, a) && MiddleInside(a, b) &&
              (Area(a.Prev, a, b.Prev) != 0 || Area(a, b.Prev, b) != 0)) ||
             (SamePosition(a, b) && Area(a.Prev, a, a.Next) > 0 && Area(b.Prev, b, b.Next) > 0));

        // Twice the signed area of the turn p → q → r, NEGATIVE when it turns counter-clockwise.
        private static double Area(Node p, Node q, Node r) => (q.Y - p.Y) * (r.X - q.X) - (q.X - p.X) * (r.Y - q.Y);

        private static bool SamePosition(Node a, Node b) => a.X == b.X && a.Y == b.Y;

        // Segments p1–q1 and p2–q2 intersect or touch.
        private static bool Intersects(Node p1, Node q1, Node p2, Node q2)
        {
            var o1 = Math.Sign(Area(p1, q1, p2));
            var o2 = Math.Sign(Area(p1, q1, q2));
            var o3 = Math.Sign(Area(p2, q2, p1));
            var o4 = Math.Sign(Area(p2, q2, q1));
            if (o1 != o2 && o3 != o4) return true;
            if (o1 == 0 && OnSegment(p1, p2, q1)) return true; // p1, q1, p2 collinear and p2 on p1–q1
            if (o2 == 0 && OnSegment(p1, q2, q1)) return true;
            if (o3 == 0 && OnSegment(p2, p1, q2)) return true;
            if (o4 == 0 && OnSegment(p2, q1, q2)) return true;
            return false;
        }

        // q within the bounding box of p–r (for three collinear points: q on the segment).
        private static bool OnSegment(Node p, Node q, Node r) =>
            q.X <= Math.Max(p.X, r.X) && q.X >= Math.Min(p.X, r.X) && q.Y <= Math.Max(p.Y, r.Y) && q.Y >= Math.Min(p.Y, r.Y);

        private static bool IntersectsPolygon(Node a, Node b)
        {
            var p = a;
            do
            {
                if (p.I != a.I && p.Next.I != a.I && p.I != b.I && p.Next.I != b.I && Intersects(p, p.Next, a, b)) return true;
                p = p.Next;
            } while (p != a);
            return false;
        }

        // The diagonal a–b leaves a into the ring's inside.
        private static bool LocallyInside(Node a, Node b) =>
            Area(a.Prev, a, a.Next) < 0
                ? Area(a, b, a.Next) >= 0 && Area(a, a.Prev, b) >= 0
                : Area(a, b, a.Prev) < 0 || Area(a, a.Next, b) < 0;

        // The midpoint of a–b lies inside the ring (even-odd).
        private static bool MiddleInside(Node a, Node b)
        {
            var p = a;
            var inside = false;
            double px = (a.X + b.X) / 2, py = (a.Y + b.Y) / 2;
            do
            {
                if ((p.Y > py) != (p.Next.Y > py) && p.Next.Y != p.Y &&
                    px < (p.Next.X - p.X) * (py - p.Y) / (p.Next.Y - p.Y) + p.X)
                    inside = !inside;
                p = p.Next;
            } while (p != a);
            return inside;
        }

        // Links a to b with a two-way bridge: the ring splits in two if a and b are on one ring, or two rings (a hole
        // and its outer) merge into one. Returns b's copy, on the other side of the bridge.
        private static Node SplitPolygon(Node a, Node b)
        {
            var a2 = new Node { I = a.I, X = a.X, Y = a.Y };
            var b2 = new Node { I = b.I, X = b.X, Y = b.Y };
            Node an = a.Next, bp = b.Prev;
            a.Next = b;
            b.Prev = a;
            a2.Next = an;
            an.Prev = a2;
            b2.Next = a2;
            a2.Prev = b2;
            bp.Next = b2;
            b2.Prev = bp;
            return b2;
        }

        private static Node InsertNode(int i, Vector2 point, Node? last)
        {
            var p = new Node { I = i, X = point.x, Y = point.y };
            if (last == null)
            {
                p.Prev = p;
                p.Next = p;
            }
            else
            {
                p.Next = last.Next;
                p.Prev = last;
                last.Next.Prev = p;
                last.Next = p;
            }
            return p;
        }

        private static void RemoveNode(Node p)
        {
            p.Next.Prev = p.Prev;
            p.Prev.Next = p.Next;
            if (p.PrevZ != null) p.PrevZ.NextZ = p.NextZ;
            if (p.NextZ != null) p.NextZ.PrevZ = p.PrevZ;
        }
    }
}
