using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
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
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool HasFlagUnsafe<TEnum>(this TEnum lhs, TEnum rhs) where TEnum : unmanaged, Enum
        {
            unsafe
            {
                switch (sizeof(TEnum))
                {
                    case 1:
                        return (*(byte*)&lhs & *(byte*)&rhs) > 0;
                    case 2:
                        return (*(ushort*)&lhs & *(ushort*)&rhs) > 0;
                    case 4:
                        return (*(uint*)&lhs & *(uint*)&rhs) > 0;
                    case 8:
                        return (*(ulong*)&lhs & *(ulong*)&rhs) > 0;
                    default:
                        throw new Exception("Size does not match a known Enum backing type.");
                }
            }
        }
    }
}
