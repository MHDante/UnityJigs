using System;
using System.Collections.Generic;
using UnityEngine.Pool;
using Random = UnityEngine.Random;
using Static = JetBrains.Annotations.RequireStaticDelegateAttribute;

namespace UnityUtils.Extensions
{
    public static class CollectionUtils
    {
        public static void RemoveWhere<TKey, TValue>(this Dictionary<TKey, TValue> dict,
            [Static] Predicate<KeyValuePair<TKey, TValue>> predicate)
        {
            using var _ = ListPool<TKey>.Get(out var toRemove);
            foreach (var kvp in dict)
                if (predicate(kvp))
                    toRemove.Add(kvp.Key);
            foreach (var key in toRemove) dict.Remove(key);
        }

        public static T? RemoveLastOrDefault<T>(this IList<T>? list, T? defaultValue = default)
        {
            if (list == null || list.Count == 0) return defaultValue;
            var item = list[^1];
            list.RemoveAt(list.Count - 1);
            return item;
        }

        public static T AddAndGet<T>(this ICollection<T> list, T item)
        {
            list.Add(item);
            return item;
        }

        public static bool AddIfNotNull<T>(this ICollection<T> list, T? item)
        {
            if (item == null) return false;
            list.Add(item);
            return true;
        }

        public static bool IsEmpty<T>(this IEnumerable<T> list)
        {
            if (list is ICollection<T> c) return c.Count == 0;
            if (list is IReadOnlyCollection<T> c2) return c2.Count == 0;
            foreach (var _ in list) return false;
            return true;
        }

        public static bool Intersects<T>(this IReadOnlyList<T> a, IReadOnlyList<T> b)
        {
            using var _ = HashSetPool<T>.Get(out var set);
            for (var i = 0; i < a.Count; i++) set.Add(a[i]);
            for (var i = 0; i < b.Count; i++)
                if (!set.Add(b[i]))
                    return true;
            return false;
        }

        public static bool ContainsBy<T, TKey>(this IReadOnlyList<T> list, TKey key, [Static] Func<T, TKey> selector) =>
            IndexOfBy(list, key, selector) >= 0;

        public static int IndexOfBy<T, TKey>(this IReadOnlyList<T> list, TKey key, [Static] Func<T, TKey> selector)
        {
            for (var i = 0; i < list.Count; i++)
            {
                var item = list[i];
                var isEqual = EqualityComparer<TKey>.Default.Equals(key, selector(item));
                if (isEqual) return i;
            }

            return -1;
        }

        public static T? FindByOrDefault<T, TKey>(this IReadOnlyList<T> list, [Static] Func<T, TKey> selector,
            TKey value)
            where TKey : IEquatable<TKey>
        {
            for (var i = 0; i < list.Count; i++)
            {
                var item = list[i];
                if (selector(item).Equals(value))
                    return item;
            }

            return default;
        }

        public static T? Pick<T>(this IEnumerable<T>? list, Predicate<T>? predicate = null, T? defaultValue = default)
        {
            if (list == null) return defaultValue;
            using var _ = ListPool<T>.Get(out var filtered);
            foreach (var item in list)
                if (predicate == null || predicate(item))
                    filtered.Add(item);
            return filtered.Pick(defaultValue);
        }

        public static T? Pick<T>(this IReadOnlyList<T>? list, T? defaultValue = default)
        {
            if (list == null) return defaultValue;
            if (list.Count == 0) return defaultValue;
            var i = Random.Range(0, list.Count);
            var item = list[i];
            return item;
        }

        public static void Shuffle<T>(this IList<T> list)
        {
            var n = list.Count;
            while (n > 1) {
                n--;
                var k = Random.Range(0, n);
                (list[k], list[n]) = (list[n], list[k]);
            }
        }

        public static string Join(this IEnumerable<string> list, char separator) => string.Join(separator, list);
        public static string Join(this IEnumerable<string> list, string separator) => string.Join(separator, list);
        public static string Join(this List<string> list, char separator) => string.Join(separator, list);
        public static string Join(this List<string> list, string separator) => string.Join(separator, list);
        public static string Join(this string[] list, char separator) => string.Join(separator, list);
        public static string Join(this string[] list, string separator) => string.Join(separator, list);

        public static TValue? GetValueOrDefault<TKey, TValue>(this IDictionary<TKey, TValue> dict,TKey key, TValue? defVal = default) =>
            dict.TryGetValue(key, out var v) ? v : defVal;

    }
}
