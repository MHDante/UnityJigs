using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityJigs.Geometry
{
    /// <summary>
    /// A 2D grid of cells, one byte each, x right, y up: 0 = off, any other value = on. The bool API reads "non-zero"
    /// and writes 1; the byte API (<see cref="GetValue"/>, the painting primitives' byte overloads) keeps the value, so
    /// a mask can carry a small palette (e.g. which style a cell is) while every on/off consumer still works.
    /// Cell (x, y) covers the square [x, x+1] × [y, y+1] in cell space. The painting primitives (disc, polygon, flood
    /// fill) and the canvas resize all work in cell space; callers own the mapping to the world.
    /// </summary>
    [Serializable]
    public class BitMask
    {
        [SerializeField] private int _width;
        [SerializeField] private int _height;
        [SerializeField] private byte[] _bits = Array.Empty<byte>();

        public int Width => _width;
        public int Height => _height;
        public byte[] Bits => _bits;

        public BitMask(int width, int height, bool value = false)
        {
            _width = Mathf.Max(0, width);
            _height = Mathf.Max(0, height);
            _bits = new byte[_width * _height];
            if (value) Fill(true);
        }

        public BitMask(BitMask other)
        {
            _width = other._width;
            _height = other._height;
            _bits = (byte[])other._bits.Clone();
        }

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < _width && y < _height;

        public bool this[int x, int y]
        {
            get => InBounds(x, y) && _bits[y * _width + x] != 0;
            set
            {
                if (InBounds(x, y)) _bits[y * _width + x] = value ? (byte)1 : (byte)0;
            }
        }

        /// <summary>The cell's value (0 out of bounds).</summary>
        public byte GetValue(int x, int y) => InBounds(x, y) ? _bits[y * _width + x] : (byte)0;

        public void SetValue(int x, int y, byte value)
        {
            if (InBounds(x, y)) _bits[y * _width + x] = value;
        }

        /// <summary>The number of on (non-zero) cells.</summary>
        public int Count
        {
            get
            {
                var n = 0;
                foreach (var b in _bits)
                    if (b != 0) n++;
                return n;
            }
        }

        public bool ContentEquals(BitMask other)
        {
            if (other._width != _width || other._height != _height) return false;
            for (var i = 0; i < _bits.Length; i++)
                if (_bits[i] != other._bits[i]) return false;
            return true;
        }

        public void Fill(bool value) => Fill(value ? (byte)1 : (byte)0);

        public void Fill(byte value)
        {
            for (var i = 0; i < _bits.Length; i++) _bits[i] = value;
        }

        /// <summary>Sets every cell whose centre lies within `radius` of `center` (cell space). Returns cells changed.</summary>
        public int FillDisc(Vector2 center, float radius, bool value) => FillDisc(center, radius, value ? (byte)1 : (byte)0);

        public int FillDisc(Vector2 center, float radius, byte v)
        {
            Scratch.Clear();
            CellsInDisc(center, radius, Scratch);
            return Set(Scratch, v);
        }

        /// <summary>The indices (y · Width + x) of the cells whose centre lies within `radius` of `center`.</summary>
        public void CellsInDisc(Vector2 center, float radius, List<int> into)
        {
            var r2 = radius * radius;
            var x0 = Mathf.Max(0, Mathf.FloorToInt(center.x - radius));
            var x1 = Mathf.Min(_width - 1, Mathf.CeilToInt(center.x + radius));
            var y0 = Mathf.Max(0, Mathf.FloorToInt(center.y - radius));
            var y1 = Mathf.Min(_height - 1, Mathf.CeilToInt(center.y + radius));
            for (var y = y0; y <= y1; y++)
            for (var x = x0; x <= x1; x++)
            {
                var dx = x + 0.5f - center.x;
                var dy = y + 0.5f - center.y;
                if (dx * dx + dy * dy <= r2) into.Add(y * _width + x);
            }
        }

        /// <summary>
        /// Sets every cell whose centre lies inside the closed polygon (cell space, even-odd rule; either winding).
        /// Returns cells changed.
        /// </summary>
        public int FillPolygon(IReadOnlyList<Vector2> polygon, bool value) =>
            FillPolygon(polygon, value ? (byte)1 : (byte)0);

        public int FillPolygon(IReadOnlyList<Vector2> polygon, byte v)
        {
            Scratch.Clear();
            CellsInPolygon(polygon, Scratch);
            return Set(Scratch, v);
        }

        /// <summary>The indices of the cells whose centre lies inside the closed polygon (even-odd rule).</summary>
        public void CellsInPolygon(IReadOnlyList<Vector2> polygon, List<int> into)
        {
            if (polygon.Count < 3) return;
            var minY = float.MaxValue;
            var maxY = float.MinValue;
            foreach (var p in polygon)
            {
                minY = Mathf.Min(minY, p.y);
                maxY = Mathf.Max(maxY, p.y);
            }
            var y0 = Mathf.Max(0, Mathf.FloorToInt(minY));
            var y1 = Mathf.Min(_height - 1, Mathf.CeilToInt(maxY));
            var xs = new List<float>();
            for (var y = y0; y <= y1; y++)
            {
                var cy = y + 0.5f;
                xs.Clear();
                for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
                {
                    var a = polygon[j];
                    var b = polygon[i];
                    if ((a.y > cy) == (b.y > cy)) continue;
                    xs.Add(a.x + (cy - a.y) / (b.y - a.y) * (b.x - a.x));
                }
                xs.Sort();
                for (var k = 0; k + 1 < xs.Count; k += 2)
                {
                    // cells whose centre x + 0.5 lies in [xs[k], xs[k+1])
                    var xa = Mathf.Max(0, Mathf.CeilToInt(xs[k] - 0.5f));
                    var xb = Mathf.Min(_width - 1, Mathf.CeilToInt(xs[k + 1] - 0.5f) - 1);
                    for (var x = xa; x <= xb; x++) into.Add(y * _width + x);
                }
            }
        }

        /// <summary>
        /// 4-connected flood fill from (x, y) with `value`: the region of cells holding the start cell's exact value.
        /// Returns cells changed (0 if already `value`).
        /// </summary>
        public int FloodFill(int x, int y, bool value) => FloodFill(x, y, value ? (byte)1 : (byte)0);

        public int FloodFill(int x, int y, byte v)
        {
            if (!InBounds(x, y) || _bits[y * _width + x] == v) return 0;
            Scratch.Clear();
            Region(x, y, Scratch);
            return Set(Scratch, v);
        }

        /// <summary>The indices of the 4-connected region of cells holding (x, y)'s exact value (empty out of bounds).</summary>
        public void Region(int x, int y, List<int> into)
        {
            if (!InBounds(x, y)) return;
            var from = _bits[y * _width + x];
            var seen = new bool[_bits.Length];
            var stack = new Stack<int>();
            stack.Push(y * _width + x);
            while (stack.Count > 0)
            {
                var i = stack.Pop();
                if (seen[i] || _bits[i] != from) continue;
                seen[i] = true;
                into.Add(i);
                var cx = i % _width;
                var cy = i / _width;
                if (cx > 0) stack.Push(i - 1);
                if (cx < _width - 1) stack.Push(i + 1);
                if (cy > 0) stack.Push(i - _width);
                if (cy < _height - 1) stack.Push(i + _width);
            }
        }

        /// <summary>Sets the listed cells (indices) to `v`. Returns cells changed.</summary>
        public int Set(List<int> cells, byte v)
        {
            var changed = 0;
            foreach (var i in cells)
            {
                if (_bits[i] == v) continue;
                _bits[i] = v;
                changed++;
            }
            return changed;
        }

        // The fills' working list (editor-time painting; not thread-safe).
        private static readonly List<int> Scratch = new();

        /// <summary>
        /// Canvas resize: a new mask of `width` × `height` where new cell (x, y) = old cell (x + offsetX, y + offsetY);
        /// cells outside the old mask get `fill`. Content never moves relative to the old origin + offset.
        /// </summary>
        public BitMask Resized(int width, int height, int offsetX, int offsetY, bool fill = false) =>
            Resized(width, height, offsetX, offsetY, fill ? (byte)1 : (byte)0);

        public BitMask Resized(int width, int height, int offsetX, int offsetY, byte fill)
        {
            var result = new BitMask(width, height);
            if (fill != 0) result.Fill(fill);
            for (var y = 0; y < result._height; y++)
            for (var x = 0; x < result._width; x++)
            {
                var ox = x + offsetX;
                var oy = y + offsetY;
                if (InBounds(ox, oy)) result._bits[y * result._width + x] = _bits[oy * _width + ox];
            }
            return result;
        }

        /// <summary>
        /// Removes every saddle (a 2×2 block where exactly two diagonal cells are set) by setting one of the two unset
        /// cells (to the value of the set cell above it), repeating until none remain. After this, traced loops never share a vertex (each is simple and
        /// vertex-disjoint from the others). Returns cells set.
        /// </summary>
        public int FillSaddles()
        {
            var total = 0;
            int changed;
            do
            {
                changed = 0;
                for (var y = 0; y + 1 < _height; y++)
                for (var x = 0; x + 1 < _width; x++)
                {
                    var a = this[x, y];
                    var b = this[x + 1, y];
                    var c = this[x, y + 1];
                    var d = this[x + 1, y + 1];
                    if (a && d && !b && !c) { SetValue(x + 1, y, GetValue(x + 1, y + 1)); changed++; }
                    else if (b && c && !a && !d) { SetValue(x, y, GetValue(x, y + 1)); changed++; }
                }
                total += changed;
            } while (changed > 0);
            return total;
        }

        /// <summary>Copies `other`'s content into this mask (sizes must match).</summary>
        public void CopyFrom(BitMask other)
        {
            if (other._width != _width || other._height != _height)
                throw new ArgumentException("BitMask.CopyFrom: size mismatch");
            Buffer.BlockCopy(other._bits, 0, _bits, 0, _bits.Length);
        }
    }
}
