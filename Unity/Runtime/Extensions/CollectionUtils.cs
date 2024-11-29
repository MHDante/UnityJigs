using System;
using System.Collections.Generic;
using UnityEngine.Pool;
using Random = UnityEngine.Random;
using Static = JetBrains.Annotations.RequireStaticDelegateAttribute;

namespace UnityJigs.Extensions
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

        public static int IndexOfBy<T, TKey>(this IReadOnlyList<T> list, TKey key, [Static] Func<T, TKey> selector,
            IEqualityComparer<TKey>? comparer = default)
        {
            comparer ??= EqualityComparer<TKey>.Default;
            for (var i = 0; i < list.Count; i++)
            {
                var item = list[i];
                var isEqual = comparer.Equals(key, selector(item));
                if (isEqual) return i;
            }

            return -1;
        }

        public static T? FindByOrDefault<T, TKey>(this IReadOnlyList<T> list, TKey value,
            [Static] Func<T, TKey> selector, IEqualityComparer<TKey>? comparer = default)
        {
            comparer ??= EqualityComparer<TKey>.Default;
            for (var i = 0; i < list.Count; i++)
            {
                var item = list[i];
                if (comparer.Equals(selector(item), value))
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
            while (n > 1)
            {
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

        public static TValue? GetOrDefault<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key,
            TValue? defVal = default) =>
            dict.TryGetValue(key, out var v) ? v : defVal;


        public static void ForceKeys<K, V>(this IDictionary<K, V> dict, IEnumerable<K> keys,
            Func<K, V>? defaultFactory = null)
        {
            using var set = HashSetPool<K>.Get(out var toRemove);
            foreach (var key in dict.Keys) toRemove.Add(key);
            foreach (var key in keys)
            {
                toRemove.Remove(key);
                if (dict.TryGetValue(key, out _)) continue;
                dict[key] = defaultFactory == null ? default! : defaultFactory(key);
            }

            foreach (var key in toRemove) dict.Remove(key);
        }

        public static IEnumerable<T> Except<T>(this IEnumerable<T> source, T exception,
            IEqualityComparer<T>? comparer = null)
        {
            comparer ??= EqualityComparer<T>.Default;
            foreach (var item in source)
            {
                if (comparer.Equals(item, exception)) continue;
                yield return item;
            }
        }
    }
}
