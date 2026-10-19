using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityJigs.Geometry;

namespace UnityJigs.Tests
{
    public class MaskMesherTests
    {
        private static BitMask Rect(int w, int h, int x0, int y0, int x1, int y1, bool value = true, BitMask? into = null)
        {
            var m = into ?? new BitMask(w, h);
            for (var y = y0; y < y1; y++)
            for (var x = x0; x < x1; x++)
                m[x, y] = value;
            return m;
        }

        private static List<List<Vector2>> Trace(BitMask m)
        {
            var loops = new List<List<Vector2>>();
            MaskContours.Trace(m, loops);
            return loops;
        }

        [Test]
        public void SquareTracesOneCounterClockwiseLoopOfFourCorners()
        {
            var loops = Trace(Rect(6, 6, 1, 1, 4, 4));
            Assert.AreEqual(1, loops.Count);
            Assert.AreEqual(4, loops[0].Count);
            Assert.AreEqual(9f, PolygonTriangulator.SignedArea(loops[0]), 1e-5f);
        }

        [Test]
        public void RingTracesOuterAndClockwiseHole()
        {
            var m = Rect(10, 10, 1, 1, 9, 9);
            Rect(10, 10, 3, 3, 7, 7, false, m);
            var loops = Trace(m);
            Assert.AreEqual(2, loops.Count);
            var areas = new List<float> { PolygonTriangulator.SignedArea(loops[0]), PolygonTriangulator.SignedArea(loops[1]) };
            areas.Sort();
            Assert.AreEqual(-16f, areas[0], 1e-5f);
            Assert.AreEqual(64f, areas[1], 1e-5f);
        }

        [Test]
        public void DiagonalCellsAreSeparateLoops()
        {
            var m = new BitMask(4, 4);
            m[1, 1] = true;
            m[2, 2] = true;
            var loops = Trace(m);
            Assert.AreEqual(2, loops.Count);
            Assert.AreEqual(1, new BitMask(m).FillSaddles());
        }

        [Test]
        public void BorderTouchingMaskTraces()
        {
            var loops = Trace(new BitMask(5, 3, true));
            Assert.AreEqual(1, loops.Count);
            Assert.AreEqual(15f, PolygonTriangulator.SignedArea(loops[0]), 1e-5f);
        }

        [Test]
        public void RingSlabHasUpTopDownBottomAndArea()
        {
            var m = Rect(40, 40, 0, 0, 40, 40);
            Rect(40, 40, 12, 12, 28, 28, false, m);
            var mesh = new Mesh();
            var r = MaskMesher.Build(m, 0.25f, Vector2.zero, 0.3f, 0.3f, mesh);
            Assert.IsTrue(r.Success);
            Assert.AreEqual(1, r.Outers);
            Assert.AreEqual(1, r.Holes);
            Assert.AreEqual(100f - 16f, r.TopArea, 1e-3f);
            AssertWindingMatchesNormals(mesh);
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void IslandInsideHoleTriangulates()
        {
            var m = Rect(30, 30, 0, 0, 30, 30);
            Rect(30, 30, 5, 5, 25, 25, false, m);
            Rect(30, 30, 12, 12, 18, 18, true, m);
            var mesh = new Mesh();
            var r = MaskMesher.Build(m, 1f, Vector2.zero, 0.5f, 0.3f, mesh);
            Assert.IsTrue(r.Success);
            Assert.AreEqual(2, r.Outers);
            Assert.AreEqual(1, r.Holes);
            Assert.AreEqual(900f - 400f + 36f, r.TopArea, 1e-3f);
            AssertWindingMatchesNormals(mesh);
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void BlobWithManyHolesStaysWithinRoughness()
        {
            var m = new BitMask(120, 90);
            m.FillDisc(new Vector2(60, 45), 40, true);
            m.FillDisc(new Vector2(40, 45), 8, false);
            m.FillDisc(new Vector2(75, 30), 6, false);
            m.FillDisc(new Vector2(78, 60), 10, false);
            m.FillPolygon(new List<Vector2> { new(55, 70), new(65, 72), new(60, 80) }, false);
            var mesh = new Mesh();
            var r = MaskMesher.Build(m, 0.25f, Vector2.zero, 0.3f, 0.3f, mesh);
            Assert.IsTrue(r.Success);
            Assert.AreEqual(1, r.Outers);
            Assert.AreEqual(4, r.Holes);
            // Faceted outline area stays close to the painted area (cells × 0.0625 m²).
            var painted = m.Count * 0.0625f;
            Assert.AreEqual(painted, r.TopArea, painted * 0.03f);
            AssertWindingMatchesNormals(mesh);
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void SimplifyKeepsPointsWithinEpsilon()
        {
            var loop = new List<Vector2>();
            for (var i = 0; i < 64; i++)
            {
                var a = i / 64f * Mathf.PI * 2f;
                loop.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 10f);
            }
            var simple = new List<Vector2>();
            PolygonSimplify.SimplifyClosed(loop, 0.2f, simple);
            Assert.Less(simple.Count, loop.Count);
            foreach (var p in loop)
            {
                var best = float.MaxValue;
                for (var i = 0; i < simple.Count; i++)
                    best = Mathf.Min(best, PolygonSimplify.DistanceToSegment(p, simple[i], simple[(i + 1) % simple.Count]));
                Assert.LessOrEqual(best, 0.2f + 1e-4f);
            }
        }

        [Test]
        public void BrushAndBucketAndResize()
        {
            var m = new BitMask(20, 20);
            Assert.Greater(m.FillDisc(new Vector2(10, 10), 3, true), 0);
            var hole = m.Count;
            m.FillDisc(new Vector2(10, 10), 1, false);
            Assert.AreEqual(400 - hole, new BitMask(m).FloodFill(0, 0, true));
            var grown = m.Resized(24, 24, -2, -2);
            Assert.AreEqual(m.Count, grown.Count);
            Assert.IsTrue(grown[12, 12] == m[10, 10]);
        }

        [Test]
        public void PaletteValues()
        {
            var m = new BitMask(10, 10, true);
            m.FillDisc(new Vector2(3, 3), 2, (byte)5);
            var fives = 0;
            foreach (var b in m.Bits) if (b == 5) fives++;
            Assert.Greater(fives, 0);
            Assert.AreEqual(100, m.Count); // every cell is on, whatever its value
            Assert.IsTrue(m[3, 3]);
            Assert.AreEqual(5, m.GetValue(3, 3));
            // The bucket fills the start cell's exact value only: the 1-region around the 5s.
            Assert.AreEqual(100 - fives, m.FloodFill(9, 9, (byte)7));
            Assert.AreEqual(5, m.GetValue(3, 3));
            Assert.AreEqual(7, m.Resized(12, 12, -1, -1, (byte)2).GetValue(10, 10));
            Assert.AreEqual(2, m.Resized(12, 12, -1, -1, (byte)2).GetValue(0, 0));
        }

        // Every triangle's geometric normal (Unity: cross(b−a, c−a)) must agree with its vertex normal.
        private static void AssertWindingMatchesNormals(Mesh mesh)
        {
            var v = mesh.vertices;
            var n = mesh.normals;
            var t = mesh.triangles;
            Assert.Greater(t.Length, 0);
            for (var i = 0; i < t.Length; i += 3)
            {
                var g = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
                if (g.sqrMagnitude < 1e-12f) continue;
                Assert.Greater(Vector3.Dot(g, n[t[i]]), 0f, $"triangle {i / 3} faces against its normal");
            }
        }
    }
}
