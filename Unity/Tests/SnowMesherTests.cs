using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityJigs.Geometry;

namespace UnityJigs.Tests
{
    public class SnowMesherTests
    {
        private static readonly SnowProfile[] Profiles = { SnowProfile.Marshmallow, SnowProfile.Mesa, SnowProfile.GameplayOnly };
        private const float Cell = 0.25f;

        // A grid from a painter function (depth m, shape, ground m), then smoothed like a snow field: a box blur of
        // 0.5 m, three passes, mixing only texels within one 0.125 m step, stored in whole centimetres.
        private static SnowGrid Grid(int w, int h, Func<Vector2, (float Depth, byte Profile, float Ground)> paint,
            bool smooth = true)
        {
            var g = new SnowGrid(w, h) { CellSize = Cell, Min = new Vector2(-3.125f, 7.5f) };
            g.Paint = new float[w * h];
            g.Profile = new byte[w * h];
            for (var z = 0; z < h; z++)
            for (var x = 0; x < w; x++)
            {
                var (d, p, gr) = paint(new Vector2((x + 0.5f) * Cell, (z + 0.5f) * Cell));
                g.Paint[z * w + x] = d;
                g.Profile[z * w + x] = p;
                g.Ground[z * w + x] = gr;
            }
            if (!smooth)
            {
                Array.Copy(g.Paint, g.Depth, g.Paint.Length);
                return g;
            }
            var a = (float[])g.Paint.Clone();
            var b = new float[a.Length];
            for (var pass = 0; pass < 3; pass++)
            {
                for (var z = 0; z < h; z++)
                for (var x = 0; x < w; x++)
                {
                    float s = 0, n = 0;
                    for (var k = -2; k <= 2; k++)
                    {
                        var xx = x + k;
                        if (xx < 0 || xx >= w || Mathf.Abs(g.Ground[z * w + xx] - g.Ground[z * w + x]) > 0.126f) continue;
                        s += a[z * w + xx];
                        n++;
                    }
                    b[z * w + x] = s / n;
                }
                for (var z = 0; z < h; z++)
                for (var x = 0; x < w; x++)
                {
                    float s = 0, n = 0;
                    for (var k = -2; k <= 2; k++)
                    {
                        var zz = z + k;
                        if (zz < 0 || zz >= h || Mathf.Abs(g.Ground[zz * w + x] - g.Ground[z * w + x]) > 0.126f) continue;
                        s += b[zz * w + x];
                        n++;
                    }
                    a[z * w + x] = s / n;
                }
            }
            for (var i = 0; i < a.Length; i++) g.Depth[i] = Mathf.Round(a[i] * 100f) * 0.01f;
            return g;
        }

        // Random drifts: discs of 0.2–maxDepth m, some with a deeper core, random shapes, optionally on steps.
        private static SnowGrid Blobs(int w, int h, int seed, float maxDepth, bool shapes, bool steps)
        {
            var random = new System.Random(seed);
            var discs = new List<(Vector2 C, float R, float D, byte P)>();
            for (var i = 0; i < 6 + seed % 7; i++)
                discs.Add((new Vector2((float)random.NextDouble() * w * Cell, (float)random.NextDouble() * h * Cell),
                    1f + (float)random.NextDouble() * 7f, 0.2f + (float)random.NextDouble() * (maxDepth - 0.2f),
                    shapes ? (byte)random.Next(3) : (byte)0));
            var stepX = 2f + (float)random.NextDouble() * (w * Cell - 4f);
            var stepZ = 2f + (float)random.NextDouble() * (h * Cell - 4f);
            return Grid(w, h, p =>
            {
                float d = 0;
                byte pr = 0;
                foreach (var c in discs)
                {
                    if ((p - c.C).magnitude >= c.R) continue;
                    var dd = (p - c.C).magnitude < c.R * 0.4f && c.D > 0.6f ? c.D : Mathf.Min(c.D, 0.35f);
                    if (dd > d)
                    {
                        d = dd;
                        pr = c.P;
                    }
                }
                var ground = 0f;
                if (steps && p.x > stepX) ground += 0.125f; // a one-step rise
                if (steps && p.y > stepZ) ground += 0.25f; // a two-step ledge
                return (d, pr, ground);
            });
        }

        private static float Bilinear(SnowGrid g, Vector2 p)
        {
            var gx = (p.x - g.Min.x) / g.CellSize - 0.5f;
            var gz = (p.y - g.Min.y) / g.CellSize - 0.5f;
            var x0 = Mathf.FloorToInt(gx);
            var z0 = Mathf.FloorToInt(gz);
            float fx = gx - x0, fz = gz - z0;
            float At(int x, int z) => x < 0 || z < 0 || x >= g.Width || z >= g.Height ? 0f : g.Depth[z * g.Width + x];
            return Mathf.Lerp(Mathf.Lerp(At(x0, z0), At(x0 + 1, z0), fx), Mathf.Lerp(At(x0, z0 + 1), At(x0 + 1, z0 + 1), fx), fz);
        }

        private static (Vector2 Min, Vector2 Max) TileRect(SnowMesher m, SnowGrid g, int tx, int tz) =>
            (new Vector2(m.TileLine(tx, g.Min.x, m.TilesX), m.TileLine(tz, g.Min.y, m.TilesZ)),
                new Vector2(m.TileLine(tx + 1, g.Min.x, m.TilesX), m.TileLine(tz + 1, g.Min.y, m.TilesZ)));

        private static bool InsideOutline(SnowMesher m, Vector2 p)
        {
            var inside = false;
            for (var l = 0; l + 1 < m.OutlineStarts.Count; l++)
            {
                int s = m.OutlineStarts[l], e = m.OutlineStarts[l + 1];
                for (int i = s, j = e - 1; i < e; j = i++)
                {
                    Vector2 a = m.OutlinePoints[j], b = m.OutlinePoints[i];
                    if ((a.y > p.y) == (b.y > p.y)) continue;
                    if (p.x < a.x + (p.y - a.y) / (b.y - a.y) * (b.x - a.x)) inside = !inside;
                }
            }
            return inside;
        }

        private static float DistanceToOutline(SnowMesher m, Vector2 p)
        {
            var best = float.MaxValue;
            for (var l = 0; l + 1 < m.OutlineStarts.Count; l++)
            {
                int s = m.OutlineStarts[l], e = m.OutlineStarts[l + 1];
                for (int i = s, j = e - 1; i < e; j = i++)
                    best = Mathf.Min(best, PolygonSimplify.DistanceToSegment(p, m.OutlinePoints[j], m.OutlinePoints[i]));
            }
            return best;
        }

        [Test]
        public void OutlineIsTheDepthContour()
        {
            // Drawn edge = gameplay edge: the field says "snow" (bilinear depth ≥ MinDepth) exactly inside the outline, up
            // to the outline's tolerance (contour points dropped within OutlineTolerance, 1-2-1 smoothing).
            var settings = new SnowMesher.Settings();
            var worst = 0f;
            var samples = 0;
            for (var seed = 0; seed < 8; seed++)
            {
                var g = Blobs(200, 200, seed, 1.2f, false, false);
                var m = new SnowMesher();
                m.Prepare(g, Profiles, settings);
                var random = new System.Random(seed + 100);
                for (var tz = 0; tz < m.TilesZ; tz++)
                for (var tx = 0; tx < m.TilesX; tx++)
                {
                    m.BuildTile(tx, tz);
                    var (mn, mx) = TileRect(m, g, tx, tz);
                    mn = Vector2.Max(mn, g.Min);
                    mx = Vector2.Min(mx, g.Min + new Vector2(g.Width, g.Height) * Cell);
                    for (var k = 0; k < 4000; k++)
                    {
                        var p = new Vector2(Mathf.Lerp(mn.x, mx.x, (float)random.NextDouble()),
                            Mathf.Lerp(mn.y, mx.y, (float)random.NextDouble()));
                        var snow = Bilinear(g, p) >= settings.MinDepth;
                        if (snow == InsideOutline(m, p)) continue;
                        samples++;
                        worst = Mathf.Max(worst, DistanceToOutline(m, p));
                    }
                }
            }
            Debug.Log($"SnowMesher outline vs field: {samples} disagreeing samples, all within {worst * 1000f:F1} mm of the outline");
            Assert.LessOrEqual(worst, settings.OutlineTolerance + 0.025f);
        }

        [Test]
        public void ShapesArePerCell()
        {
            // Mesa | Marshmallow | gameplay only, side by side, 1 m deep.
            var g = Grid(120, 80, p =>
            {
                if (p.y < 5f || p.y >= 15f || p.x < 3f || p.x >= 28f) return (0f, 0, 0f);
                return (1f, p.x < 13f ? (byte)1 : p.x < 23f ? (byte)0 : (byte)2, 0f);
            });
            var m = new SnowMesher();
            m.Prepare(g, Profiles, new SnowMesher.Settings());
            Assert.AreEqual(3, m.Regions, "one drift per shape");
            float downMesa = 0, downMarsh = 0, areaMesa = 0, areaMarsh = 0;
            var beyond = 0;
            for (var tz = 0; tz < m.TilesZ; tz++)
            for (var tx = 0; tx < m.TilesX; tx++)
            {
                m.BuildTile(tx, tz);
                foreach (var v in m.Vertices)
                    if (v.x - g.Min.x > 23.6f)
                        beyond++;
                for (var t = 0; t < m.Triangles.Count; t += 3)
                {
                    Vector3 a = m.Vertices[m.Triangles[t]], b = m.Vertices[m.Triangles[t + 1]], c = m.Vertices[m.Triangles[t + 2]];
                    var n = Vector3.Cross(b - a, c - a);
                    var area = n.magnitude * 0.5f;
                    var x = (a.x + b.x + c.x) / 3f - g.Min.x;
                    var down = n.normalized.y < -0.2f;
                    if (x < 12.5f)
                    {
                        areaMesa += area;
                        if (down) downMesa += area;
                    }
                    else if (x > 13.5f && x < 23f)
                    {
                        areaMarsh += area;
                        if (down) downMarsh += area;
                    }
                }
            }
            Assert.AreEqual(0, beyond, "gameplay-only snow draws nothing (the Marshmallow's lip leans ≤ 0.6 m past its border)");
            Assert.Less(downMesa / areaMesa, 0.005f, "Mesa: no tucked base");
            Assert.Greater(downMarsh / areaMarsh, 0.02f, "Marshmallow: a tucked base (faces looking down)");
            // The two drifts' outlines both run along the texel border between them.
            var border = g.Min.x + 13f;
            var onBorder = 0;
            foreach (var p in m.OutlinePoints)
                if (Mathf.Abs(p.x - border) < 1e-4f)
                    onBorder++;
            Assert.Greater(onBorder, 20, "both outlines run along the shape border");
        }

        [Test]
        public void OneStepMergesTwoStepsSplit()
        {
            SnowGrid Strip(float rise) => Grid(120, 40, p => (p.y > 3f && p.y < 7f && p.x > 3f && p.x < 27f ? 0.3f : 0f, 0,
                p.x > 15f ? rise : 0f));
            var m = new SnowMesher();
            m.Prepare(Strip(0.125f), Profiles, new SnowMesher.Settings());
            Assert.AreEqual(1, m.Regions, "a drift flows over a one-step rise");
            m.Prepare(Strip(0.25f), Profiles, new SnowMesher.Settings());
            Assert.AreEqual(2, m.Regions, "a two-step ledge splits the drift");
            m.BuildTile(0, 0);
            var ledge = Strip(0.25f).Min.x + 15f;
            var onLedge = 0;
            foreach (var p in m.OutlinePoints)
                if (Mathf.Abs(p.x - ledge) < 1e-4f)
                    onLedge++;
            Assert.Greater(onLedge, 6, "both drifts end on the ledge's line");
            // The upper drift stands on the ledge: nothing under its ground past the lower drift's lip (which leans
            // ≤ 0.08 m into the ledge face).
            var grid = Strip(0.25f);
            foreach (var v in m.Vertices)
                if (v.x - grid.Min.x > 15.1f)
                    Assert.GreaterOrEqual(v.y, 0.25f - 1e-4f);
        }

        [Test]
        public void TiersStack()
        {
            SnowGrid Bed() => Grid(80, 80, p =>
            {
                var r = (p - new Vector2(10f, 10f)).magnitude;
                return (r < 2.5f ? 1.2f : r < 7f ? 0.3f : 0f, 0, 0f);
            });
            var m = new SnowMesher();
            var g = Bed();
            m.Prepare(g, Profiles, new SnowMesher.Settings());
            m.BuildTile(0, 0);
            Assert.AreEqual(2, m.Stats.Layers, "0.3 m and 1.2 m paint: two tiers");
            var top = float.MinValue;
            foreach (var v in m.Vertices) top = Mathf.Max(top, v.y);
            Assert.Greater(top, 1.15f);
            Assert.AreEqual(0, m.Stats.Crossings);

            g.Paint = null; // without the paint there are no levels to stack
            m.Prepare(g, Profiles, new SnowMesher.Settings());
            m.BuildTile(0, 0);
            Assert.AreEqual(1, m.Stats.Layers);

            var flat = (SnowProfile[])Profiles.Clone();
            flat[0].TierStep = 0f;
            g = Bed();
            m.Prepare(g, flat, new SnowMesher.Settings());
            m.BuildTile(0, 0);
            Assert.AreEqual(1, m.Stats.Layers, "TierStep 0: no tiers");
        }

        [Test]
        public void NoCrossingsAndEveryCapBuilds()
        {
            for (var seed = 0; seed < 16; seed++)
            {
                var g = Blobs(260, 260, seed, 2.5f, true, seed % 2 == 0);
                var m = new SnowMesher();
                m.Prepare(g, Profiles, new SnowMesher.Settings());
                for (var tz = 0; tz < m.TilesZ; tz++)
                for (var tx = 0; tx < m.TilesX; tx++)
                {
                    m.BuildTile(tx, tz);
                    Assert.AreEqual(0, m.Stats.Crossings, $"seed {seed} tile {tx},{tz}: rings cross");
                    Assert.AreEqual(0, m.Stats.CapFailures, $"seed {seed} tile {tx},{tz}: a top failed to triangulate");
                    for (var t = 0; t < m.Triangles.Count; t++) Assert.Less(m.Triangles[t], m.Vertices.Count);
                }
            }
        }

        [Test]
        public void TileSeamsMatch()
        {
            for (var seed = 0; seed < 6; seed++)
            {
                var g = Blobs(390, 390, seed, 1.6f, true, seed % 2 == 1);
                var m = new SnowMesher();
                m.Prepare(g, Profiles, new SnowMesher.Settings());
                var meshes = new Dictionary<(int, int), (Vector3[] V, int[] T)>();
                for (var tz = 0; tz < m.TilesZ; tz++)
                for (var tx = 0; tx < m.TilesX; tx++)
                {
                    m.BuildTile(tx, tz);
                    meshes[(tx, tz)] = (m.Vertices.ToArray(), m.Triangles.ToArray());
                }
                for (var tz = 0; tz < m.TilesZ; tz++)
                for (var tx = 0; tx < m.TilesX; tx++)
                for (var dir = 0; dir < 2; dir++)
                {
                    var nb = dir == 0 ? (tx + 1, tz) : (tx, tz + 1);
                    if (!meshes.ContainsKey(nb)) continue;
                    var line = dir == 0 ? m.TileLine(tx + 1, g.Min.x, m.TilesX) : m.TileLine(tz + 1, g.Min.y, m.TilesZ);
                    var a = OnLine(meshes[(tx, tz)], dir, line, out var lenA);
                    var b = OnLine(meshes[nb], dir, line, out var lenB);
                    Assert.AreEqual(lenA, lenB, 1e-4, $"seed {seed} line {dir} {line}: covered length");
                    foreach (var p in a) Assert.Less(Nearest(b, p), 1e-5f, $"seed {seed}: {p} has no partner across the line");
                    foreach (var p in b) Assert.Less(Nearest(a, p), 1e-5f, $"seed {seed}: {p} has no partner across the line");
                }
            }
        }

        private static List<Vector3> OnLine((Vector3[] V, int[] T) mesh, int dir, float line, out double length)
        {
            var on = new List<Vector3>();
            length = 0;
            bool On(Vector3 p) => dir == 0 ? p.x == line : p.z == line;
            foreach (var p in mesh.V)
                if (On(p))
                    on.Add(p);
            for (var t = 0; t < mesh.T.Length; t += 3)
            for (var k = 0; k < 3; k++)
            {
                var a = mesh.V[mesh.T[t + k]];
                var b = mesh.V[mesh.T[t + (k + 1) % 3]];
                if (On(a) && On(b)) length += (b - a).magnitude;
            }
            return on;
        }

        private static float Nearest(List<Vector3> set, Vector3 p)
        {
            var best = float.MaxValue;
            foreach (var q in set) best = Mathf.Min(best, (q - p).magnitude);
            return best;
        }

        [Test]
        public void CutsAreExact()
        {
            var g = Blobs(160, 160, 3, 1.4f, true, false);
            var m = new SnowMesher();
            m.Prepare(g, Profiles, new SnowMesher.Settings());
            // A gate region: a rotated 3 m × 40 m quad across the middle.
            var c = g.Min + new Vector2(20f, 20f);
            var along = new Vector2(Mathf.Cos(0.4f), Mathf.Sin(0.4f));
            var across = new Vector2(-along.y, along.x);
            var cut = new[] { c - along * 20f - across * 1.5f, c + along * 20f - across * 1.5f, c + along * 20f + across * 1.5f, c - along * 20f + across * 1.5f };
            var on = 0;
            for (var tz = 0; tz < m.TilesZ; tz++)
            for (var tx = 0; tx < m.TilesX; tx++)
            {
                m.BuildTile(tx, tz, new[] { cut });
                foreach (var v in m.Vertices)
                {
                    var q = new Vector2(v.x, v.z) - c;
                    float u = Vector2.Dot(q, along), w = Vector2.Dot(q, across);
                    var inside = Mathf.Abs(u) < 20f - 1e-3f && Mathf.Abs(w) < 1.5f - 1e-3f;
                    Assert.IsFalse(inside, $"vertex {v} inside the cut");
                    if (Mathf.Abs(Mathf.Abs(w) - 1.5f) < 1e-3f) on++;
                }
            }
            Assert.Greater(on, 50, "the mesh ends ON the cut's lines");
        }

        [Test]
        public void ATileAloneIsTheSameTile()
        {
            var g = Blobs(300, 300, 7, 1.5f, true, true);
            var m = new SnowMesher();
            m.Prepare(g, Profiles, new SnowMesher.Settings());
            m.BuildTile(1, 1);
            var v = m.Vertices.ToArray();
            var t = m.Triangles.ToArray();
            for (var tz = 0; tz < m.TilesZ; tz++)
            for (var tx = 0; tx < m.TilesX; tx++)
                m.BuildTile(tx, tz);
            var other = new SnowMesher();
            other.Prepare(g, Profiles, new SnowMesher.Settings());
            other.BuildTile(1, 1);
            CollectionAssert.AreEqual(v, other.Vertices);
            CollectionAssert.AreEqual(t, other.Triangles);
            m.BuildTile(1, 1);
            CollectionAssert.AreEqual(v, m.Vertices);
            CollectionAssert.AreEqual(t, m.Triangles);
        }

        // A bake that keeps each tile whose key didn't change gives the same meshes, bit for bit, as building every tile
        // again — after a local edit (most tiles kept) and after an edit that reaches a far tile through its drift.
        [Test]
        public void TileKeysFindEveryChangedTile()
        {
            void Check(SnowGrid before, SnowGrid after, string what, out int kept, out int rebuilt)
            {
                var m = new SnowMesher();
                m.Prepare(before, Profiles, new SnowMesher.Settings());
                var keys = new Dictionary<(int, int), ulong>();
                var meshes = new Dictionary<(int, int), (Vector3[] V, int[] T)>();
                for (var tz = 0; tz < m.TilesZ; tz++)
                for (var tx = 0; tx < m.TilesX; tx++)
                {
                    keys[(tx, tz)] = m.TileKey(tx, tz);
                    m.BuildTile(tx, tz);
                    meshes[(tx, tz)] = (m.Vertices.ToArray(), m.Triangles.ToArray());
                    if (keys[(tx, tz)] == 0) Assert.AreEqual(0, m.Triangles.Count, $"{what}: tile {tx},{tz} keyed 0 draws");
                }
                m.Prepare(after, Profiles, new SnowMesher.Settings());
                kept = rebuilt = 0;
                for (var tz = 0; tz < m.TilesZ; tz++)
                for (var tx = 0; tx < m.TilesX; tx++)
                {
                    var key = m.TileKey(tx, tz);
                    m.BuildTile(tx, tz);
                    if (key != keys[(tx, tz)])
                    {
                        rebuilt++;
                        continue;
                    }
                    kept++;
                    CollectionAssert.AreEqual(meshes[(tx, tz)].V, m.Vertices, $"{what}: tile {tx},{tz} kept, vertices changed");
                    CollectionAssert.AreEqual(meshes[(tx, tz)].T, m.Triangles, $"{what}: tile {tx},{tz} kept, triangles changed");
                }
            }

            // A dab: one disc deeper in a corner of a random field.
            for (var seed = 0; seed < 4; seed++)
            {
                var a = Blobs(390, 390, seed, 1.6f, true, seed % 2 == 1);
                var b = Blobs(390, 390, seed, 1.6f, true, seed % 2 == 1);
                for (var z = 20; z < 40; z++)
                for (var x = 20; x < 40; x++)
                {
                    var i = z * b.Width + x;
                    b.Paint![i] = 0.4f;
                    b.Depth[i] = 0.4f;
                }
                Check(a, b, $"dab, seed {seed}", out var kept, out var rebuilt);
                Assert.Greater(kept, 0, $"seed {seed}: a dab kept no tile");
                Assert.Greater(rebuilt, 0, $"seed {seed}: a dab rebuilt no tile");
            }

            // A long drift across three tiles; a deeper end in the last one adds wall rings to the whole drift (its ring
            // count comes from its deepest point), so the first tile's mesh changes too — and its key must.
            SnowGrid Strip(bool deepEnd)
            {
                var g = Grid(300, 60, p =>
                {
                    var d = p.x > 2f && p.x < 73f && p.y > 3f && p.y < 12f ? 0.3f : 0f;
                    if (deepEnd && (p - new Vector2(70f, 7.5f)).magnitude < 2f) d = 0.8f;
                    return (d, (byte)0, 0f);
                }, smooth: false);
                g.Paint = null;
                return g;
            }
            var m0 = new SnowMesher();
            m0.Prepare(Strip(false), Profiles, new SnowMesher.Settings());
            var far0 = m0.TileKey(0, 0);
            m0.BuildTile(0, 0);
            var v0 = m0.Vertices.ToArray();
            m0.Prepare(Strip(true), Profiles, new SnowMesher.Settings());
            Assert.AreNotEqual(far0, m0.TileKey(0, 0), "the far tile's key missed the drift's new depth");
            m0.BuildTile(0, 0);
            Assert.AreNotEqual(v0.Length, m0.Vertices.Count, "the far tile's mesh should change (more wall rings)");
            Check(Strip(false), Strip(true), "deep end", out _, out _);
        }

        [Test]
        public void ADenseTileFitsTheBudget()
        {
            // One 32 m tile full of drifts of both shapes, deep cores (tiers) and steps: the pause-bake budget is 100 ms.
            var g = Blobs(128, 128, 11, 2.0f, true, true);
            var m = new SnowMesher();
            m.Prepare(g, Profiles, new SnowMesher.Settings());
            var times = new List<double>();
            for (var i = 0; i < 5; i++)
            {
                m.BuildTile(0, 0);
                times.Add(m.Stats.Ms);
            }
            times.Sort();
            Debug.Log($"SnowMesher dense tile: {times[2]:F1} ms median, {m.Stats.Triangles} triangles, {m.Stats.Layers} layers");
            Assert.Less(times[2], 100.0);
        }

        [Test]
        public void DelaunayMeshKeepsItsOutline()
        {
            var random = new System.Random(5);
            for (var seed = 0; seed < 20; seed++)
            {
                var outer = new List<Vector2>();
                var n = 30 + seed * 7;
                for (var i = 0; i < n; i++)
                {
                    var a = i * Mathf.PI * 2f / n;
                    var r = 8f + (float)random.NextDouble() * 4f;
                    outer.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
                }
                var hole = new List<Vector2> { new(-2f, -2f), new(-2f, 2f), new(2f, 2f), new(2f, -2f) }; // clockwise
                var tris = new List<int>();
                Assert.IsTrue(PolygonTriangulator.TriangulateIndices(outer, new[] { hole }, tris));
                var points = new List<Vector2>(outer);
                points.AddRange(hole);
                var mesh = new DelaunayMesh();
                mesh.Reset(points, tris);
                mesh.MakeDelaunay();
                for (var x = -12f; x <= 12f; x += 1.3f)
                for (var z = -12f; z <= 12f; z += 1.1f)
                    mesh.Insert(new Vector2(x, z));
                // Every outline edge is still an edge; the area is the polygon's; every triangle counter-clockwise.
                var edges = new HashSet<(int, int)>();
                var area = 0.0;
                for (var t = 0; t < mesh.Triangles.Count; t += 3)
                {
                    int a = mesh.Triangles[t], b = mesh.Triangles[t + 1], c = mesh.Triangles[t + 2];
                    var cross = Cross(mesh.Points[a], mesh.Points[b], mesh.Points[c]);
                    Assert.Greater(cross, 0.0, "counter-clockwise");
                    area += cross * 0.5;
                    edges.Add((a, b));
                    edges.Add((b, c));
                    edges.Add((c, a));
                }
                for (var i = 0; i < outer.Count; i++) Assert.IsTrue(edges.Contains((i, (i + 1) % outer.Count)), $"seed {seed}: outline edge {i}");
                var want = PolygonTriangulator.SignedArea(outer) - 16.0;
                Assert.AreEqual(want, area, 1e-3 * want);
                Assert.Greater(mesh.Points.Count, points.Count + 50, "interior points went in");
            }
        }

        private static double Cross(Vector2 a, Vector2 b, Vector2 c) =>
            ((double)b.x - a.x) * ((double)c.y - a.y) - ((double)b.y - a.y) * ((double)c.x - a.x);

        [Test]
        public void DistanceTransformIsExact()
        {
            var random = new System.Random(9);
            int w = 37, h = 23;
            var mask = new bool[w * h];
            for (var i = 0; i < mask.Length; i++) mask[i] = random.NextDouble() > 0.15;
            var d = new float[w * h];
            DistanceTransform.Distance(mask, true, w, h, d);
            for (var z = 0; z < h; z++)
            for (var x = 0; x < w; x++)
            {
                var want = 0f;
                if (mask[z * w + x])
                {
                    want = float.MaxValue;
                    for (var zz = 0; zz < h; zz++)
                    for (var xx = 0; xx < w; xx++)
                        if (!mask[zz * w + xx])
                            want = Mathf.Min(want, Mathf.Sqrt((x - xx) * (x - xx) + (z - zz) * (z - zz)));
                }
                Assert.AreEqual(want, d[z * w + x], 1e-4f);
            }
        }

        // A new deep tier painted at one end of a long drift: the far tile's key stays, and its mesh would be the same.
        [Test]
        public void ANewTierFarAwayKeepsATilesKey()
        {
            (float, byte, float) Band(Vector2 p) => (p.x > 2f && p.x < 98f && p.y > 2f && p.y < 23f ? 0.3f : 0f, (byte)0, 0f);
            var before = Grid(400, 100, Band);
            var after = Grid(400, 100, p => p.x > 6f && p.x < 10f && p.y > 6f && p.y < 10f ? (2f, (byte)0, 0f) : Band(p));
            var settings = new SnowMesher.Settings();
            var a = new SnowMesher();
            a.Prepare(before, Profiles, settings);
            var b = new SnowMesher();
            b.Prepare(after, Profiles, settings);
            Assert.That(b.TileKey(0, 0), Is.Not.EqualTo(a.TileKey(0, 0)), "the tile it was painted in");
            var far = a.TilesX - 1;
            Assert.That(b.TileKey(far, 0), Is.EqualTo(a.TileKey(far, 0)), "the far tile");
            a.BuildTile(far, 0);
            b.BuildTile(far, 0);
            Assert.That(b.Vertices, Is.EqualTo(a.Vertices));
            Assert.That(b.Normals, Is.EqualTo(a.Normals));
            Assert.That(b.Triangles, Is.EqualTo(a.Triangles));
            // Another mesher sharing what Prepare made: the same mesh.
            var shared = new SnowMesher();
            shared.ShareFrom(b);
            shared.BuildTile(0, 0);
            b.BuildTile(0, 0);
            Assert.That(shared.Vertices, Is.EqualTo(b.Vertices));
            Assert.That(shared.Triangles, Is.EqualTo(b.Triangles));
        }
    }
}
