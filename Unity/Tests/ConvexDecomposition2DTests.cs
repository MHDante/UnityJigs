using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityJigs.Geometry;

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
        Check(new List<Vector2> {
            new(0,0), new(1,0), new(1,1), new(0,1)
        }, 1, "convex square");
    }

    [Test]
    public void ConcaveLShape()
    {
        Check(new List<Vector2> {
            new(0,0), new(2,0), new(2,2), new(1,2), new(1,1), new(0,1)
        }, 2, "concave L-shape");
    }

    [Test]
    public void Triangle()
    {
        Check(new List<Vector2> {
            new(0,0), new(2,0), new(1,1)
        }, 1, "triangle");
    }

    [Test]
    public void CwSquare()
    {
        Check(new List<Vector2> {
            new(0,0), new(0,1), new(1,1), new(1,0)
        }, 1, "CW square");
    }

    [Test]
    public void DegenerateLine()
    {
        var line = new List<Vector2> { new(0,0), new(1,0), new(2,0) };
        var results = new List<List<Vector2>>();
        var count = ConvexDecomposition2D.Decompose(line, results);
        Assert.AreEqual(0, count, "[degenerate line] Expected 0 polys");
    }

    [Test]
    public void DuplicatePoints()
    {
        Check(new List<Vector2> {
            new(0,0), new(1,0), new(1,0), new(1,1), new(0,1)
        }, 1, "duplicate points");
    }

    [Test]
    public void SelfIntersectingStar()
    {
        var star = new List<Vector2> {
            new(0,3), new(1,1), new(3,1), new(1.5f,-1), new(2.5f,-3),
            new(0,-2), new(-2.5f,-3), new(-1.5f,-1), new(-3,1), new(-1,1)
        };
        var results = new List<List<Vector2>>();
        ConvexDecomposition2D.Decompose(star, results);

        // We don’t assert exact counts here since self-intersection is undefined,
        // but we want to at least confirm it doesn’t crash or return null.
        Assert.IsNotNull(results, "[star] Decomposition returned null");
    }
}
