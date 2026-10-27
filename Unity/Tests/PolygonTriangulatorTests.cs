using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityJigs.Geometry;

namespace UnityJigs.Tests
{
    // The hard cases for the triangulator (earcut): hole-rich masks (the ear clipper it replaced dead-ended on them after
    // bridging and MaskMesher retried down to roughness 0, then failed), touching holes, collinear runs, slivers.
    public class PolygonTriangulatorTests
    {
        // n × n cells all set, minus `count` round holes (radius rMin..rMax+1 cells), seeded.
        private static BitMask Holes(int n, int count, int seed, int rMin, int rMax)
        {
            var m = new BitMask(n, n, true);
            var rnd = new System.Random(seed);
            for (var k = 0; k < count; k++)
            {
                var r = rnd.Next(rMin, rMax + 1) + (float)rnd.NextDouble();
                m.FillDisc(new Vector2(rnd.Next(rMax + 4, n - rMax - 4), rnd.Next(rMax + 4, n - rMax - 4)), r, false);
            }
            return m;
        }

        private static MaskMesher.Result AssertMeshes(BitMask m, int holes)
        {
            var mesh = new Mesh();
            var r = MaskMesher.Build(m, 0.25f, Vector2.zero, 0.3f, 0.3f, mesh);
            Assert.IsTrue(r.Success);
            Assert.AreEqual(0.3f, r.Roughness, "the roughness was halved: a triangulation failed");
            Assert.AreEqual(holes, r.Holes);
            AssertTopCoversArea(mesh, r.TopArea);
            Object.DestroyImmediate(mesh);
            return r;
        }

        [Test]
        public void HundredTwentyFiveHolesInOneOuter()
        {
            // A lake-sized sheet: 150 m with 125 small holes (some merge: 124 loops).
            var m = Holes(600, 125, 11, 1, 6);
            var r = AssertMeshes(m, 124);
            Assert.AreEqual(1, r.Outers);
            Assert.AreEqual(m.Count * 0.0625f, r.TopArea, m.Count * 0.0625f * 0.001f);
        }

        [Test]
        public void TwentyOneRoundHolesInA215MetreSheet()
        {
            var m = new BitMask(860, 860, true);
            var rnd = new System.Random(1);
            for (var k = 0; k < 21; k++) m.FillDisc(new Vector2(rnd.Next(60, 800), rnd.Next(60, 800)), rnd.Next(6, 30), false);
            AssertMeshes(m, 19); // overlapping discs merge
        }

        [Test]
        public void TouchingHolesMesh()
        {
            // 144 square holes, each with a smaller neighbour sharing an edge or touching a corner of it.
            var m = new BitMask(300, 300, true);
            for (var j = 0; j < 12; j++)
            for (var i = 0; i < 12; i++)
            {
                int x = 10 + i * 23, y = 10 + j * 23;
                Fill(m, x, y, 6, 6);
                Fill(m, x + 6, (j & 1) == 1 ? y + 6 : y, 4, 4);
            }
            AssertMeshes(m, 216);
        }

        [Test]
        public void ThinStripsMesh()
        {
            // 1-cell lines at shallow slopes (thin holes), and a field of 1-cell-wide diagonal ice strips.
            var m = new BitMask(400, 400, true);
            for (var k = 0; k < 12; k++)
            for (var x = 10; x < 390; x++)
                m[x, 20 + k * 30 + (int)(x / (3f + k * 2))] = false;
            for (var y = 300; y < 390; y++)
            for (var x = 10; x < 390; x++)
                m[x, y] = (x + y / 7) % 9 == 0;
            AssertMeshes(m, 50);
        }

        [Test]
        public void HolesTouchingAtAVertexAndTheOuter()
        {
            var holes = new List<IReadOnlyList<Vector2>>
            {
                Loop(2, 2, 2, 5, 5, 5, 5, 2),
                Loop(5, 5, 5, 8, 8, 8, 8, 5), // shares (5, 5) with the first
                Loop(10, 1, 9, 0.5f, 9, 1.5f), // touches the outer's edge at (10, 1)
            };
            AssertTriangulates(Square(10), holes, 100 - 9 - 9 - 0.5);
        }

        [Test]
        public void HolesSharingAnEdge()
        {
            // The ear clipper it replaced reported success here with 89.5 m² instead of 82.
            var holes = new List<IReadOnlyList<Vector2>> { Loop(2, 2, 2, 5, 5, 5, 5, 2), Loop(5, 2, 5, 5, 8, 5, 8, 2) };
            AssertTriangulates(Square(10), holes, 100 - 18);
        }

        [Test]
        public void CollinearRunsAndDuplicatePoints()
        {
            var outer = new List<Vector2>();
            for (var i = 0; i < 40; i++) outer.Add(new Vector2(i * 0.25f, 0));
            for (var i = 0; i < 40; i++) outer.Add(new Vector2(10, i * 0.25f));
            for (var i = 0; i < 40; i++) outer.Add(new Vector2(10 - i * 0.25f, 10));
            for (var i = 0; i < 40; i++) outer.Add(new Vector2(0, 10 - i * 0.25f));
            var hole = new List<Vector2> { new(3, 3), new(3, 5), new(3, 7), new(3, 7), new(7, 7), new(7, 3), new(5, 3) };
            AssertTriangulates(outer, new List<IReadOnlyList<Vector2>> { hole }, 100 - 16);
        }

        [Test]
        public void SliverAndComb()
        {
            AssertTriangulates(new List<Vector2> { new(0, 0), new(100, 0), new(100, 0.01f), new(50, 0.0101f), new(0, 0.01f) },
                new List<IReadOnlyList<Vector2>>(), 1.005);
            var comb = new List<Vector2> { new(0, 0) };
            for (var i = 0; i < 50; i++)
            {
                comb.Add(new Vector2(i * 2 + 1, 0.1f));
                comb.Add(new Vector2(i * 2 + 2, 0));
            }
            comb.Add(new Vector2(100, 5));
            comb.Add(new Vector2(0, 5));
            AssertTriangulates(comb, new List<IReadOnlyList<Vector2>>(), 495);
        }

        [Test]
        public void DegenerateInput()
        {
            var vertices = new List<Vector2>();
            var triangles = new List<int>();
            Assert.IsFalse(PolygonTriangulator.Triangulate(new List<Vector2> { new(0, 0), new(1, 0) },
                new List<IReadOnlyList<Vector2>>(), vertices, triangles));
            Assert.IsTrue(PolygonTriangulator.Triangulate(new List<Vector2> { new(0, 0), new(1, 0), new(2, 0) },
                new List<IReadOnlyList<Vector2>>(), vertices, triangles));
            Assert.AreEqual(0, triangles.Count, "a zero-area outer has no triangles");
            // Clockwise outer, a two-point hole (ignored).
            var cw = Square(10);
            cw.Reverse();
            AssertTriangulates(cw, new List<IReadOnlyList<Vector2>> { Loop(1, 1, 2, 2) }, 100);
            // A hole outside the outer: no valid triangulation.
            Assert.IsFalse(PolygonTriangulator.Triangulate(Square(10), new List<IReadOnlyList<Vector2>> { Loop(20, 20, 20, 22, 22, 22) },
                vertices, triangles));
        }

        [Test]
        public void TracedLoopsCoverEveryCellOnce()
        {
            // Random blobs, traced WITHOUT FillSaddles (loops may share a vertex: touching holes / islands). Every set cell
            // must be covered by exactly one triangle, every unset cell by none.
            var rnd = new System.Random(12345);
            for (var k = 0; k < 400; k++)
            {
                int w = rnd.Next(6, 40), h = rnd.Next(6, 40), block = rnd.Next(2, 6);
                var density = 0.35 + rnd.NextDouble() * 0.5;
                var noise = new double[(w / block + 2) * (h / block + 2)];
                for (var i = 0; i < noise.Length; i++) noise[i] = rnd.NextDouble();
                var m = new BitMask(w, h);
                for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                    m[x, y] = noise[y / block * (w / block + 2) + x / block] < density ^ rnd.NextDouble() < 0.08;

                var loops = new List<List<Vector2>>();
                MaskContours.Trace(m, loops);
                var outers = new List<List<Vector2>>();
                var holesOf = new List<List<IReadOnlyList<Vector2>>>();
                foreach (var l in loops)
                    if (PolygonTriangulator.SignedArea(l) > 0)
                    {
                        outers.Add(l);
                        holesOf.Add(new List<IReadOnlyList<Vector2>>());
                    }
                foreach (var l in loops)
                {
                    if (PolygonTriangulator.SignedArea(l) > 0) continue;
                    // The smallest outer around a point just on the ice side of the hole's first edge (ice is on a loop's left).
                    var d = (l[1] - l[0]).normalized;
                    var probe = (l[0] + l[1]) * 0.5f + new Vector2(-d.y, d.x) * 0.25f;
                    var best = -1;
                    for (var o = 0; o < outers.Count; o++)
                        if (PolygonTriangulator.Contains(outers[o], probe) &&
                            (best < 0 || PolygonTriangulator.SignedArea(outers[o]) < PolygonTriangulator.SignedArea(outers[best])))
                            best = o;
                    holesOf[best].Add(l);
                }

                var cover = new int[w * h];
                for (var o = 0; o < outers.Count; o++)
                {
                    var vertices = new List<Vector2>();
                    var triangles = new List<int>();
                    Assert.IsTrue(PolygonTriangulator.Triangulate(outers[o], holesOf[o], vertices, triangles), $"mask {k}");
                    for (var t = 0; t < triangles.Count; t += 3)
                    {
                        Vector2 a = vertices[triangles[t]], b = vertices[triangles[t + 1]], c = vertices[triangles[t + 2]];
                        Assert.Greater(Cross(a, b, c), 0, $"mask {k}: triangle not counter-clockwise");
                        var min = Vector2.Min(a, Vector2.Min(b, c));
                        var max = Vector2.Max(a, Vector2.Max(b, c));
                        for (var y = (int)min.y; y < (int)max.y; y++)
                        for (var x = (int)min.x; x < (int)max.x; x++)
                            if (Inside(a, b, c, new Vector2(x + 0.5137f, y + 0.4729f))) cover[y * w + x]++;
                    }
                }
                for (var i = 0; i < cover.Length; i++)
                    Assert.AreEqual(m[i % w, i / w] ? 1 : 0, cover[i], $"mask {k}: cell {i % w},{i / w}");
            }
        }

        [Test]
        public void Deterministic()
        {
            var m = Holes(600, 125, 11, 1, 6);
            var a = new Mesh();
            var b = new Mesh();
            MaskMesher.Build(m, 0.25f, Vector2.zero, 0.3f, 0.3f, a);
            MaskMesher.Build(m, 0.25f, Vector2.zero, 0.3f, 0.3f, b);
            CollectionAssert.AreEqual(a.vertices, b.vertices);
            CollectionAssert.AreEqual(a.triangles, b.triangles);
            Object.DestroyImmediate(a);
            Object.DestroyImmediate(b);
        }

        // ---- Helpers ---------------------------------------------------------------------------------------------------

        private static void Fill(BitMask m, int x0, int y0, int w, int h)
        {
            for (var y = y0; y < y0 + h; y++)
            for (var x = x0; x < x0 + w; x++)
                m[x, y] = false;
        }

        private static List<Vector2> Square(float size) => new() { new(0, 0), new(size, 0), new(size, size), new(0, size) };

        private static List<Vector2> Loop(params float[] xy)
        {
            var loop = new List<Vector2>();
            for (var i = 0; i + 1 < xy.Length; i += 2) loop.Add(new Vector2(xy[i], xy[i + 1]));
            return loop;
        }

        private static double Cross(Vector2 a, Vector2 b, Vector2 c) =>
            ((double)b.x - a.x) * ((double)c.y - a.y) - ((double)b.y - a.y) * ((double)c.x - a.x);

        private static bool Inside(Vector2 a, Vector2 b, Vector2 c, Vector2 p) =>
            Cross(a, b, p) > 0 && Cross(b, c, p) > 0 && Cross(c, a, p) > 0;

        // Succeeds, every triangle counter-clockwise with area, their total = `area`.
        private static void AssertTriangulates(List<Vector2> outer, List<IReadOnlyList<Vector2>> holes, double area)
        {
            var vertices = new List<Vector2>();
            var triangles = new List<int>();
            Assert.IsTrue(PolygonTriangulator.Triangulate(outer, holes, vertices, triangles));
            var total = 0.0;
            for (var t = 0; t < triangles.Count; t += 3)
            {
                var c = Cross(vertices[triangles[t]], vertices[triangles[t + 1]], vertices[triangles[t + 2]]);
                Assert.Greater(c, 0, "triangle not counter-clockwise (or zero area)");
                total += c * 0.5;
            }
            Assert.AreEqual(area, total, 1e-4);
        }

        // The slab's top triangles (normal up) face up, have area, and add up to `area`.
        private static void AssertTopCoversArea(Mesh mesh, float area)
        {
            var v = mesh.vertices;
            var n = mesh.normals;
            var t = mesh.triangles;
            var total = 0.0;
            for (var i = 0; i < t.Length; i += 3)
            {
                if (n[t[i]].y < 0.5f) continue;
                var g = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
                Assert.Greater(g.y, 0f, $"top triangle {i / 3} faces down or has no area");
                total += g.y * 0.5;
            }
            Assert.AreEqual(area, total, area * 1e-5);
        }
    }
}
