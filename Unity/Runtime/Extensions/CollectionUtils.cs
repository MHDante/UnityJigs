using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using UnityEngine.Pool;
using UnityJigs.Types;
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

        public static V GetOrAddNew<K, V>(this IDictionary<K, V> dict, K key) where V : new() =>
            dict.TryGetValue(key, out var val) ? val : dict[key] = new();

        public static V GetOrAdd<K, V>(this IDictionary<K, V> dict, K key, Func<V> addFn) =>
            dict.TryGetValue(key, out var val) ? val : dict[key] = addFn();

        public static V GetOrAdd<K, V>(this IDictionary<K, V> dict, K key, V value) =>
            dict.TryGetValue(key, out var val) ? val : dict[key] = value;

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

        public static T? GetSafe<T>(this IReadOnlyList<T>? list, int index, T? defaultVal = default) =>
            list == null || list.Count <= index ? defaultVal : list[index];

        public static T? GetSafe<T>(this IReadOnlyList<T>? list, int? index, T? defaultVal = default) =>
            list == null || index == null || list.Count <= index ? defaultVal : list[index.Value];


        public static void AddRange<T>(this HashSet<T> hashSet, IEnumerable<T> range)
        {
            foreach (T obj in range)
                hashSet.Add(obj);
        }

        /// <summary>
        /// Increases or decrease the number of items in the list to the specified count.
        /// </summary>
        /// <param name="list">The list.</param>
        /// <param name="length">The new length.</param>
        /// <param name="newElement">Value of new elements.</param>
        public static void SetLength<T>(this IList<T> list, int length, Func<T> newElement)
        {
            if (list == null)
                throw new ArgumentNullException(nameof(list));
            if (length < 0)
                throw new ArgumentException("Length must be larger than or equal to 0.");
            if (newElement == null)
                throw new ArgumentNullException(nameof(newElement));
            if (list.GetType().IsArray)
                throw new ArgumentException(
                    "Cannot use the SetLength extension method on an array. Use Array.Resize or the ListUtilities.SetLength(ref IList<T> list, int length) overload.");
            while (list.Count < length)
                list.Add(newElement());
            while (list.Count > length)
                list.RemoveAt(list.Count - 1);
        }


        /// <summary>
        /// Increases or decrease the number of items in the list to the specified count.
        /// </summary>
        /// <param name="list">The list.</param>
        /// <param name="length">The new length.</param>
        public static void SetLength<T>(this IList<T?> list, int length)
        {
            if (list == null)
                throw new ArgumentNullException(nameof(list));
            if (length < 0)
                throw new ArgumentException("Length must be larger than or equal to 0.");
            if (list.GetType().IsArray)
                throw new ArgumentException(
                    "Cannot use the SetLength extension method on an array. Use Array.Resize or the ListUtilities.SetLength(ref IList<T> list, int length) overload.");
            while (list.Count < length)
                list.Add(default);
            while (list.Count > length)
                list.RemoveAt(list.Count - 1);
        }


        public static SerializedSet<T> ToSerializedSet<T>(this IEnumerable<T> list) => new(list);

        public static SerializedDict<TKey, TValue> ToSerializedDict<T, TKey, TValue>(
            this IEnumerable<T> list, Func<T, TKey> keySelector, Func<T, TValue> valueSelector) =>
            SerializedDict<TKey, TValue>.Create(list, keySelector, valueSelector);

        public static SerializedDict<TKey, TValue> ToSerializedDict<TKey, TValue>(
            this IEnumerable<TKey> keys, Func<TKey, TValue> valueSelector) =>
            SerializedDict<TKey, TValue>.Create(keys, valueSelector);

        [MustDisposeResource]
        public static PooledObject<List<T>> FilterPooled<T>(this List<T> source, out List<T> list,
            [Static] Predicate<T> filter)
        {
            var pool = ListPool<T>.Get(out list);
            foreach (var item in source)
                if (filter(item))
                    list.Add(item);
            return pool;
        }

        [MustDisposeResource]
        public static PooledObject<List<T>> FilterPooled<T>(this HashSet<T> source, out List<T> list,
            [Static] Predicate<T> filter)
        {
            var pool = ListPool<T>.Get(out list);
            foreach (var item in source)
                if (filter(item))
                    list.Add(item);
            return pool;
        }

        [MustDisposeResource]
        public static PooledObject<List<TResult>> FilterCastPooled<TSource, TResult>(this HashSet<TSource> source,
            out List<TResult> list)
        {
            var pool = ListPool<TResult>.Get(out list);
            foreach (var item in source)
                if(item is TResult result)
                    list.Add(result);
            return pool;
        }

        [MustDisposeResource]
        public static PooledObject<List<TResult>> FilterCastPooled<TSource, TResult>(this List<TSource> source,
            out List<TResult> list)
        {
            var pool = ListPool<TResult>.Get(out list);
            foreach (var item in source)
                if(item is TResult result)
                    list.Add(result);
            return pool;
        }
    }

    public static class QueuePool<T>
    {
        internal static readonly ObjectPool<Queue<T>> Pool = new(() => new Queue<T>(), actionOnRelease: l => l.Clear());
        public static Queue<T> Get() => Pool.Get();
        public static PooledObject<Queue<T>> Get(out Queue<T> value) => Pool.Get(out value);
        public static void Release(Queue<T> toRelease) => Pool.Release(toRelease);
    }
}
