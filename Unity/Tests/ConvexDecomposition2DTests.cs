using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityJigs.Extensions;

namespace UnityJigs.Tests
{
    public class ConvexDecomposition2DTests
    {
        void Check(List<Vector2> input, int minOut, string label)
        {
            var results = new List<List<Vector2>>();
            var count = ConvexDecomposition2D.Decompose(input, results);

            Assert.GreaterOrEqual(count, minOut, $"[{label}] Expected ≥ {minOut} convex polys, got {count}");

            foreach (var poly in results)
                Assert.GreaterOrEqual(poly.Count, 3, $"[{label}] Convex poly has <3 points");
        }

        [Test]
        public void ConvexSquare()
        {
            Check(new List<Vector2>
            {
                new(0, 0), new(1, 0), new(1, 1), new(0, 1)
            }, 1, "convex square");
        }

        [Test]
        public void ConcaveLShape()
        {
            Check(new List<Vector2>
            {
                new(0, 0), new(2, 0), new(2, 2), new(1, 2), new(1, 1), new(0, 1)
            }, 2, "concave L-shape");
        }

        [Test]
        public void Triangle()
        {
            Check(new List<Vector2>
            {
                new(0, 0), new(2, 0), new(1, 1)
            }, 1, "triangle");
        }

        [Test]
        public void CwSquare()
        {
            Check(new List<Vector2>
            {
                new(0, 0), new(0, 1), new(1, 1), new(1, 0)
            }, 1, "CW square");
        }

        [Test]
        public void DegenerateLine()
        {
            var line = new List<Vector2> { new(0, 0), new(1, 0), new(2, 0) };
            var results = new List<List<Vector2>>();
            var count = ConvexDecomposition2D.Decompose(line, results);
            Assert.AreEqual(0, count, "[degenerate line] Expected 0 polys");
        }

        [Test]
        public void DuplicatePoints()
        {
            Check(new List<Vector2>
            {
                new(0, 0), new(1, 0), new(1, 0), new(1, 1), new(0, 1)
            }, 1, "duplicate points");
        }

        [Test]
        public void SelfIntersectingStar()
        {
            var star = new List<Vector2>
            {
                new(0, 3), new(1, 1), new(3, 1), new(1.5f, -1), new(2.5f, -3),
                new(0, -2), new(-2.5f, -3), new(-1.5f, -1), new(-3, 1), new(-1, 1)
            };
            var results = new List<List<Vector2>>();
            ConvexDecomposition2D.Decompose(star, results);

            // We don’t assert exact counts here since self-intersection is undefined,
            // but we want to at least confirm it doesn’t crash or return null.
            Assert.IsNotNull(results, "[star] Decomposition returned null");
        }

        [Test]
        public void DegeneratePolyShape_FailsOrProducesValidPieces()
        {
            var poly = new List<Vector2>
            {
                new(5.099777f, -6.210258f),
                new(-5.44101f, -27.96449f),
                new(-49.63548f, -18.46001f),
                new(-15.50814f, -13.09025f),
                new(-51.37621f, -2.944122f),
                new(-28.35718f, 9.326164f),
                new(-39.79032f, 25.35439f),
                new(-40.83625f, 45.248f),
                new(-27.71294f, 51.7504f),
                new(-4.730133f, 48.18953f),
                new(8.784924f, 41.20758f),
                new(15.15331f, 22.27672f),
                new(3.038517f, 23.30598f),
                new(-10.24511f, 25.02667f),
                new(-22.16779f, 23.67555f),
                new(-16.7002f, 13.5687f),
                new(3.298359f, 7.648537f),
                new(11.96991f, 3.965744f),
            };

            var results = new List<List<Vector2>>();
            var count = ConvexDecomposition2D.Decompose(poly, results);

            Debug.Log($"Decomposed into {count} convex pieces.");
            for (int i = 0; i < results.Count; i++)
                Debug.Log($"Piece {i}: {results[i].Count} verts, area={PolyUtils.SignedArea(results[i])}");

            // Sanity checks
            Assert.Greater(count, 0, "Expected at least one convex piece.");

            foreach (var piece in results)
            {
                Assert.GreaterOrEqual(piece.Count, 3, "Piece has too few vertices.");
                Assert.IsTrue(PolyUtils.IsConvex(piece), "Piece is not convex.");
                Assert.AreNotEqual(0f, PolyUtils.SignedArea(piece), "Piece has zero area.");
            }
        }

        [Test]
        public void CleanPolyShape_DecomposesIntoValidConvexPieces()
        {
            var poly = new List<Vector2>
            {
                new(5.099777f, -6.210258f),
                new(-5.44101f, -27.96449f),
                new(-49.63548f, -18.46001f),
                new(-23.97111f, 20.33735f),
                new(-60.08826f, 12.22004f),
                new(-52.31614f, 33.84326f),
                new(-25.69953f, 37.53798f),
            };

            var results = new List<List<Vector2>>();
            var count = ConvexDecomposition2D.Decompose(poly, results);

            Assert.Greater(count, 0, "Expected at least one convex piece.");

            var originalArea = Mathf.Abs(PolyUtils.SignedArea(poly));
            var sumPieces = 0f;

            foreach (var piece in results)
            {
                Assert.GreaterOrEqual(piece.Count, 3, "Piece has too few vertices.");
                Assert.IsTrue(PolyUtils.IsConvex(piece), "Piece is not convex.");
                var a = PolyUtils.SignedArea(piece);
                Assert.AreNotEqual(0f, a, "Piece has zero area.");
                sumPieces += Mathf.Abs(a);
            }

            Assert.That(sumPieces, Is.EqualTo(originalArea).Within(1e-3f),
                "Sum of convex piece areas does not match original polygon area.");
        }
    }
}
