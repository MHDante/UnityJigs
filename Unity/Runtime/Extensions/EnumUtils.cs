using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.Pool;

namespace UnityJigs.Extensions
{
    public static class EnumUtils<T> where T : unmanaged, Enum
    {
        private static readonly T[] ValueArray = Enum.GetValues(typeof(T)).Cast<T>().ToArray();
        private static readonly string[] NameArray = Enum.GetNames(typeof(T)).ToArray();
        public static IReadOnlyList<T> Values => ValueArray;
        public static readonly string Name = typeof(T).Name;

        public static PooledObject<List<T>> GetPooled(out List<T> list, Func<T, bool>? filter)
        {
            var pooled = ListPool<T>.Get(out list);
            foreach (var element in ValueArray) list.Add(element);
            return pooled;
        }

        public static string GetName(T value)
        {
            var index = Array.IndexOf(ValueArray, value);
            if (index < 0) return value.ToString();
            return NameArray[index];
        }
    }

    public static class EnumUtils
    {
        public static string GetName<T>(this T value) where T : unmanaged, Enum => EnumUtils<T>.GetName(value);
    }
}
