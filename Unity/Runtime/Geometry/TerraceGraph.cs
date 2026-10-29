using System.Collections.Generic;
using UnityEngine;

namespace UnityJigs.Geometry
{
    /// <summary>
    /// The borders of a LABEL grid (one int per cell, 0 = empty, x right, y up), traced once so that every border between
    /// two different labels is ONE chain shared by both sides: a polyline between two junctions (corners where three or
    /// more labels meet, or points an edit pinned), or a closed loop where it meets none. Simplified once per chain
    /// (Douglas–Peucker, ends kept), so the two regions on either side of a border always meet on the same line — no
    /// cracks or slivers between terraces. The terrace mesher's first half (<see cref="TerraceMesher"/>).
    /// <para>Labels are also the saddle ORDER: a chain walks with the HIGHER label on its left (with two labels, the set
    /// cells on the left — exactly <see cref="MaskContours"/>), and a saddle (two cells touching only at a corner) is
    /// filled towards the higher label (<see cref="BitMask.FillSaddles"/> generalised; with two labels the same cells).
    /// After that no label touches itself at a corner, so every label's loops are simple and vertex-disjoint.</para>
    /// <para>Mesh space: cell (x, y) covers [origin + (x, y)·cellSize, +cellSize] on the XZ plane (points are (x, z)).</para>
    /// </summary>
    public sealed class TerraceGraph
    {
        /// <summary>One border: `Left` on the left walking `Points` forward, `Right` on the right (0 = empty).</summary>
        public sealed class Chain
        {
            /// <summary>The traced points (mesh space): turning points only, plus both ends of an open chain.</summary>
            public readonly List<Vector2> Points = new();

            /// <summary>The simplified points (<see cref="Simplify"/>); an open chain keeps both ends.</summary>
            public readonly List<Vector2> Simple = new();

            public int Left, Right;

            /// <summary>A closed loop (meets no junction): Points[0] is not repeated at the end.</summary>
            public bool Closed;

            /// <summary>Dropped by the mesher (a loop under the minimum area): its region joins the one around it.</summary>
            public bool Dropped;

            public Vector2 Start => Points[0];
            public Vector2 End => Closed ? Points[0] : Points[Points.Count - 1];
        }

        public readonly List<Chain> Chains = new();
        public readonly int Width, Height;
        public readonly float CellSize;
        public readonly Vector2 Origin;

        /// <summary>Cells changed by the saddle fill.</summary>
        public int SaddlesFilled { get; private set; }

        private readonly int[] _labels; // saddle-filled copy
        private readonly List<IReadOnlyList<Vector2>> _cuts = new(); // regions subtracted so far (now label 0)

        private TerraceGraph(int[] labels, int width, int height, float cellSize, Vector2 origin)
        {
            _labels = labels;
            Width = width;
            Height = height;
            CellSize = cellSize;
            Origin = origin;
        }

        /// <summary>The (saddle-filled) label of cell (x, y); 0 outside the grid.</summary>
        public int LabelOf(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height ? _labels[y * Width + x] : 0;

        /// <summary>The label at a mesh-space point (0 outside the grid or inside a subtracted region). Off cell lines.</summary>
        public int LabelAt(Vector2 p)
        {
            foreach (var cut in _cuts)
                if (InsideConvex(cut, p)) return 0;
            var c = (p - Origin) / CellSize;
            return LabelOf(Mathf.FloorToInt(c.x), Mathf.FloorToInt(c.y));
        }

        // ---- Trace -----------------------------------------------------------------------------------------------------

        // Directions: 0 = +x, 1 = +y, 2 = -x, 3 = -y (counter-clockwise order: left of d is (d + 1) & 3).
        private static readonly int[] Dx = { 1, 0, -1, 0 };
        private static readonly int[] Dy = { 0, 1, 0, -1 };

        /// <summary>Traces `labels` (Width × Height, row-major, not modified).</summary>
        public static TerraceGraph Trace(int[] labels, int width, int height, float cellSize, Vector2 origin)
        {
            var copy = (int[])labels.Clone();
            var graph = new TerraceGraph(copy, width, height, cellSize, origin);
            graph.SaddlesFilled = FillSaddles(copy, width, height);
            graph.TraceChains();
            return graph;
        }

        // A 2×2 block whose one diagonal holds one label X while neither other cell does is a saddle. Every fix raises one
        // cell to a higher label, so it terminates: X joins across the corner when X is above one of the other two (that
        // one becomes X), else X is split (its first cell takes the higher of the other two). Two labels: exactly
        // BitMask.FillSaddles (the lower cell of the unset diagonal takes the set value).
        private static int FillSaddles(int[] l, int w, int h)
        {
            var total = 0;
            int changed;
            do
            {
                changed = 0;
                for (var y = 0; y + 1 < h; y++)
                for (var x = 0; x + 1 < w; x++)
                {
                    var ia = y * w + x;
                    var ib = ia + 1;
                    var ic = ia + w;
                    var id = ic + 1;
                    int a = l[ia], b = l[ib], c = l[ic], d = l[id];
                    if (a == d && b != a && c != a)
                    {
                        if (a > b) l[ib] = a;
                        else if (a > c) l[ic] = a;
                        else l[ia] = Mathf.Max(b, c);
                        changed++;
                    }
                    else if (b == c && a != b && d != b)
                    {
                        if (b > a) l[ia] = b;
                        else if (b > d) l[id] = b;
                        else l[ib] = Mathf.Max(a, d);
                        changed++;
                    }
                }
                total += changed;
            } while (changed > 0);
            return total;
        }

        private void TraceChains()
        {
            var w = Width;
            var h = Height;
            var stride = w + 1;
            // Outgoing border edges per corner vertex (directed: the higher label on the left), and each vertex's degree.
            var outEdges = new byte[stride * (h + 1)];
            var degree = new byte[stride * (h + 1)];
            for (var y = 0; y < h; y++) // vertical edges: between (x − 1, y) and (x, y), corner (x, y) → (x, y + 1)
            for (var x = 0; x <= w; x++)
            {
                var l = LabelOf(x - 1, y);
                var r = LabelOf(x, y);
                if (l == r) continue;
                var v0 = y * stride + x;
                var v1 = v0 + stride;
                if (l > r) outEdges[v0] |= 1 << 1; // +y: (x − 1, y) on the left
                else outEdges[v1] |= 1 << 3; // −y: (x, y) on the left
                degree[v0]++;
                degree[v1]++;
            }
            for (var y = 0; y <= h; y++) // horizontal edges: between (x, y − 1) and (x, y), corner (x, y) → (x + 1, y)
            for (var x = 0; x < w; x++)
            {
                var below = LabelOf(x, y - 1);
                var above = LabelOf(x, y);
                if (below == above) continue;
                var v0 = y * stride + x;
                var v1 = v0 + 1;
                if (above > below) outEdges[v0] |= 1 << 0; // +x: (x, y) on the left
                else outEdges[v1] |= 1 << 2; // −x: (x, y − 1) on the left
                degree[v0]++;
                degree[v1]++;
            }

            // Open chains: from every junction (three or more labels meet: degree ≥ 3) along each outgoing edge to the next.
            for (var start = 0; start < outEdges.Length; start++)
            {
                if (degree[start] < 3) continue;
                while (outEdges[start] != 0)
                {
                    var d0 = FirstBit(outEdges[start]);
                    var chain = NewChain(start, d0, stride);
                    chain.Points.Add(Corner(start, stride));
                    var v = start;
                    var dir = d0;
                    while (true)
                    {
                        outEdges[v] &= (byte)~(1 << dir);
                        v = Step(v, dir, stride);
                        if (degree[v] >= 3)
                        {
                            chain.Points.Add(Corner(v, stride));
                            break;
                        }
                        var next = Choose(outEdges[v], dir);
                        if (next < 0) break; // malformed — cannot happen
                        if (next != dir) chain.Points.Add(Corner(v, stride));
                        dir = next;
                    }
                    Chains.Add(chain);
                }
            }

            // Closed loops: what is left, traced exactly as MaskContours does (same start points, same order).
            for (var start = 0; start < outEdges.Length; start++)
            {
                while (outEdges[start] != 0)
                {
                    var d0 = FirstBit(outEdges[start]);
                    var chain = NewChain(start, d0, stride);
                    chain.Closed = true;
                    var loop = chain.Points;
                    var v = start;
                    var dir = d0;
                    while (true)
                    {
                        outEdges[v] &= (byte)~(1 << dir);
                        v = Step(v, dir, stride);
                        // At the start vertex the first edge counts as available: choosing it closes the loop.
                        var avail = outEdges[v];
                        if (v == start) avail |= (byte)(1 << d0);
                        var next = Choose(avail, dir);
                        if (next < 0) break;
                        if (v == start && next == d0)
                        {
                            if (dir != d0) loop.Insert(0, Corner(start, stride));
                            break;
                        }
                        if (next != dir) loop.Add(Corner(v, stride));
                        dir = next;
                    }
                    if (loop.Count >= 3) Chains.Add(chain);
                }
            }
        }

        // A chain starting along edge (v, dir): its labels are the cells left and right of that edge.
        private Chain NewChain(int v, int dir, int stride)
        {
            var x = v % stride;
            var y = v / stride;
            var (lx, ly, rx, ry) = dir switch
            {
                0 => (x, y, x, y - 1),
                1 => (x - 1, y, x, y),
                2 => (x - 1, y - 1, x - 1, y),
                _ => (x, y - 1, x - 1, y - 1),
            };
            return new Chain { Left = LabelOf(lx, ly), Right = LabelOf(rx, ry) };
        }

        private static int Step(int v, int dir, int stride) => (v / stride + Dy[dir]) * stride + v % stride + Dx[dir];

        // Mesh space, as MaskMesher maps its loops: origin + corner · cellSize.
        private Vector2 Corner(int v, int stride) => Origin + new Vector2(v % stride, v / stride) * CellSize;

        // Prefer left, then straight, then right (never back).
        private static int Choose(int avail, int dir)
        {
            var left = (dir + 1) & 3;
            if ((avail & (1 << left)) != 0) return left;
            if ((avail & (1 << dir)) != 0) return dir;
            var right = (dir + 3) & 3;
            if ((avail & (1 << right)) != 0) return right;
            return -1;
        }

        private static int FirstBit(int b)
        {
            for (var i = 0; i < 4; i++)
                if ((b & (1 << i)) != 0) return i;
            return -1;
        }

        // ---- Edit: subtracting a region --------------------------------------------------------------------------------

        /// <summary>
        /// Makes a convex region (counter-clockwise, mesh space) empty: every chain is cut where it crosses the region's
        /// boundary (the crossing points become chain ends, so the simplification keeps them), the pieces inside go, and
        /// the region's boundary pieces that run through a non-empty label become new chains (that label | empty). Keep the
        /// region's corners and edges off the cell lattice (nudge them): ties aren't resolved. True if anything changed.
        /// </summary>
        public bool Subtract(IReadOnlyList<Vector2> region)
        {
            var n = region.Count;
            var onRegion = new List<(float u, Vector2 p)>[n];
            for (var e = 0; e < n; e++) onRegion[e] = new List<(float, Vector2)>();
            var cuts = new List<List<(int seg, float t, Vector2 p)>>(Chains.Count);
            foreach (var chain in Chains)
            {
                var list = new List<(int, float, Vector2)>();
                var pts = chain.Points;
                var segs = chain.Closed ? pts.Count : pts.Count - 1;
                for (var i = 0; i < segs; i++)
                {
                    var a = pts[i];
                    var b = pts[(i + 1) % pts.Count];
                    for (var e = 0; e < n; e++)
                    {
                        var c = region[e];
                        var d = region[(e + 1) % n];
                        var r = b - a;
                        var s = d - c;
                        var den = Cross(r, s);
                        if (Mathf.Abs(den) < 1e-12f) continue; // parallel (the caller's nudges keep lattice edges off it)
                        var t = Cross(c - a, s) / den;
                        var u = Cross(c - a, r) / den;
                        if (t <= 0f || t >= 1f || u <= 0f || u >= 1f) continue;
                        var x = a + r * t;
                        list.Add((i, t, x));
                        onRegion[e].Add((u, x));
                    }
                }
                list.Sort((p, q) => p.Item1 != q.Item1 ? p.Item1.CompareTo(q.Item1) : p.Item2.CompareTo(q.Item2));
                cuts.Add(list);
            }

            var result = new List<Chain>(Chains.Count);
            var changed = false;
            for (var k = 0; k < Chains.Count; k++)
            {
                var chain = Chains[k];
                var list = cuts[k];
                if (list.Count == 0)
                {
                    // Wholly inside or wholly outside.
                    var pts = chain.Points;
                    var probe = pts.Count > 1 ? (pts[0] + pts[1]) * 0.5f : pts[0];
                    if (InsideConvex(region, probe)) changed = true;
                    else result.Add(chain);
                    continue;
                }
                changed = true;
                // The chain's points with its crossings inserted; a crossing is a cut (a new chain end).
                var seq = new List<(Vector2 p, bool cut)>();
                var at = 0;
                for (var i = 0; i < chain.Points.Count; i++)
                {
                    seq.Add((chain.Points[i], false));
                    while (at < list.Count && list[at].seg == i) seq.Add((list[at++].p, true));
                }
                if (chain.Closed)
                {
                    // Start at the first cut, and close the loop back onto it.
                    var first = seq.FindIndex(s => s.cut);
                    var rotated = new List<(Vector2, bool)>(seq.Count + 1);
                    for (var i = 0; i <= seq.Count; i++) rotated.Add(seq[(first + i) % seq.Count]);
                    seq = rotated;
                }
                // Runs between cuts (and the chain's own ends): each wholly inside or wholly outside.
                var run = new List<Vector2> { seq[0].p };
                for (var i = 1; i < seq.Count; i++)
                {
                    run.Add(seq[i].p);
                    if (!seq[i].cut && i < seq.Count - 1) continue;
                    if (!InsideConvex(region, (run[0] + run[1]) * 0.5f))
                    {
                        var piece = new Chain { Left = chain.Left, Right = chain.Right };
                        piece.Points.AddRange(run);
                        result.Add(piece);
                    }
                    run = new List<Vector2> { seq[i].p };
                }
            }
            // The region's boundary pieces through a non-empty label: that label | empty, the label on the left (the
            // region runs counter-clockwise, its inside on its left, so the piece is reversed).
            for (var e = 0; e < n; e++)
            {
                var list = onRegion[e];
                list.Sort((p, q) => p.u.CompareTo(q.u));
                var seq = new List<Vector2> { region[e] };
                foreach (var c in list) seq.Add(c.p);
                seq.Add(region[(e + 1) % n]);
                for (var i = 0; i + 1 < seq.Count; i++)
                {
                    var label = LabelAt((seq[i] + seq[i + 1]) * 0.5f);
                    if (label == 0) continue;
                    var piece = new Chain { Left = label, Right = 0 };
                    piece.Points.Add(seq[i + 1]);
                    piece.Points.Add(seq[i]);
                    result.Add(piece);
                    changed = true;
                }
            }
            Chains.Clear();
            Chains.AddRange(result);
            _cuts.Add(region);
            return changed;
        }

        // ---- Simplify --------------------------------------------------------------------------------------------------

        /// <summary>Simplifies every chain at `epsilon` (≤ 0 copies): closed loops as PolygonSimplify does, open chains
        /// end to end.</summary>
        public void Simplify(float epsilon)
        {
            foreach (var chain in Chains)
            {
                if (chain.Closed)
                {
                    PolygonSimplify.SimplifyClosed(chain.Points, epsilon, chain.Simple);
                    continue;
                }
                var pts = chain.Points;
                chain.Simple.Clear();
                if (epsilon <= 0f || pts.Count < 3)
                {
                    chain.Simple.AddRange(pts);
                    continue;
                }
                var keep = new bool[pts.Count];
                keep[0] = keep[pts.Count - 1] = true;
                Recurse(pts, 0, pts.Count - 1, epsilon, keep);
                for (var i = 0; i < pts.Count; i++)
                    if (keep[i]) chain.Simple.Add(pts[i]);
            }
        }

        private static void Recurse(List<Vector2> pts, int a, int b, float epsilon, bool[] keep)
        {
            if (b - a < 2) return;
            var pa = pts[a];
            var pb = pts[b];
            var best = -1;
            var bestD = epsilon;
            for (var i = a + 1; i < b; i++)
            {
                var d = PolygonSimplify.DistanceToSegment(pts[i], pa, pb);
                if (d <= bestD) continue;
                bestD = d;
                best = i;
            }
            if (best < 0) return;
            keep[best] = true;
            Recurse(pts, a, best, epsilon, keep);
            Recurse(pts, best, b, epsilon, keep);
        }

        // ---- Loops -----------------------------------------------------------------------------------------------------

        /// <summary>One boundary loop of a label: the label on the left (outers counter-clockwise, holes clockwise).</summary>
        public sealed class Loop
        {
            public readonly List<Vector2> Points = new();     // simplified (or raw, see Loops)
            public readonly List<int> Across = new();         // per edge i (Points[i] → Points[i + 1]): the label across
            public readonly List<Chain> Chains = new();       // the chains it runs along, in order
            public readonly List<bool> Reversed = new();      // parallel to Chains: walked backwards (the label was Right)
        }

        /// <summary>
        /// `label`'s loops (its chains, not dropped, linked end to end with the label on the left), in chain order: the
        /// simplified points (`simplified`) or the traced ones. Every chain end is exactly one other's start (exact
        /// equality: shared points are the same values).
        /// </summary>
        public List<Loop> Loops(int label, bool simplified)
        {
            var loops = new List<Loop>();
            var pieces = new List<(Chain chain, bool reversed)>();
            foreach (var chain in Chains)
            {
                if (chain.Dropped) continue;
                if (chain.Left == label) pieces.Add((chain, false));
                else if (chain.Right == label) pieces.Add((chain, true));
            }
            var byStart = new Dictionary<Vector2, int>();
            for (var i = 0; i < pieces.Count; i++)
            {
                var (c, rev) = pieces[i];
                if (c.Closed) continue;
                byStart[rev ? c.End : c.Start] = i;
            }
            var used = new bool[pieces.Count];
            for (var s = 0; s < pieces.Count; s++)
            {
                if (used[s]) continue;
                var loop = new Loop();
                var i = s;
                while (true)
                {
                    used[i] = true;
                    var (c, rev) = pieces[i];
                    var pts = simplified ? c.Simple : c.Points;
                    var other = rev ? c.Left : c.Right;
                    loop.Chains.Add(c);
                    loop.Reversed.Add(rev);
                    if (c.Closed)
                    {
                        // A closed chain is a whole loop (never reversed into another's).
                        if (!rev)
                            foreach (var p in pts) loop.Points.Add(p);
                        else
                            for (var k = pts.Count - 1; k >= 0; k--) loop.Points.Add(pts[k]);
                        foreach (var _ in pts) loop.Across.Add(other);
                        break;
                    }
                    // Every point but the last (the next piece's first).
                    if (!rev)
                        for (var k = 0; k < pts.Count - 1; k++) loop.Points.Add(pts[k]);
                    else
                        for (var k = pts.Count - 1; k > 0; k--) loop.Points.Add(pts[k]);
                    for (var k = 0; k < pts.Count - 1; k++) loop.Across.Add(other);
                    var end = rev ? c.Start : c.End;
                    if (!byStart.TryGetValue(end, out var next) || used[next]) break;
                    i = next;
                }
                loops.Add(loop);
            }
            return loops;
        }

        /// <summary>The labels that border something (ascending, 0 left out).</summary>
        public List<int> Labels()
        {
            var set = new SortedSet<int>();
            foreach (var c in Chains)
            {
                if (c.Dropped) continue;
                if (c.Left != 0) set.Add(c.Left);
                if (c.Right != 0) set.Add(c.Right);
            }
            return new List<int>(set);
        }

        // ---- Crossing check --------------------------------------------------------------------------------------------

        /// <summary>
        /// True if any two simplified segments (of the chains not dropped) properly cross or touch, except adjacent
        /// segments of one chain and segments meeting at a shared chain end (a junction or a cut) without overlapping. With
        /// closed chains only, the same answer as PolygonTriangulator.AnyIntersection over those loops.
        /// </summary>
        public bool AnySimplifiedIntersection()
        {
            var segs = new List<(Vector2 a, Vector2 b, int chain, int index, int count, bool closed)>();
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            for (var k = 0; k < Chains.Count; k++)
            {
                var c = Chains[k];
                if (c.Dropped) continue;
                var pts = c.Simple;
                var count = c.Closed ? pts.Count : pts.Count - 1;
                for (var i = 0; i < count; i++)
                {
                    var a = pts[i];
                    var b = pts[(i + 1) % pts.Count];
                    segs.Add((a, b, k, i, count, c.Closed));
                    min = Vector2.Min(min, Vector2.Min(a, b));
                    max = Vector2.Max(max, Vector2.Max(a, b));
                }
            }
            if (segs.Count < 2) return false;
            // A uniform bucket grid over the segments' bounds (about one segment per bucket).
            var side = Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(segs.Count)), 1, 1024);
            var size = Vector2.Max(max - min, new Vector2(1e-3f, 1e-3f));
            var cell = new Vector2(size.x / side, size.y / side);
            var buckets = new List<int>?[side * side];
            for (var s = 0; s < segs.Count; s++)
            {
                var (a, b, _, _, _, _) = segs[s];
                BucketRange(a, b, min, cell, side, out var x0, out var y0, out var x1, out var y1);
                for (var y = y0; y <= y1; y++)
                for (var x = x0; x <= x1; x++)
                    (buckets[y * side + x] ??= new List<int>()).Add(s);
            }
            foreach (var bucket in buckets)
            {
                if (bucket == null) continue;
                for (var i = 0; i < bucket.Count; i++)
                for (var j = i + 1; j < bucket.Count; j++)
                {
                    var e = segs[bucket[i]];
                    var f = segs[bucket[j]];
                    if (Mathf.Max(f.a.x, f.b.x) < Mathf.Min(e.a.x, e.b.x) || Mathf.Min(f.a.x, f.b.x) > Mathf.Max(e.a.x, e.b.x) ||
                        Mathf.Max(f.a.y, f.b.y) < Mathf.Min(e.a.y, e.b.y) || Mathf.Min(f.a.y, f.b.y) > Mathf.Max(e.a.y, e.b.y))
                        continue;
                    if (e.chain == f.chain)
                    {
                        var d = Mathf.Abs(e.index - f.index);
                        if (d == 1 || e.closed && d == e.count - 1) continue; // adjacent segments share an endpoint
                    }
                    if (!SegmentsTouch(e.a, e.b, f.a, f.b)) continue;
                    // Meeting at a shared end (a junction, a cut, a loop through one junction): fine unless they overlap.
                    if (SharedEnd(e.a, e.b, f.a, f.b, out var p, out var q, out var r))
                    {
                        if (Cross(q - p, r - p) == 0f && Vector2.Dot(q - p, r - p) > 0f) return true;
                        continue;
                    }
                    return true;
                }
            }
            return false;
        }

        private static void BucketRange(Vector2 a, Vector2 b, Vector2 min, Vector2 cell, int side, out int x0, out int y0,
            out int x1, out int y1)
        {
            x0 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(a.x, b.x) - min.x) / cell.x), 0, side - 1);
            x1 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Max(a.x, b.x) - min.x) / cell.x), 0, side - 1);
            y0 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(a.y, b.y) - min.y) / cell.y), 0, side - 1);
            y1 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Max(a.y, b.y) - min.y) / cell.y), 0, side - 1);
        }

        // Two segments with exactly one endpoint in common: `p` it, `q` / `r` their other ends.
        private static bool SharedEnd(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out Vector2 p, out Vector2 q, out Vector2 r)
        {
            p = q = r = default;
            if (a == c) (p, q, r) = (a, b, d);
            else if (a == d) (p, q, r) = (a, b, c);
            else if (b == c) (p, q, r) = (b, a, d);
            else if (b == d) (p, q, r) = (b, a, c);
            else return false;
            return true;
        }

        // PolygonTriangulator.AnyIntersection's test: proper crossings, or an endpoint on the other segment.
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

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        private static bool InsideConvex(IReadOnlyList<Vector2> poly, Vector2 p)
        {
            for (var i = 0; i < poly.Count; i++)
            {
                var a = poly[i];
                var b = poly[(i + 1) % poly.Count];
                if ((b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x) < 0) return false;
            }
            return true;
        }
    }
}
