using System;
using UnityEngine;

namespace UnityJigs.Geometry
{
    /// <summary>
    /// Exact Euclidean distance transform of a boolean grid (Felzenszwalb–Huttenlocher, two 1D passes), in nodes:
    /// linear in the grid's size.
    /// </summary>
    public static class DistanceTransform
    {
        [ThreadStatic] private static double[]? _f, _d, _z;
        [ThreadStatic] private static int[]? _v;
        [ThreadStatic] private static double[]? _col;

        /// <summary>
        /// For every node where `mask` equals `of`: the distance (in nodes) to the nearest node where it doesn't (1e6 when
        /// there is none); 0 elsewhere. Writes `width × height` values into `distance`.
        /// </summary>
        public static void Distance(bool[] mask, bool of, int width, int height, float[] distance)
        {
            // Doubles: squared distances are whole numbers, exact here whatever the grid's extent (so two overlapping grids
            // agree wherever they see the same sites).
            const double inf = 1e20;
            var n = width * height;
            var m = Mathf.Max(width, height);
            if (_f == null || _f.Length < m || _col == null || _col.Length < n)
            {
                _f = new double[m];
                _d = new double[m];
                _z = new double[m + 1];
                _v = new int[m];
                _col = new double[Mathf.Max(n, 1)];
            }
            for (var i = 0; i < n; i++) _col[i] = mask[i] == of ? inf : 0.0;
            for (var x = 0; x < width; x++)
            {
                for (var y = 0; y < height; y++) _f[y] = _col[y * width + x];
                Pass(_f, height, _d!, _v!, _z!);
                for (var y = 0; y < height; y++) _col[y * width + x] = _d![y];
            }
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++) _f[x] = _col[y * width + x];
                Pass(_f, width, _d!, _v!, _z!);
                for (var x = 0; x < width; x++) _col[y * width + x] = _d![x];
            }
            for (var i = 0; i < n; i++) distance[i] = _col[i] >= inf * 0.5 ? 1e6f : (float)Math.Sqrt(_col[i]);
        }

        // The lower envelope of the parabolas (q − p)² + f(p).
        private static void Pass(double[] f, int n, double[] d, int[] v, double[] z)
        {
            var k = 0;
            v[0] = 0;
            z[0] = double.NegativeInfinity;
            z[1] = double.PositiveInfinity;
            for (var q = 1; q < n; q++)
            {
                var s = (f[q] + (double)q * q - (f[v[k]] + (double)v[k] * v[k])) / (2.0 * q - 2.0 * v[k]);
                while (s <= z[k])
                {
                    k--;
                    s = (f[q] + (double)q * q - (f[v[k]] + (double)v[k] * v[k])) / (2.0 * q - 2.0 * v[k]);
                }
                k++;
                v[k] = q;
                z[k] = s;
                z[k + 1] = double.PositiveInfinity;
            }
            k = 0;
            for (var q = 0; q < n; q++)
            {
                while (z[k + 1] < q) k++;
                var dq = q - v[k];
                d[q] = (double)dq * dq + f[v[k]];
            }
        }
    }
}
