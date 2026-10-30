using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityJigs.Geometry
{
    /// <summary>One solid interval of a label's column: [Bottom, Top] in mesh-space y, drawn into output `Part`.</summary>
    public struct TerraceSlice
    {
        public float Bottom, Top;
        public int Part;
        public TerraceFaces Faces;
        public TerraceSliceFlags Flags;

        /// <summary>Hides a neighbouring column's walls where it covers them (unless <see cref="TerraceMesher.Options.Hides"/>
        /// decides otherwise).</summary>
        public bool HidesWalls;

        /// <summary>The caller's own data (an index into its tables, an owner…): the mesher never reads it.</summary>
        public int Tag;

        public TerraceSlice(float bottom, float top, int part, TerraceFaces faces = TerraceFaces.All, bool hidesWalls = true,
            TerraceSliceFlags flags = TerraceSliceFlags.None, int tag = 0)
        {
            Bottom = bottom;
            Top = top;
            Part = part;
            Faces = faces;
            HidesWalls = hidesWalls;
            Flags = flags;
            Tag = tag;
        }
    }

    [Flags]
    public enum TerraceFaces
    {
        None = 0,
        Top = 1,
        Bottom = 2,
        Walls = 4,
        All = Top | Bottom | Walls,
    }

    [Flags]
    public enum TerraceSliceFlags
    {
        None = 0,
        /// <summary>The slice's top moves by the column's <see cref="TerraceMesher.Options.Lift"/> (a chamfer).</summary>
        LiftTop = 1,
        /// <summary>The slice's bottom moves by the lift too (a slab that keeps its thickness on a chamfer).</summary>
        LiftBottom = 2,
        /// <summary>A SKIRT: walls only, from the top of the column across (when it is not empty and lower) up to this
        /// slice's Top — closes the gap under a column that floats beside a lower one. Bottom is ignored.</summary>
        Skirt = 4,
        /// <summary>Not part of the column's surface (a block standing on it): skirts across measure to below it.</summary>
        NotSurface = 8,
    }

    /// <summary>
    /// The terrace mesher: a label grid (one int per cell, 0 = empty) where each label stands for a COLUMN — a few solid
    /// slices stacked in y — turned into faceted, flat-topped, vertical-sided meshes. The grid is traced ONCE
    /// (<see cref="TerraceGraph"/>), so the border between two labels is one chain, simplified once (Douglas–Peucker at
    /// `roughness`, halved until nothing crosses — as <see cref="MaskMesher"/>) and shared by both sides: terraces meet with
    /// no crack, sliver or coincident face. Per label, each slice gets a top and a bottom over the label's region
    /// (earcut), and along each border a wall covering where the slice is solid and the column across is not, built once,
    /// by the solid side, facing out. Faces have split normals; uv0 = (x, z). Loops under `minLoopAreaCells` cells of
    /// area that border one other label only are dropped (that label takes the area).
    /// <para>A grid with one non-empty label whose column is one slice [−thickness, 0] gives exactly
    /// <see cref="MaskMesher"/>'s slab (same vertices, same order).</para>
    /// <para>Options add: per-border simplification scale and long-edge splitting, a wall-hiding rule, skirts, a per-vertex
    /// LIFT (sloped tops — chamfers: the top's height then varies across a region, walls follow it at each end, normals
    /// are the faces'), and per-vertex extra data (uv1 = position + offset, uv2 from a callback) on chosen parts.</para>
    /// </summary>
    public static class TerraceMesher
    {
        /// <summary>An edit of the traced graph before it is simplified (e.g. <see cref="TerraceGraph.Subtract"/>).</summary>
        public delegate void GraphEdit(TerraceGraph graph);

        public enum FaceKinds
        {
            Top,
            Bottom,
            Wall,
        }

        /// <summary>What a vertex is, for <see cref="Options.Uv2"/>.</summary>
        public struct VertexContext
        {
            public FaceKinds Face;
            public int Label;
            public TerraceSlice Slice;
            /// <summary>Walls: the label across the edge; else 0.</summary>
            public int Across;
            /// <summary>The vertex (mesh space).</summary>
            public Vector3 Position;
            /// <summary>Walls: the slice's top at this vertex (lift included).</summary>
            public float SliceTop;
            /// <summary>Walls: the edge's ends (x, z), from → to.</summary>
            public Vector2 EdgeFrom, EdgeTo;
        }

        public sealed class Options
        {
            public float Roughness;
            public float MinLoopAreaCells = 1.5f;
            public GraphEdit? Edit;

            /// <summary>Per border (left label, right label): its share of the roughness (1 = all of it).</summary>
            public Func<int, int, float>? RoughnessScale;

            /// <summary>Per border (left label, right label): edges longer than this (metres) get extra points (≤ 0 =
            /// never).</summary>
            public Func<int, int, float>? MaxEdge;

            /// <summary>Does slice `across` hide a wall of slice `mine` where it covers it? Default: across.HidesWalls.</summary>
            public Func<TerraceSlice, TerraceSlice, bool>? Hides;

            /// <summary>The lift (added height) of label's lifting slices at a point; null = none anywhere.</summary>
            public Func<int, Vector2, float>? Lift;

            /// <summary>A lifting label's top is refined (TriangleRefine) to edges no longer than this, metres (0 = not):
            /// the slope is then sampled at well-shaped triangles. Its outline should already be split as finely. The top's
            /// T-junctions (points earcut left off an edge) are split first and it is made constrained Delaunay
            /// (DelaunayMesh: earcut's fans of needles — a long one-cell strip's are 50 m long — would refine into hundreds of
            /// thousands of slivers); the refinement stops at a vertex budget scaled to the region's area
            /// (<see cref="Result.RefinesCapped"/> counts the regions that reached it).</summary>
            public float RefineLifted;

            /// <summary>Parts that carry uv1 (position + Uv1Offset) and uv2 (this callback): the rest carry neither.</summary>
            public Func<int, bool>? PartHasUv12;
            public Vector3 Uv1Offset;
            public Func<VertexContext, Vector4>? Uv2;

            /// <summary>Stops a build running off the main thread (between roughness tries and between regions).</summary>
            public System.Threading.CancellationToken Cancel;
        }

        public struct Result
        {
            public bool Success;
            public int Outers;
            public int Holes;
            public int Vertices;
            public int Triangles;
            /// <summary>The roughness actually used (halved from the requested one when simplification made loops cross).</summary>
            public float Roughness;
            public int SaddlesFilled;
            /// <summary>The summed area of every label's top region.</summary>
            public float TopArea;
            /// <summary>Chains in the traced graph (after the edit) and the junction-bounded ones among them.</summary>
            public int Chains, OpenChains;
            /// <summary>Lifted regions whose refinement stopped at its vertex budget (never expected; such a top is
            /// coarser than RefineLifted asks).</summary>
            public int RefinesCapped;
        }

        /// <summary>One output part's faces (a mesh, or a submesh of one).</summary>
        public sealed class Part
        {
            public readonly List<Vector3> Vertices = new();
            public readonly List<Vector3> Normals = new();
            public readonly List<Vector2> Uvs = new();
            public readonly List<Vector4> Uv1 = new();
            public readonly List<Vector4> Uv2 = new();
            public readonly List<int> Triangles = new();
            public bool HasUv12;

            internal readonly Section Tops = new(), Bottoms = new(), Walls = new();

            internal sealed class Section
            {
                public readonly List<Vector3> V = new(), N = new();
                public readonly List<Vector2> Uv = new();
                public readonly List<Vector4> Uv1 = new(), Uv2 = new();
                public readonly List<int> Tris = new();
            }

            public bool IsEmpty => Triangles.Count == 0;

            // Tops, then bottoms, then walls (MaskMesher's order).
            internal void Finish()
            {
                Vertices.Clear();
                Normals.Clear();
                Uvs.Clear();
                Uv1.Clear();
                Uv2.Clear();
                Triangles.Clear();
                Append(Tops);
                Append(Bottoms);
                Append(Walls);
            }

            private void Append(Section s)
            {
                var offset = Vertices.Count;
                Vertices.AddRange(s.V);
                Normals.AddRange(s.N);
                Uvs.AddRange(s.Uv);
                Uv1.AddRange(s.Uv1);
                Uv2.AddRange(s.Uv2);
                foreach (var t in s.Tris) Triangles.Add(t + offset);
            }

            /// <summary>Writes the part into `mesh` (cleared first): one submesh, tangents, bounds.</summary>
            public void WriteTo(Mesh mesh) => WriteSubmeshes(mesh, new[] { this });

            /// <summary>Appends another (finished) part's faces after this one's (e.g. a second build into the same
            /// output).</summary>
            public void AppendFrom(Part other)
            {
                var offset = Vertices.Count;
                Vertices.AddRange(other.Vertices);
                Normals.AddRange(other.Normals);
                Uvs.AddRange(other.Uvs);
                if (HasUv12 && other.HasUv12)
                {
                    Uv1.AddRange(other.Uv1);
                    Uv2.AddRange(other.Uv2);
                }
                foreach (var t in other.Triangles) Triangles.Add(t + offset);
            }
        }

        /// <summary>Writes `parts` into `mesh` (cleared first) as one submesh each, in order: shared vertex lists, tangents,
        /// bounds. uv1 / uv2 are written when every part carries them.</summary>
        public static void WriteSubmeshes(Mesh mesh, IReadOnlyList<Part> parts)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var uv1 = new List<Vector4>();
            var uv2 = new List<Vector4>();
            var withUv12 = parts.Count > 0;
            foreach (var p in parts) withUv12 &= p.HasUv12;
            var starts = new int[parts.Count];
            for (var i = 0; i < parts.Count; i++)
            {
                starts[i] = verts.Count;
                verts.AddRange(parts[i].Vertices);
                normals.AddRange(parts[i].Normals);
                uvs.AddRange(parts[i].Uvs);
                if (!withUv12) continue;
                uv1.AddRange(parts[i].Uv1);
                uv2.AddRange(parts[i].Uv2);
            }
            mesh.Clear();
            mesh.indexFormat = verts.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            if (withUv12)
            {
                mesh.SetUVs(1, uv1);
                mesh.SetUVs(2, uv2);
            }
            mesh.subMeshCount = parts.Count;
            for (var i = 0; i < parts.Count; i++)
            {
                if (starts[i] == 0)
                {
                    mesh.SetTriangles(parts[i].Triangles, i);
                    continue;
                }
                var tris = new List<int>(parts[i].Triangles.Count);
                foreach (var t in parts[i].Triangles) tris.Add(t + starts[i]);
                mesh.SetTriangles(tris, i);
            }
            mesh.RecalculateTangents(); // normal-mapped materials (URP/Lit) need them
            mesh.RecalculateBounds();
        }

        /// <summary>
        /// Meshes `labels` (width × height, row-major; not modified). `columns[label]` = that label's slices (label 0 and
        /// labels with no column are empty). Returns the parts (`partCount` of them) and the result; on failure the parts
        /// are empty. `edit` runs on the traced graph before simplification (cuts: their points are kept exactly).
        /// </summary>
        public static Result Build(int[] labels, int width, int height, float cellSize, Vector2 origin, float roughness,
            IReadOnlyList<TerraceSlice[]?> columns, int partCount, out Part[] parts, GraphEdit? edit = null,
            float minLoopAreaCells = 1.5f) =>
            Build(TerraceGraph.Trace(labels, width, height, cellSize, origin), columns, partCount, out parts,
                new Options { Roughness = roughness, Edit = edit, MinLoopAreaCells = minLoopAreaCells });

        /// <summary>As above on an already traced graph (it is edited, simplified and marked by the build).</summary>
        public static Result Build(TerraceGraph graph, IReadOnlyList<TerraceSlice[]?> columns, int partCount,
            out Part[] parts, Options options)
        {
            var result = new Result { SaddlesFilled = graph.SaddlesFilled };
            parts = new Part[partCount];
            for (var i = 0; i < partCount; i++)
                parts[i] = new Part { HasUv12 = options.PartHasUv12 != null && options.PartHasUv12(i) };
            options.Edit?.Invoke(graph);
            result.Chains = graph.Chains.Count;
            foreach (var c in graph.Chains)
                if (!c.Closed) result.OpenChains++;

            var cellSize = graph.CellSize;
            var minArea = options.MinLoopAreaCells * cellSize * cellSize;
            var eps = Mathf.Max(0f, options.Roughness);
            List<Region>? regions;
            while (true)
            {
                options.Cancel.ThrowIfCancellationRequested();
                if (TryBuild(graph, eps, minArea, options, out regions, ref result)) break;
                if (eps <= 0f)
                {
                    result.Success = false;
                    return result;
                }
                eps = eps < cellSize * 0.05f ? 0f : eps * 0.5f;
            }
            result.Roughness = eps;
            var writer = new Writer(columns, parts, options);
            writer.Write(regions!);
            result.RefinesCapped = writer.RefinesCapped;
            foreach (var p in parts)
            {
                p.Finish();
                result.Vertices += p.Vertices.Count;
                result.Triangles += p.Triangles.Count / 3;
            }
            result.Success = true;
            return result;
        }

        // A label's simplified region: its kept loops (in chain order) and the top triangulation.
        private sealed class Region
        {
            public int Label;
            public readonly List<TerraceGraph.Loop> Loops = new();
            public readonly List<Vector2> Vertices = new();
            public readonly List<int> Triangles = new();
        }

        private static bool TryBuild(TerraceGraph graph, float eps, float minArea, Options options,
            out List<Region>? regions, ref Result result)
        {
            regions = null;
            foreach (var c in graph.Chains) c.Dropped = false;
            graph.Simplify(eps, options.RoughnessScale, options.MaxEdge);
            var labels = graph.Labels();

            // Loops that collapse (< 3 points, or under the minimum area) and border one other label only are dropped:
            // their chains go, and that label takes the area (an island vanishes, a pinhole fills).
            foreach (var label in labels)
            {
                var simple = graph.Loops(label, simplified: true);
                foreach (var loop in simple)
                {
                    if (loop.Points.Count >= 3 && Mathf.Abs(PolygonTriangulator.SignedArea(loop.Points)) >= minArea) continue;
                    var other = loop.Across.Count > 0 ? loop.Across[0] : 0;
                    var single = true;
                    foreach (var a in loop.Across)
                        if (a != other) single = false;
                    if (!single) continue;
                    foreach (var c in loop.Chains) c.Dropped = true;
                }
            }

            regions = new List<Region>();
            result.Outers = result.Holes = 0;
            result.TopArea = 0f;
            foreach (var label in graph.Labels())
            {
                var simple = graph.Loops(label, simplified: true);
                var raw = graph.Loops(label, simplified: false);
                var region = new Region { Label = label };
                for (var i = 0; i < simple.Count; i++)
                {
                    // Simplification must not flip a loop's winding (an outer stays CCW, a hole CW).
                    if (Mathf.Sign(PolygonTriangulator.SignedArea(simple[i].Points)) !=
                        Mathf.Sign(PolygonTriangulator.SignedArea(raw[i].Points)))
                        return false;
                    region.Loops.Add(simple[i]);
                }
                regions.Add(region);
            }
            if (eps > 0f && graph.AnySimplifiedIntersection()) return false;

            foreach (var region in regions)
            {
                var outers = new List<List<Vector2>>();
                var holes = new List<List<Vector2>>();
                foreach (var l in region.Loops)
                    (PolygonTriangulator.SignedArea(l.Points) > 0 ? outers : holes).Add(l.Points);
                var holesOf = new List<List<IReadOnlyList<Vector2>>>();
                foreach (var _ in outers) holesOf.Add(new List<IReadOnlyList<Vector2>>());
                foreach (var h in holes)
                {
                    var best = -1;
                    var bestArea = float.MaxValue;
                    for (var o = 0; o < outers.Count; o++)
                    {
                        if (!PolygonTriangulator.Contains(outers[o], h[0])) continue;
                        var area = PolygonTriangulator.SignedArea(outers[o]);
                        if (area >= bestArea) continue;
                        bestArea = area;
                        best = o;
                    }
                    if (best < 0) return false; // a hole outside every outer: crossed by simplification
                    holesOf[best].Add(h);
                }
                for (var o = 0; o < outers.Count; o++)
                {
                    if (!PolygonTriangulator.Triangulate(outers[o], holesOf[o], region.Vertices, region.Triangles))
                        return false;
                    result.TopArea += PolygonTriangulator.SignedArea(outers[o]);
                    foreach (var h in holesOf[o]) result.TopArea += PolygonTriangulator.SignedArea(h);
                }
                result.Outers += outers.Count;
                result.Holes += holes.Count;
            }
            return true;
        }

        private static readonly TerraceSlice[] NoSlices = new TerraceSlice[0];

        // A wall end shorter than this (metres) has shrunk to nothing.
        private const float MinWall = 1e-4f;

        private sealed class Writer
        {
            private readonly IReadOnlyList<TerraceSlice[]?> _columns;
            private readonly Part[] _parts;
            private readonly Options _o;
            private readonly List<(float lo, float hi)> _a = new(), _b = new();
            private DelaunayMesh? _delaunay;
            public int RefinesCapped;

            public Writer(IReadOnlyList<TerraceSlice[]?> columns, Part[] parts, Options options)
            {
                _columns = columns;
                _parts = parts;
                _o = options;
            }

            private TerraceSlice[] ColumnOf(int label) =>
                label > 0 && label < _columns.Count && _columns[label] != null ? _columns[label]! : NoSlices;

            private float Lift(int label, Vector2 p) => _o.Lift == null ? 0f : _o.Lift(label, p);

            // The slice's top / bottom at a point (its lift applied when it lifts).
            private float TopAt(int label, TerraceSlice s, Vector2 p) =>
                (s.Flags & TerraceSliceFlags.LiftTop) != 0 ? s.Top + Lift(label, p) : s.Top;

            private float BottomAt(int label, TerraceSlice s, Vector2 p) =>
                (s.Flags & TerraceSliceFlags.LiftBottom) != 0 ? s.Bottom + Lift(label, p) : s.Bottom;

            public void Write(List<Region> regions)
            {
                foreach (var region in regions)
                {
                    _o.Cancel.ThrowIfCancellationRequested();
                    var column = ColumnOf(region.Label);
                    if (_o.Lift != null && _o.RefineLifted > 0f)
                        foreach (var slice in column)
                            if ((slice.Flags & (TerraceSliceFlags.LiftTop | TerraceSliceFlags.LiftBottom)) != 0)
                            {
                                Refine(region);
                                break;
                            }
                    foreach (var slice in column)
                    {
                        if ((slice.Flags & TerraceSliceFlags.Skirt) != 0) continue;
                        var part = _parts[slice.Part];
                        var thick = slice.Top - slice.Bottom > 0f;
                        if ((slice.Faces & TerraceFaces.Top) != 0) Cap(part.Tops, part, region, slice, top: true);
                        if (thick && (slice.Faces & TerraceFaces.Bottom) != 0) Cap(part.Bottoms, part, region, slice, top: false);
                    }
                    // Walls: every loop keeps the label on its left, so each edge's wall faces right — over the heights
                    // where this column is solid and the one across is not.
                    foreach (var loop in region.Loops)
                    {
                        var pts = loop.Points;
                        for (var i = 0; i < pts.Count; i++)
                        {
                            var a2 = pts[i];
                            var b2 = pts[(i + 1) % pts.Count];
                            var d = b2 - a2;
                            if (d.sqrMagnitude < 1e-12f) continue;
                            var acrossLabel = loop.Across[i];
                            var across = ColumnOf(acrossLabel);
                            var n = new Vector3(d.y, 0, -d.x).normalized;
                            foreach (var slice in column)
                            {
                                if ((slice.Faces & TerraceFaces.Walls) == 0) continue;
                                var part = _parts[slice.Part];
                                if ((slice.Flags & TerraceSliceFlags.Skirt) != 0)
                                {
                                    Skirt(part, region.Label, slice, across, acrossLabel, a2, b2, n);
                                    continue;
                                }
                                if (!(slice.Top - slice.Bottom > 0f)) continue;
                                if (_o.Lift == null)
                                {
                                    // Flat: one interval set for the whole edge (MaskMesher's exact floats).
                                    Uncovered(slice, slice.Bottom, slice.Top, across, acrossLabel, a2, _a);
                                    foreach (var (lo, hi) in _a)
                                        Wall(part, region.Label, slice, acrossLabel, a2, b2, n, lo, hi, lo, hi, slice.Top,
                                            slice.Top);
                                    continue;
                                }
                                // Lifted: the intervals at each end, paired in order.
                                float ta = TopAt(region.Label, slice, a2), tb = TopAt(region.Label, slice, b2);
                                Uncovered(slice, BottomAt(region.Label, slice, a2), ta, across, acrossLabel, a2, _a);
                                Uncovered(slice, BottomAt(region.Label, slice, b2), tb, across, acrossLabel, b2, _b);
                                Pair(part, region.Label, slice, acrossLabel, a2, b2, n, ta, tb);
                            }
                        }
                    }
                }
            }

            // A lifted region's top: its T-junctions repaired (RepairTJunctions), flipped to the constrained Delaunay
            // triangulation (same points, same outline), then refined, at most to a vertex budget of 8 per maxEdge² of area
            // (well-shaped triangles with edges up to maxEdge need about 1.2) beyond the points it has — so even a bad input
            // ends in milliseconds.
            private void Refine(Region region)
            {
                RepairTJunctions(region, _o.RefineLifted);
                var d = _delaunay ??= new DelaunayMesh();
                d.Reset(region.Vertices, region.Triangles);
                d.MakeDelaunay();
                region.Triangles.Clear();
                region.Triangles.AddRange(d.Triangles);
                var v = region.Vertices;
                var t = region.Triangles;
                var area = 0f;
                for (var i = 0; i < t.Count; i += 3)
                {
                    Vector2 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                    area += Mathf.Abs((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x)) * 0.5f;
                }
                var max = _o.RefineLifted;
                var budget = v.Count + 16 + Mathf.CeilToInt(Mathf.Min(8f * area / (max * max), 1e6f));
                TriangleRefine.Refine(v, t, max, budget);
                if (v.Count >= budget) RefinesCapped++;
            }

            // Earcut drops zero-area ears, so points can be missing from an edge: a run of collinear outline points (a
            // straight line split every cell) spanned by one outline edge, or an edge across the region running straight
            // through a vertex of another loop (a T-junction inside). A flat top doesn't mind; a lifted one must sample the
            // lift at every point and meet its neighbours' triangles there (no crack), and the refinement treats such an
            // edge as outline and stalls on it. Every edge used by one triangle that has points of the region's loops on
            // it is split there: that triangle becomes a fan from its third corner.
            private static void RepairTJunctions(Region region, float cell)
            {
                var v = region.Vertices;
                var t = region.Triangles;
                var index = new Dictionary<Vector2, int>();
                for (var i = 0; i < v.Count; i++) index.TryAdd(v[i], i);
                // The loops' points, hashed by cell.
                var grid = new Dictionary<long, List<Vector2>>(EdgeKeys.Comparer);
                long CellKey(int x, int y) => (long)x << 32 | (uint)y;
                foreach (var loop in region.Loops)
                foreach (var p in loop.Points)
                {
                    var k = CellKey(Mathf.FloorToInt(p.x / cell), Mathf.FloorToInt(p.y / cell));
                    if (!grid.TryGetValue(k, out var list)) grid[k] = list = new List<Vector2>();
                    list.Add(p);
                }
                var uses = new Dictionary<long, int>(EdgeKeys.Comparer); // undirected edge → triangles using it
                long Key(int a, int b) => a < b ? (long)a << 32 | (uint)b : (long)b << 32 | (uint)a;
                void Use(int a, int b, int n) => uses[Key(a, b)] = (uses.TryGetValue(Key(a, b), out var u) ? u : 0) + n;
                for (var i = 0; i < t.Count; i += 3)
                for (var k = 0; k < 3; k++)
                    Use(t[i + k], t[i + (k + 1) % 3], 1);
                var on = new List<(float s, Vector2 p)>();
                for (var tri = 0; tri < t.Count / 3; tri++)
                for (var k = 0; k < 3; k++)
                {
                    int a = t[tri * 3 + k], b = t[tri * 3 + (k + 1) % 3], c = t[tri * 3 + (k + 2) % 3];
                    if (uses[Key(a, b)] != 1) continue;
                    Vector2 pa = v[a], pb = v[b];
                    var d = pb - pa;
                    var len2 = d.sqrMagnitude;
                    on.Clear();
                    int x0 = Mathf.FloorToInt(Mathf.Min(pa.x, pb.x) / cell), x1 = Mathf.FloorToInt(Mathf.Max(pa.x, pb.x) / cell);
                    int y0 = Mathf.FloorToInt(Mathf.Min(pa.y, pb.y) / cell), y1 = Mathf.FloorToInt(Mathf.Max(pa.y, pb.y) / cell);
                    for (var y = y0; y <= y1; y++)
                    for (var x = x0; x <= x1; x++)
                    {
                        if (!grid.TryGetValue(CellKey(x, y), out var list)) continue;
                        foreach (var p in list)
                        {
                            if (p == pa || p == pb) continue;
                            var q = p - pa;
                            var along = Vector2.Dot(q, d);
                            if (along <= 0f || along >= len2) continue;
                            if (Mathf.Abs(q.x * d.y - q.y * d.x) > 1e-6f * Mathf.Sqrt(len2)) continue; // off the line (> 1 µm)
                            on.Add((along, p));
                        }
                    }
                    if (on.Count == 0) continue;
                    on.Sort((m, n) => m.s.CompareTo(n.s));
                    // (a, b, c) → (a, p1, c), (p1, p2, c) … (pn, b, c): the same winding.
                    Use(a, b, -1);
                    var prev = a;
                    for (var r = 0; r <= on.Count; r++)
                    {
                        int next;
                        if (r < on.Count)
                        {
                            var p = on[r].p;
                            if (!index.TryGetValue(p, out next))
                            {
                                next = v.Count;
                                v.Add(p);
                                index[p] = next;
                            }
                        }
                        else next = b;
                        if (r == 0)
                        {
                            t[tri * 3] = prev;
                            t[tri * 3 + 1] = next;
                            t[tri * 3 + 2] = c;
                        }
                        else
                        {
                            t.Add(prev);
                            t.Add(next);
                            t.Add(c);
                        }
                        Use(prev, next, 1);
                        if (r < on.Count) Use(next, c, 2);
                        prev = next;
                    }
                    k = -1; // this triangle's edges changed: look at them again
                }
            }

            // A top (or bottom) over the region. Flat (no lift): MaskMesher's exact vertices, normal straight up / down.
            // Lifted: heights per vertex; each vertex's normal = the area-weighted normal of its triangles.
            private void Cap(Part.Section s, Part part, Region region, TerraceSlice slice, bool top)
            {
                var at = s.V.Count;
                var lifts = (slice.Flags & (top ? TerraceSliceFlags.LiftTop : TerraceSliceFlags.LiftBottom)) != 0 &&
                            _o.Lift != null;
                var any = false;
                foreach (var p in region.Vertices)
                {
                    Vector3 v;
                    if (top) v = new Vector3(p.x, slice.Top, p.y);
                    else v = new Vector3(p.x, slice.Top, p.y) + new Vector3(0, slice.Bottom - slice.Top, 0);
                    if (lifts)
                    {
                        var l = Lift(region.Label, p);
                        if (l != 0f)
                        {
                            v.y += l;
                            any = true;
                        }
                    }
                    s.V.Add(v);
                    s.N.Add(top ? Vector3.up : Vector3.down);
                    s.Uv.Add(p);
                    if (part.HasUv12) AddUv12(s, top ? FaceKinds.Top : FaceKinds.Bottom, region.Label, slice, 0, v,
                        default, default, 0f);
                }
                var tris = region.Triangles;
                for (var i = 0; i < tris.Count; i += 3)
                {
                    if (top)
                    {
                        s.Tris.Add(at + tris[i]);
                        s.Tris.Add(at + tris[i + 2]);
                        s.Tris.Add(at + tris[i + 1]);
                    }
                    else
                    {
                        s.Tris.Add(at + tris[i]);
                        s.Tris.Add(at + tris[i + 1]);
                        s.Tris.Add(at + tris[i + 2]);
                    }
                }
                if (!any) return;
                // Sloped somewhere: normals from the faces (Unity winding: clockwise seen from the front).
                for (var i = at; i < s.N.Count; i++) s.N[i] = Vector3.zero;
                for (var i = 0; i < tris.Count; i += 3)
                {
                    int a = at + tris[i], b = at + tris[i + 1], c = at + tris[i + 2];
                    var n = Vector3.Cross(s.V[c] - s.V[a], s.V[b] - s.V[a]); // CCW in xz = up
                    if (!top) n = -n;
                    s.N[a] += n;
                    s.N[b] += n;
                    s.N[c] += n;
                }
                for (var i = at; i < s.N.Count; i++)
                    s.N[i] = s.N[i].sqrMagnitude > 1e-20f ? s.N[i].normalized : top ? Vector3.up : Vector3.down;
            }

            // The parts of [lo, hi] of `slice` no slice of the column across covers (that hides it) — at point p (lifts).
            private void Uncovered(TerraceSlice slice, float lo0, float hi0, TerraceSlice[] across, int acrossLabel,
                Vector2 p, List<(float lo, float hi)> into)
            {
                into.Clear();
                if (!(hi0 > lo0)) return;
                into.Add((lo0, hi0));
                foreach (var other in across)
                {
                    if ((other.Flags & TerraceSliceFlags.Skirt) != 0) continue;
                    if (!(_o.Hides?.Invoke(slice, other) ?? other.HidesWalls)) continue;
                    var ob = _o.Lift == null ? other.Bottom : BottomAt(acrossLabel, other, p);
                    var ot = _o.Lift == null ? other.Top : TopAt(acrossLabel, other, p);
                    if (!(ot > ob)) continue;
                    for (var k = into.Count - 1; k >= 0; k--)
                    {
                        var (lo, hi) = into[k];
                        if (ot <= lo || ob >= hi) continue;
                        into.RemoveAt(k);
                        if (ot < hi) into.Insert(k, (ot, hi));
                        if (ob > lo) into.Insert(k, (lo, ob));
                    }
                }
            }

            // Lifted walls: the k-th interval at a with the k-th at b (an interval missing at one end closes to a point
            // there: the wall tapers to nothing — e.g. where a chamfer meets the riser it replaces).
            private void Pair(Part part, int label, TerraceSlice slice, int across, Vector2 a2, Vector2 b2, Vector3 n,
                float topA, float topB)
            {
                var count = Mathf.Max(_a.Count, _b.Count);
                for (var k = 0; k < count; k++)
                {
                    // A missing interval closes at the slice's top there (the side tapers up to it).
                    (float lo, float hi) ia = k < _a.Count ? _a[k] : (topA, topA);
                    (float lo, float hi) ib = k < _b.Count ? _b[k] : (topB, topB);
                    if (!(ia.hi - ia.lo > MinWall) && !(ib.hi - ib.lo > MinWall)) continue;
                    Wall(part, label, slice, across, a2, b2, n, ia.lo, ia.hi, ib.lo, ib.hi, topA, topB);
                }
            }

            // A skirt along one edge: from the top of the column across (when not empty and below) up to the slice's top.
            private void Skirt(Part part, int label, TerraceSlice slice, TerraceSlice[] across, int acrossLabel, Vector2 a2,
                Vector2 b2, Vector3 n)
            {
                float SurfaceAt(Vector2 p)
                {
                    var top = float.NegativeInfinity;
                    foreach (var o in across)
                        if ((o.Flags & (TerraceSliceFlags.Skirt | TerraceSliceFlags.NotSurface)) == 0)
                            top = Mathf.Max(top, _o.Lift == null ? o.Top : TopAt(acrossLabel, o, p));
                    return top;
                }
                var ta = SurfaceAt(a2);
                var tb = SurfaceAt(b2);
                if (float.IsNegativeInfinity(ta) || float.IsNegativeInfinity(tb)) return; // nothing across: no skirt
                float ha = TopAt(label, slice, a2), hb = TopAt(label, slice, b2);
                if (!(ha > ta) && !(hb > tb)) return;
                Wall(part, label, slice, acrossLabel, a2, b2, n, Mathf.Min(ta, ha), ha, Mathf.Min(tb, hb), hb, ha, hb);
            }

            // One wall quad from a2 → b2 between heights [loA, hiA] at a and [loB, hiB] at b (MaskMesher's vertex order:
            // top a, top b, bottom b, bottom a). A flat wall's heights are written as MaskMesher writes them: the slice
            // top, then down from it.
            private void Wall(Part part, int label, TerraceSlice slice, int across, Vector2 a2, Vector2 b2, Vector3 n,
                float loA, float hiA, float loB, float hiB, float topA, float topB)
            {
                var s = part.Walls;
                var w = s.V.Count;
                Vector3 ua, ub, la, lb;
                if (topA == topB && hiA == hiB && loA == loB)
                {
                    var at = new Vector3(a2.x, topA, a2.y);
                    var bt = new Vector3(b2.x, topA, b2.y);
                    if (hiA == topA)
                    {
                        ua = at;
                        ub = bt;
                    }
                    else
                    {
                        var upper = new Vector3(0, hiA - topA, 0);
                        ua = at + upper;
                        ub = bt + upper;
                    }
                    var lower = new Vector3(0, loA - topA, 0);
                    la = at + lower;
                    lb = bt + lower;
                }
                else
                {
                    ua = new Vector3(a2.x, hiA, a2.y);
                    ub = new Vector3(b2.x, hiB, b2.y);
                    la = new Vector3(a2.x, loA, a2.y);
                    lb = new Vector3(b2.x, loB, b2.y);
                }
                s.V.Add(ua);
                s.V.Add(ub);
                s.V.Add(lb);
                s.V.Add(la);
                for (var k = 0; k < 4; k++) s.N.Add(n);
                s.Uv.Add(a2);
                s.Uv.Add(b2);
                s.Uv.Add(b2);
                s.Uv.Add(a2);
                if (part.HasUv12)
                {
                    AddUv12(s, FaceKinds.Wall, label, slice, across, ua, a2, b2, topA);
                    AddUv12(s, FaceKinds.Wall, label, slice, across, ub, a2, b2, topB);
                    AddUv12(s, FaceKinds.Wall, label, slice, across, lb, a2, b2, topB);
                    AddUv12(s, FaceKinds.Wall, label, slice, across, la, a2, b2, topA);
                }
                // A side that has shrunk to nothing at one end is one triangle (under MinWall: nothing, no sliver).
                var degenerateB = !(hiB - loB > MinWall);
                var degenerateA = !(hiA - loA > MinWall);
                if (!degenerateB)
                {
                    s.Tris.Add(w);
                    s.Tris.Add(w + 1);
                    s.Tris.Add(w + 2);
                }
                if (!degenerateA)
                {
                    s.Tris.Add(w);
                    s.Tris.Add(w + 2);
                    s.Tris.Add(w + 3);
                }
            }

            private void AddUv12(Part.Section s, FaceKinds face, int label, TerraceSlice slice, int across, Vector3 v,
                Vector2 from, Vector2 to, float sliceTop)
            {
                s.Uv1.Add(v + _o.Uv1Offset);
                s.Uv2.Add(_o.Uv2?.Invoke(new VertexContext
                {
                    Face = face, Label = label, Slice = slice, Across = across, Position = v, SliceTop = sliceTop,
                    EdgeFrom = from, EdgeTo = to,
                }) ?? Vector4.zero);
            }
        }
    }
}
