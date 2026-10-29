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

        /// <summary>Hides a neighbouring column's walls where it covers them (a solid seen from beside).</summary>
        public bool HidesWalls;

        public TerraceSlice(float bottom, float top, int part, TerraceFaces faces = TerraceFaces.All, bool hidesWalls = true)
        {
            Bottom = bottom;
            Top = top;
            Part = part;
            Faces = faces;
            HidesWalls = hidesWalls;
        }
    }

    [System.Flags]
    public enum TerraceFaces
    {
        None = 0,
        Top = 1,
        Bottom = 2,
        Walls = 4,
        All = Top | Bottom | Walls,
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
    /// </summary>
    public static class TerraceMesher
    {
        /// <summary>An edit of the traced graph before it is simplified (e.g. <see cref="TerraceGraph.Subtract"/>).</summary>
        public delegate void GraphEdit(TerraceGraph graph);

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
        }

        /// <summary>One output part's faces (a mesh, or a submesh of one).</summary>
        public sealed class Part
        {
            public readonly List<Vector3> Vertices = new();
            public readonly List<Vector3> Normals = new();
            public readonly List<Vector2> Uvs = new();
            public readonly List<int> Triangles = new();

            internal readonly List<int> TopTris = new(), BottomTris = new(), WallTris = new();
            internal readonly List<Vector3> TopV = new(), BottomV = new(), WallV = new();
            internal readonly List<Vector3> TopN = new(), BottomN = new(), WallN = new();
            internal readonly List<Vector2> TopUv = new(), BottomUv = new(), WallUv = new();

            public bool IsEmpty => Triangles.Count == 0;

            // Tops, then bottoms, then walls (MaskMesher's order).
            internal void Finish()
            {
                Vertices.Clear();
                Normals.Clear();
                Uvs.Clear();
                Triangles.Clear();
                Append(TopV, TopN, TopUv, TopTris);
                Append(BottomV, BottomN, BottomUv, BottomTris);
                Append(WallV, WallN, WallUv, WallTris);
            }

            private void Append(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> tris)
            {
                var offset = Vertices.Count;
                Vertices.AddRange(v);
                Normals.AddRange(n);
                Uvs.AddRange(uv);
                foreach (var t in tris) Triangles.Add(t + offset);
            }

            /// <summary>Writes the part into `mesh` (cleared first): one submesh, tangents, bounds.</summary>
            public void WriteTo(Mesh mesh)
            {
                mesh.Clear();
                mesh.indexFormat = Vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                mesh.SetVertices(Vertices);
                mesh.SetNormals(Normals);
                mesh.SetUVs(0, Uvs);
                mesh.SetTriangles(Triangles, 0);
                mesh.RecalculateTangents(); // normal-mapped materials (URP/Lit) need them
                mesh.RecalculateBounds();
            }
        }

        /// <summary>
        /// Meshes `labels` (width × height, row-major; not modified). `columns[label]` = that label's slices (label 0 and
        /// labels with no column are empty). Returns the parts (`partCount` of them) and the result; on failure the parts
        /// are empty. `edit` runs on the traced graph before simplification (cuts: their points are kept exactly).
        /// </summary>
        public static Result Build(int[] labels, int width, int height, float cellSize, Vector2 origin, float roughness,
            IReadOnlyList<TerraceSlice[]?> columns, int partCount, out Part[] parts, GraphEdit? edit = null,
            float minLoopAreaCells = 1.5f) =>
            Build(TerraceGraph.Trace(labels, width, height, cellSize, origin), roughness, columns, partCount, out parts,
                edit, minLoopAreaCells);

        /// <summary>As above on an already traced graph (it is edited, simplified and marked by the build).</summary>
        public static Result Build(TerraceGraph graph, float roughness, IReadOnlyList<TerraceSlice[]?> columns,
            int partCount, out Part[] parts, GraphEdit? edit = null, float minLoopAreaCells = 1.5f)
        {
            var result = new Result { SaddlesFilled = graph.SaddlesFilled };
            parts = new Part[partCount];
            for (var i = 0; i < partCount; i++) parts[i] = new Part();
            edit?.Invoke(graph);
            result.Chains = graph.Chains.Count;
            foreach (var c in graph.Chains)
                if (!c.Closed) result.OpenChains++;

            var cellSize = graph.CellSize;
            var minArea = minLoopAreaCells * cellSize * cellSize;
            var eps = Mathf.Max(0f, roughness);
            List<Region>? regions;
            while (true)
            {
                if (TryBuild(graph, eps, minArea, out regions, ref result)) break;
                if (eps <= 0f)
                {
                    result.Success = false;
                    return result;
                }
                eps = eps < cellSize * 0.05f ? 0f : eps * 0.5f;
            }
            result.Roughness = eps;
            Write(regions!, columns, parts);
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

        private static bool TryBuild(TerraceGraph graph, float eps, float minArea, out List<Region>? regions,
            ref Result result)
        {
            regions = null;
            foreach (var c in graph.Chains) c.Dropped = false;
            graph.Simplify(eps);
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

        private static TerraceSlice[] ColumnOf(IReadOnlyList<TerraceSlice[]?> columns, int label) =>
            label > 0 && label < columns.Count && columns[label] != null ? columns[label]! : NoSlices;

        private static void Write(List<Region> regions, IReadOnlyList<TerraceSlice[]?> columns, Part[] parts)
        {
            var intervals = new List<(float lo, float hi)>();
            foreach (var region in regions)
            {
                var column = ColumnOf(columns, region.Label);
                foreach (var slice in column)
                {
                    var part = parts[slice.Part];
                    var thick = slice.Top - slice.Bottom > 0f;
                    // Top: 2D triangles are counter-clockwise; Unity front faces are clockwise seen from the normal's side.
                    if ((slice.Faces & TerraceFaces.Top) != 0)
                    {
                        var at = part.TopV.Count;
                        foreach (var p in region.Vertices)
                        {
                            part.TopV.Add(new Vector3(p.x, slice.Top, p.y));
                            part.TopN.Add(Vector3.up);
                            part.TopUv.Add(p);
                        }
                        for (var i = 0; i < region.Triangles.Count; i += 3)
                        {
                            part.TopTris.Add(at + region.Triangles[i]);
                            part.TopTris.Add(at + region.Triangles[i + 2]);
                            part.TopTris.Add(at + region.Triangles[i + 1]);
                        }
                    }
                    if (thick && (slice.Faces & TerraceFaces.Bottom) != 0)
                    {
                        var at = part.BottomV.Count;
                        foreach (var p in region.Vertices)
                        {
                            part.BottomV.Add(new Vector3(p.x, slice.Top, p.y) + new Vector3(0, slice.Bottom - slice.Top, 0));
                            part.BottomN.Add(Vector3.down);
                            part.BottomUv.Add(p);
                        }
                        for (var i = 0; i < region.Triangles.Count; i += 3)
                        {
                            part.BottomTris.Add(at + region.Triangles[i]);
                            part.BottomTris.Add(at + region.Triangles[i + 1]);
                            part.BottomTris.Add(at + region.Triangles[i + 2]);
                        }
                    }
                }
                // Walls: every loop keeps the label on its left, so each edge's wall faces right — over the heights where
                // this column is solid and the one across is not.
                foreach (var loop in region.Loops)
                {
                    var pts = loop.Points;
                    for (var i = 0; i < pts.Count; i++)
                    {
                        var a2 = pts[i];
                        var b2 = pts[(i + 1) % pts.Count];
                        var d = b2 - a2;
                        if (d.sqrMagnitude < 1e-12f) continue;
                        var across = ColumnOf(columns, loop.Across[i]);
                        var n = new Vector3(d.y, 0, -d.x).normalized;
                        foreach (var slice in column)
                        {
                            if ((slice.Faces & TerraceFaces.Walls) == 0 || !(slice.Top - slice.Bottom > 0f)) continue;
                            Uncovered(slice, across, intervals);
                            var part = parts[slice.Part];
                            foreach (var (lo, hi) in intervals) Wall(part, a2, b2, n, lo, hi, slice.Top);
                        }
                    }
                }
            }
        }

        // The parts of `slice` no slice of the column across covers (that hides walls).
        private static void Uncovered(TerraceSlice slice, TerraceSlice[] across, List<(float lo, float hi)> into)
        {
            into.Clear();
            into.Add((slice.Bottom, slice.Top));
            foreach (var other in across)
            {
                if (!other.HidesWalls || !(other.Top > other.Bottom)) continue;
                for (var k = into.Count - 1; k >= 0; k--)
                {
                    var (lo, hi) = into[k];
                    if (other.Top <= lo || other.Bottom >= hi) continue;
                    into.RemoveAt(k);
                    if (other.Top < hi) into.Insert(k, (other.Top, hi));
                    if (other.Bottom > lo) into.Insert(k, (lo, other.Bottom));
                }
            }
        }

        // One wall quad from a2 → b2 between heights lo and hi (MaskMesher's vertex order: top a, top b, bottom b,
        // bottom a). Heights as MaskMesher writes them: the slice top, then down from it.
        private static void Wall(Part part, Vector2 a2, Vector2 b2, Vector3 n, float lo, float hi, float top)
        {
            var at = new Vector3(a2.x, top, a2.y);
            var bt = new Vector3(b2.x, top, b2.y);
            var lower = new Vector3(0, lo - top, 0);
            var w = part.WallV.Count;
            if (hi == top)
            {
                part.WallV.Add(at);
                part.WallV.Add(bt);
            }
            else
            {
                var upper = new Vector3(0, hi - top, 0);
                part.WallV.Add(at + upper);
                part.WallV.Add(bt + upper);
            }
            part.WallV.Add(bt + lower);
            part.WallV.Add(at + lower);
            for (var k = 0; k < 4; k++) part.WallN.Add(n);
            part.WallUv.Add(a2);
            part.WallUv.Add(b2);
            part.WallUv.Add(b2);
            part.WallUv.Add(a2);
            part.WallTris.Add(w);
            part.WallTris.Add(w + 1);
            part.WallTris.Add(w + 2);
            part.WallTris.Add(w);
            part.WallTris.Add(w + 2);
            part.WallTris.Add(w + 3);
        }
    }
}
