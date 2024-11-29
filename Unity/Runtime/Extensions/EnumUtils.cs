using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.Pool;

namespace UnityJigs.Extensions
{
    public static class EnumUtils<T> where T : unmanaged, Enum
    {
        private static readonly T[] ValueArray = Enum.GetValues(typeof(T)).Cast<T>().ToArray();
        public static IReadOnlyList<T> Values => ValueArray;

        public static PooledObject<List<T>> GetPooled(out List<T> list, Func<T, bool>? filter)
        {
            var pooled = ListPool<T>.Get(out list);
            foreach (var element in ValueArray) list.Add(element);
            return pooled;
        }
    }
}
