using System;
using System.Collections.Generic;
using System.Linq;

namespace MHDante.UnityUtils
{
    public static class EnumUtils<T> where T : unmanaged, Enum
    {
        private static readonly T[] ValueArray = Enum.GetValues(typeof(T)).Cast<T>().ToArray();
        public static IReadOnlyList<T> Values => ValueArray;
        public static List<T> GetValues() => ValueArray.ToList();
    }
}
