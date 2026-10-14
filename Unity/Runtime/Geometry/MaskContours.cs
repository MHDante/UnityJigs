using System.Collections.Generic;
using UnityEngine;

namespace UnityJigs.Geometry
{
    /// <summary>
    /// Traces the boundaries of a <see cref="BitMask"/> into closed loops of cell-corner points (cell space). Every
    /// loop keeps the set cells on its LEFT, so outer boundaries wind counter-clockwise (positive area) and holes
    /// clockwise (negative area). Regions are 4-connected: two cells touching only at a corner trace as separate
    /// loops (at such a saddle the trace turns left, hugging the region it is walking). A loop holds only its
    /// turning points (no collinear corners). Loops are simple; two loops may share a saddle vertex.
    /// </summary>
    public static class MaskContours
    {
        // Directions: 0 = +x, 1 = +y, 2 = -x, 3 = -y (counter-clockwise order: left of d is (d + 1) & 3).
        private static readonly int[] Dx = { 1, 0, -1, 0 };
        private static readonly int[] Dy = { 0, 1, 0, -1 };

        public static void Trace(BitMask mask, List<List<Vector2>> loops)
        {
            var w = mask.Width;
            var h = mask.Height;
            var stride = w + 1;
            // Outgoing boundary edges per corner vertex, as a 4-bit set of directions.
            var outEdges = new byte[stride * (h + 1)];
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                if (!mask[x, y]) continue;
                // Each side of a set cell facing an unset cell (or the border) is an edge walked with the cell on the left.
                if (!mask[x, y - 1]) outEdges[y * stride + x] |= 1 << 0; // bottom: (x,y) → (x+1,y)
                if (!mask[x + 1, y]) outEdges[y * stride + x + 1] |= 1 << 1; // right: (x+1,y) → (x+1,y+1)
                if (!mask[x, y + 1]) outEdges[(y + 1) * stride + x + 1] |= 1 << 2; // top: (x+1,y+1) → (x,y+1)
                if (!mask[x - 1, y]) outEdges[(y + 1) * stride + x] |= 1 << 3; // left: (x,y+1) → (x,y)
            }

            for (var start = 0; start < outEdges.Length; start++)
            {
                while (outEdges[start] != 0)
                {
                    var d0 = FirstBit(outEdges[start]);
                    var loop = new List<Vector2>();
                    var v = start;
                    var dir = d0;
                    while (true)
                    {
                        outEdges[v] &= (byte)~(1 << dir);
                        v = (v / stride + Dy[dir]) * stride + v % stride + Dx[dir];
                        // At the start vertex the first edge counts as available: choosing it closes the loop.
                        var avail = outEdges[v];
                        if (v == start) avail |= (byte)(1 << d0);
                        var next = Choose(avail, dir);
                        if (next < 0) break; // malformed boundary — cannot happen for a BitMask
                        if (v == start && next == d0)
                        {
                            if (dir != d0) loop.Insert(0, Corner(start, stride));
                            break;
                        }
                        if (next != dir) loop.Add(Corner(v, stride));
                        dir = next;
                    }
                    if (loop.Count >= 3) loops.Add(loop);
                }
            }
        }

        private static Vector2 Corner(int v, int stride) => new(v % stride, v / stride);

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
    }
}
