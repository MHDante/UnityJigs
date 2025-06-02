using System.Collections.Generic;

namespace UnityJigs.Extensions
{
    public static class NullableUtils
    {
        public static bool EqualsNonAlloc<T>(this T? a, T? b) where T : struct
        {
            if (!a.HasValue && !b.HasValue) return true;
            if (!a.HasValue || !b.HasValue) return false;
            return EqualityComparer<T>.Default.Equals(a.Value,b.Value);
        }
        public static bool  EqualsNonAlloc<T>(this T? a, T b) where T : struct
        {
            return a.HasValue && EqualityComparer<T>.Default.Equals(a.Value,b);
        }
    }
}
