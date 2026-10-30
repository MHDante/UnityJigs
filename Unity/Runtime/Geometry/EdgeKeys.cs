using System.Collections.Generic;

namespace UnityJigs.Geometry
{
    /// <summary>
    /// The hash for edge keys packed as <c>(long)u &lt;&lt; 32 | v</c> (vertex indices). <c>long.GetHashCode</c> is
    /// <c>u ^ v</c> for those, so the edges of a mesh — (i, i + 1), (i, i + 2)… — fall into a handful of buckets and every
    /// dictionary lookup walks thousands of them (TriangleRefine went quadratic on a long ramp). This mixes the bits first;
    /// lookups only, so the meshes built with it are the same.
    /// </summary>
    internal sealed class EdgeKeys : IEqualityComparer<long>
    {
        public static readonly EdgeKeys Comparer = new();

        public bool Equals(long a, long b) => a == b;

        public int GetHashCode(long key)
        {
            var x = (ulong)key * 0x9E3779B97F4A7C15UL; // Fibonacci hashing: every input bit reaches the high half
            return (int)(x >> 32) ^ (int)x;
        }
    }
}
