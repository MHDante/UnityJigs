using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityJigs.Geometry
{
    /// <summary>
    /// Turns a <see cref="BitMask"/> into a horizontal slab mesh: the set cells' outline, simplified to `roughness`
    /// (faceted — no smoothing), triangulated on top, mirrored underneath, joined by walls along every outer and
    /// hole edge. Mesh space: cell (x, y) of the mask covers [origin + (x, y)·cellSize, +cellSize] on the XZ plane;
    /// the top face is at y = 0, the bottom at y = −thickness. Faces have split normals and tangents; uv0 = (x, z).
    /// </summary>
    public static class MaskMesher
    {
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
            public float TopArea;
        }

        /// <summary>Builds into `mesh` (cleared first). An empty mask gives an empty mesh and Success.</summary>
        public static Result Build(BitMask mask, float cellSize, Vector2 origin, float roughness, float thickness, Mesh mesh,
            float minLoopAreaCells = 1.5f)
        {
            var result = new Result();
            var work = new BitMask(mask);
            result.SaddlesFilled = work.FillSaddles();

            var raw = new List<List<Vector2>>();
            MaskContours.Trace(work, raw);
            foreach (var loop in raw)
                for (var i = 0; i < loop.Count; i++)
                    loop[i] = origin + loop[i] * cellSize;

            var minArea = minLoopAreaCells * cellSize * cellSize;
            var eps = Mathf.Max(0f, roughness);
            var loops = new List<List<Vector2>>();
            var vertices2 = new List<Vector2>();
            var triangles = new List<int>();
            while (true)
            {
                loops.Clear();
                vertices2.Clear();
                triangles.Clear();
                if (TryBuild(raw, eps, minArea, loops, vertices2, triangles, ref result)) break;
                if (eps <= 0f)
                {
                    result.Success = false;
                    mesh.Clear();
                    return result;
                }
                eps = eps < cellSize * 0.05f ? 0f : eps * 0.5f;
            }
            result.Roughness = eps;
            WriteSlab(loops, vertices2, triangles, thickness, mesh, ref result);
            result.Success = true;
            return result;
        }

        private static bool TryBuild(List<List<Vector2>> raw, float eps, float minArea, List<List<Vector2>> loops,
            List<Vector2> vertices2, List<int> triangles, ref Result result)
        {
            foreach (var loop in raw)
            {
                var simple = new List<Vector2>();
                PolygonSimplify.SimplifyClosed(loop, eps, simple);
                if (simple.Count < 3) continue;
                if (Mathf.Abs(PolygonTriangulator.SignedArea(simple)) < minArea) continue;
                // Simplification must not flip a loop's winding (an outer stays CCW, a hole CW).
                if (Mathf.Sign(PolygonTriangulator.SignedArea(simple)) != Mathf.Sign(PolygonTriangulator.SignedArea(loop)))
                    return false;
                loops.Add(simple);
            }
            var asRead = new List<IReadOnlyList<Vector2>>(loops.Count);
            foreach (var l in loops) asRead.Add(l);
            if (eps > 0f && PolygonTriangulator.AnyIntersection(asRead)) return false;

            var outers = new List<List<Vector2>>();
            var holes = new List<List<Vector2>>();
            foreach (var l in loops)
                (PolygonTriangulator.SignedArea(l) > 0 ? outers : holes).Add(l);
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
            result.TopArea = 0f;
            for (var o = 0; o < outers.Count; o++)
            {
                if (!PolygonTriangulator.Triangulate(outers[o], holesOf[o], vertices2, triangles)) return false;
                result.TopArea += PolygonTriangulator.SignedArea(outers[o]);
                foreach (var h in holesOf[o]) result.TopArea += PolygonTriangulator.SignedArea(h);
            }
            result.Outers = outers.Count;
            result.Holes = holes.Count;
            return true;
        }

        private static void WriteSlab(List<List<Vector2>> loops, List<Vector2> top, List<int> topTris, float thickness,
            Mesh mesh, ref Result result)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            var down = new Vector3(0, -Mathf.Max(0f, thickness), 0);

            // Top: 2D triangles are counter-clockwise; Unity front faces are clockwise seen from the normal's side.
            foreach (var p in top) Add(new Vector3(p.x, 0, p.y), Vector3.up, p);
            for (var i = 0; i < topTris.Count; i += 3)
            {
                tris.Add(topTris[i]);
                tris.Add(topTris[i + 2]);
                tris.Add(topTris[i + 1]);
            }
            if (thickness > 0f)
            {
                var baseBottom = verts.Count;
                foreach (var p in top) Add(new Vector3(p.x, 0, p.y) + down, Vector3.down, p);
                for (var i = 0; i < topTris.Count; i += 3)
                {
                    tris.Add(baseBottom + topTris[i]);
                    tris.Add(baseBottom + topTris[i + 1]);
                    tris.Add(baseBottom + topTris[i + 2]);
                }
                // Walls: every loop keeps ice on its left, so each edge's wall faces right.
                foreach (var loop in loops)
                for (var i = 0; i < loop.Count; i++)
                {
                    var a2 = loop[i];
                    var b2 = loop[(i + 1) % loop.Count];
                    var d = b2 - a2;
                    if (d.sqrMagnitude < 1e-12f) continue;
                    var n = new Vector3(d.y, 0, -d.x).normalized;
                    var at = new Vector3(a2.x, 0, a2.y);
                    var bt = new Vector3(b2.x, 0, b2.y);
                    var w = verts.Count;
                    Add(at, n, a2);
                    Add(bt, n, b2);
                    Add(bt + down, n, b2);
                    Add(at + down, n, a2);
                    tris.Add(w); tris.Add(w + 1); tris.Add(w + 2);
                    tris.Add(w); tris.Add(w + 2); tris.Add(w + 3);
                }
            }

            mesh.Clear();
            mesh.indexFormat = verts.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateTangents(); // normal-mapped materials (URP/Lit) need them
            mesh.RecalculateBounds();
            result.Vertices = verts.Count;
            result.Triangles = tris.Count / 3;

            void Add(Vector3 v, Vector3 n, Vector2 uv)
            {
                verts.Add(v);
                normals.Add(n);
                uvs.Add(uv);
            }
        }
    }
}
