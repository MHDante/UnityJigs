using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityJigs.Geometry;

namespace UnityJigs.Tests
{
    public class TerraceMesherTests
    {
        // Blobs and holes from a seeded random walk of discs: saddles, islands, holes in islands.
        private static BitMask Blobs(int w, int h, int seed, int discs, float maxRadius)
        {
            var random = new System.Random(seed);
            var m = new BitMask(w, h);
            for (var i = 0; i < discs; i++)
            {
                var c = new Vector2((float)random.NextDouble() * w, (float)random.NextDouble() * h);
                var r = 0.5f + (float)random.NextDouble() * maxRadius;
                m.FillDisc(c, r, random.NextDouble() > 0.3);
            }
            for (var i = 0; i < w * h / 40; i++) m[random.Next(w), random.Next(h)] = random.NextDouble() > 0.5;
            return m;
        }

        private static int[] Labels(BitMask m)
        {
            var l = new int[m.Width * m.Height];
            for (var i = 0; i < l.Length; i++) l[i] = m.Bits[i] != 0 ? 1 : 0;
            return l;
        }

        private static TerraceSlice[]?[] Slab(float thickness) =>
            new[] { null, new[] { new TerraceSlice(-thickness, 0f, 0) } };

        [Test]
        public void OneLabelSlabIsExactlyMaskMeshers()
        {
            for (var seed = 0; seed < 40; seed++)
            {
                var size = 8 + seed * 3;
                var mask = Blobs(size, size + seed % 5, seed, 6 + seed % 9, 2f + seed % 7);
                var origin = new Vector2(-3.25f + seed, 7.5f - seed * 0.25f);
                var thickness = seed % 4 == 0 ? 0f : 0.1f * (1 + seed % 3);
                var old = new Mesh();
                var a = MaskMesher.Build(mask, 0.25f, origin, 0.3f, thickness, old);
                var b = TerraceMesher.Build(Labels(mask), mask.Width, mask.Height, 0.25f, origin, 0.3f, Slab(thickness), 1,
                    out var parts);
                Assert.AreEqual(a.Success, b.Success, $"seed {seed}");
                Assert.AreEqual(a.Roughness, b.Roughness, $"seed {seed}");
                Assert.AreEqual(a.Outers, b.Outers, $"seed {seed}");
                Assert.AreEqual(a.Holes, b.Holes, $"seed {seed}");
                Assert.AreEqual(a.SaddlesFilled, b.SaddlesFilled, $"seed {seed}");
                Assert.AreEqual(a.TopArea, b.TopArea, $"seed {seed}");
                var p = parts[0];
                CollectionAssert.AreEqual(old.vertices, p.Vertices, $"seed {seed} vertices");
                CollectionAssert.AreEqual(old.normals, p.Normals, $"seed {seed} normals");
                CollectionAssert.AreEqual(old.uv, p.Uvs, $"seed {seed} uvs");
                CollectionAssert.AreEqual(old.triangles, p.Triangles, $"seed {seed} triangles");
                Object.DestroyImmediate(old);
            }
        }

        // Two terraces side by side: the border between them is ONE chain, both regions use its simplified points, and
        // only the higher side builds a wall there (over the height difference).
        [Test]
        public void TerracesShareOneBorderAndTheHigherSideBuildsTheStep()
        {
            const int w = 40, h = 30;
            var labels = new int[w * h];
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                labels[y * w + x] = x + (int)(3 * Mathf.Sin(y * 0.4f)) < 20 ? 1 : 2;
            var columns = new TerraceSlice[]?[]
            {
                null,
                new[] { new TerraceSlice(-1f, 0.5f, 0) },
                new[] { new TerraceSlice(-1f, 1f, 0) },
            };
            var graph = TerraceGraph.Trace(labels, w, h, 0.25f, Vector2.zero);
            var r = TerraceMesher.Build(graph, columns, 1, out var parts, new TerraceMesher.Options { Roughness = 0.3f });
            Assert.IsTrue(r.Success);
            var shared = graph.Chains.FindAll(c => c.Left != 0 && c.Right != 0);
            Assert.AreEqual(1, shared.Count, "one border between the two terraces");
            var chain = shared[0];
            Assert.IsFalse(chain.Closed);
            Assert.Greater(chain.Simple.Count, 2, "the wavy border keeps facets");
            foreach (var label in new[] { 1, 2 })
            {
                var loops = graph.Loops(label, simplified: true);
                Assert.AreEqual(1, loops.Count);
                foreach (var p in chain.Simple) Assert.Contains(p, loops[0].Points, $"label {label} uses the shared point");
            }
            // Walls along the shared border: only label 2's, from 0.5 to 1.0.
            var stepWalls = 0;
            var verts = parts[0].Vertices;
            var normals = parts[0].Normals;
            for (var i = 0; i < verts.Count; i += 1)
            {
                if (Mathf.Abs(normals[i].y) > 0.5f) continue; // tops and bottoms
                var onBorder = false;
                foreach (var p in chain.Simple)
                    if (Mathf.Abs(p.x - verts[i].x) < 1e-5f && Mathf.Abs(p.y - verts[i].z) < 1e-5f)
                        onBorder = true;
                if (!onBorder) continue;
                if (Mathf.Abs(verts[i].y - 0.5f) < 1e-5f) stepWalls++;
            }
            Assert.Greater(stepWalls, 0, "the step's foot at 0.5 exists");
            Assert.AreEqual(w * h * 0.0625f, r.TopArea, 0.5f, "the two tops cover the grid (within the simplification)");
        }

        // A random grid of several labels: every region is closed, the regions tile exactly what the outline around the
        // non-empty cells encloses (no gap, no overlap: interior borders are shared), and junctions make open chains.
        [Test]
        public void RegionsTileTheOutlineWithNoCracks()
        {
            for (var seed = 0; seed < 25; seed++)
            {
                var w = 30 + seed;
                var h = 25 + seed % 7;
                var random = new System.Random(seed);
                var labels = new int[w * h];
                for (var layer = 1; layer <= 4; layer++)
                {
                    var m = Blobs(w, h, seed * 7 + layer, 5, 6f);
                    for (var i = 0; i < labels.Length; i++)
                        if (m.Bits[i] != 0 && random.NextDouble() > 0.02)
                            labels[i] = layer;
                }
                var columns = new TerraceSlice[]?[5];
                for (var l = 1; l < 5; l++) columns[l] = new[] { new TerraceSlice(-2f, l * 0.125f, 0) };
                var graph = TerraceGraph.Trace(labels, w, h, 0.25f, new Vector2(1.5f, -2f));
                var r = TerraceMesher.Build(graph, columns, 1, out _, new TerraceMesher.Options { Roughness = 0.3f });
                Assert.IsTrue(r.Success, $"seed {seed}");
                var outline = 0.0;
                foreach (var c in graph.Chains)
                {
                    if (c.Dropped || c.Right != 0) continue;
                    var pts = c.Simple;
                    var n = c.Closed ? pts.Count : pts.Count - 1;
                    for (var i = 0; i < n; i++)
                    {
                        var a = pts[i];
                        var b = pts[(i + 1) % pts.Count];
                        outline += 0.5 * ((double)a.x * b.y - (double)b.x * a.y);
                    }
                }
                Assert.AreEqual(outline, r.TopArea, 1e-2, $"seed {seed}: the regions tile the outline");
                if (seed > 3) Assert.Greater(r.OpenChains, 0, $"seed {seed}: junctions");
            }
        }

        [Test]
        public void SaddlesAreFilledTowardsTheHigherLabel()
        {
            // 2 3
            // 3 2   with 1 around: the 3s are joined across the corner (3 > 2), the 2s are split.
            const int w = 4, h = 4;
            var labels = new int[w * h];
            for (var i = 0; i < labels.Length; i++) labels[i] = 1;
            labels[1 * w + 1] = 3;
            labels[1 * w + 2] = 2;
            labels[2 * w + 1] = 2;
            labels[2 * w + 2] = 3;
            var graph = TerraceGraph.Trace(labels, w, h, 1f, Vector2.zero);
            Assert.AreEqual(1, graph.SaddlesFilled);
            Assert.AreEqual(3, graph.LabelOf(2, 1));
        }

        // Subtracting a region: the cut points are kept exactly through the simplification, and the region is gone.
        [Test]
        public void SubtractCutsOnALineThatSurvivesSimplification()
        {
            const int w = 40, h = 40;
            var labels = new int[w * h];
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                labels[y * w + x] = (x - 20) * (x - 20) + (y - 20) * (y - 20) < 300 ? (x < 22 ? 1 : 2) : 0;
            // Everything with x > 6.0123 m (cells are 0.25 m) goes.
            const float cut = 6.0123f;
            var region = new[] { new Vector2(cut, -10f), new Vector2(50f, -10f), new Vector2(50f, 50f), new Vector2(cut, 50f) };
            var columns = new TerraceSlice[]?[] { null, new[] { new TerraceSlice(-1, 0, 0) }, new[] { new TerraceSlice(-1, 0.25f, 0) } };
            var r = TerraceMesher.Build(labels, w, h, 0.25f, Vector2.zero, 0.3f, columns, 1, out var parts,
                g => Assert.IsTrue(g.Subtract(region)));
            Assert.IsTrue(r.Success);
            var onLine = 0;
            foreach (var v in parts[0].Vertices)
            {
                Assert.LessOrEqual(v.x, cut + 1e-4f, "nothing past the cut");
                if (Mathf.Abs(v.x - cut) < 1e-4f) onLine++;
            }
            Assert.Greater(onLine, 0, "the cut line is in the mesh");
        }

        // A slab floating beside a lower one (the ice pit case): the floating side builds a skirt down to the lower top,
        // so no gap opens under it; with nothing across (the void) no skirt.
        [Test]
        public void SkirtsCloseTheGapDownToALowerNeighbour()
        {
            const int w = 20, h = 10;
            var labels = new int[w * h];
            for (var y = 0; y < h; y++)
            for (var x = 2; x < 18; x++)
                labels[y * w + x] = x < 10 ? 1 : 2;
            var columns = new TerraceSlice[]?[]
            {
                null,
                new[]
                {
                    new TerraceSlice(-0.1f, 0f, 0), // a slab over water at 0
                    new TerraceSlice(0f, -0.1f, 1, TerraceFaces.Walls, flags: TerraceSliceFlags.Skirt),
                },
                new[] { new TerraceSlice(-1.1f, -1f, 0) }, // the pit's slab at −1
            };
            var r = TerraceMesher.Build(labels, w, h, 0.25f, Vector2.zero, 0.3f, columns, 2, out var parts);
            Assert.IsTrue(r.Success);
            var skirt = parts[1];
            Assert.IsFalse(skirt.IsEmpty, "a skirt along the pit");
            var minY = float.MaxValue;
            var maxY = float.MinValue;
            foreach (var v in skirt.Vertices)
            {
                minY = Mathf.Min(minY, v.y);
                maxY = Mathf.Max(maxY, v.y);
                Assert.AreEqual(2.5f, v.x, 1e-4f, "only along the border with the pit (x = 10 cells), never the void");
            }
            Assert.AreEqual(-1f, minY, 1e-5f, "down to the pit's top");
            Assert.AreEqual(-0.1f, maxY, 1e-5f, "up to the slab's bottom");
        }

        // A lifted (chamfered) band: its top meets the lower terrace on the shared border, the riser there is gone, and
        // the walls taper where the band ends.
        [Test]
        public void LiftedBandMeetsTheLowerTerraceWithNoRiser()
        {
            const int w = 24, h = 16;
            var labels = new int[w * h];
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                labels[y * w + x] = x < 8 ? 1 : x < 12 && y >= 4 && y < 12 ? 3 : 2; // 1 low, 2 high, 3 = the band in 2
            var columns = new TerraceSlice[]?[]
            {
                null,
                new[] { new TerraceSlice(-1f, 0f, 0, flags: TerraceSliceFlags.LiftTop) },
                new[] { new TerraceSlice(-1f, 0.125f, 0, flags: TerraceSliceFlags.LiftTop) },
                new[] { new TerraceSlice(-1f, 0.125f, 0, flags: TerraceSliceFlags.LiftTop) },
            };
            // The band (label 3) ramps from 0 at x = 2 m (the step) up to 0.125 at x = 3 m.
            float Lift(int label, Vector2 p) => label == 3 ? -0.125f * (1f - Mathf.Clamp01(p.x - 2f)) : 0f;
            var r = TerraceMesher.Build(TerraceGraph.Trace(labels, w, h, 0.25f, Vector2.zero), columns, 1, out var parts,
                new TerraceMesher.Options { Roughness = 0.3f, Lift = Lift });
            Assert.IsTrue(r.Success);
            var p0 = parts[0];
            // Every vertex on the step line inside the band (x = 2, 1 < z < 3) is at the low top: no riser there.
            for (var i = 0; i < p0.Vertices.Count; i++)
            {
                var v = p0.Vertices[i];
                if (Mathf.Abs(v.x - 2f) > 1e-4f || v.z <= 1f + 1e-4f || v.z >= 3f - 1e-4f) continue;
                if (v.y > -0.5f) Assert.AreEqual(0f, v.y, 1e-5f, $"band edge vertex {v}");
            }
            // The band's top is sloped: its normals lean toward −x.
            var leaning = 0;
            for (var i = 0; i < p0.Normals.Count; i++)
                if (p0.Normals[i].y > 0.5f && p0.Normals[i].x < -0.05f) leaning++;
            Assert.Greater(leaning, 0, "a visible bevel");
        }

        [Test]
        public void LongEdgesAreSplitWhereAsked()
        {
            const int w = 200, h = 8;
            var labels = new int[w * h];
            for (var i = 0; i < labels.Length; i++) labels[i] = 1;
            var columns = new TerraceSlice[]?[] { null, new[] { new TerraceSlice(-3f, 0f, 0) } };
            var r = TerraceMesher.Build(TerraceGraph.Trace(labels, w, h, 0.25f, Vector2.zero), columns, 1, out var parts,
                new TerraceMesher.Options { Roughness = 0.3f, MaxEdge = (_, _) => 10f });
            Assert.IsTrue(r.Success);
            var verts = parts[0].Vertices;
            for (var t = 0; t < parts[0].Triangles.Count; t += 3)
            {
                var a = verts[parts[0].Triangles[t]];
                var b = verts[parts[0].Triangles[t + 1]];
                var c = verts[parts[0].Triangles[t + 2]];
                if (Mathf.Abs(a.y - b.y) < 1e-6f && Mathf.Abs(a.y - c.y) < 1e-6f) continue; // tops and bottoms
                Assert.LessOrEqual(Mathf.Max((a - b).magnitude, (b - c).magnitude, (c - a).magnitude), Mathf.Sqrt(100f + 9f) + 1e-3f);
            }
        }

        [Test]
        public void PartsBecomeSubmeshes()
        {
            const int w = 12, h = 12;
            var labels = new int[w * h];
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                labels[y * w + x] = x < 6 ? 1 : 2;
            var columns = new TerraceSlice[]?[]
                { null, new[] { new TerraceSlice(-1f, 0f, 0) }, new[] { new TerraceSlice(-1f, 0.5f, 1) } };
            var r = TerraceMesher.Build(TerraceGraph.Trace(labels, w, h, 0.25f, Vector2.zero), columns, 2, out var parts,
                new TerraceMesher.Options
                {
                    Roughness = 0.3f, PartHasUv12 = _ => true, Uv1Offset = new Vector3(100, 0, 0),
                    Uv2 = c => new Vector4((int)c.Face, c.Label, c.Across, 0),
                });
            Assert.IsTrue(r.Success);
            var mesh = new Mesh();
            TerraceMesher.WriteSubmeshes(mesh, parts);
            Assert.AreEqual(2, mesh.subMeshCount);
            Assert.AreEqual(parts[0].Vertices.Count + parts[1].Vertices.Count, mesh.vertexCount);
            var uv1 = new List<Vector4>();
            mesh.GetUVs(1, uv1);
            Assert.AreEqual(mesh.vertices[0].x + 100f, uv1[0].x, 1e-4f);
            Object.DestroyImmediate(mesh);
        }
    }
}
