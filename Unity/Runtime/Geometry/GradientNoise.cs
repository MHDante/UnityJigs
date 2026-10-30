using UnityEngine;

namespace UnityJigs.Geometry
{
    /// <summary>2D gradient (Perlin-style) noise in plain C#: the same values on every platform and thread.</summary>
    public static class GradientNoise
    {
        /// <summary>Smooth noise in about [−1, 1], one feature per unit.</summary>
        public static float Sample(float x, float y)
        {
            var xi = Mathf.FloorToInt(x);
            var yi = Mathf.FloorToInt(y);
            float fx = x - xi, fy = y - yi;
            var u = fx * fx * fx * (fx * (fx * 6f - 15f) + 10f);
            var v = fy * fy * fy * (fy * (fy * 6f - 15f) + 10f);
            var a = Dot(xi, yi, fx, fy);
            var b = Dot(xi + 1, yi, fx - 1f, fy);
            var c = Dot(xi, yi + 1, fx, fy - 1f);
            var d = Dot(xi + 1, yi + 1, fx - 1f, fy - 1f);
            return Mathf.Clamp(1.414f * Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v), -1f, 1f);
        }

        private static float Dot(int x, int y, float dx, float dy)
        {
            var h = (uint)x * 0x8da6b343u ^ (uint)y * 0xd8163841u;
            h ^= h >> 13;
            h *= 0x5bd1e995u;
            h ^= h >> 15;
            var angle = (h & 0xffff) * (Mathf.PI * 2f / 65536f);
            return Mathf.Cos(angle) * dx + Mathf.Sin(angle) * dy;
        }
    }
}
