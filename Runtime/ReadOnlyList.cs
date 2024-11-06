using System;
using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using Static = JetBrains.Annotations.RequireStaticDelegateAttribute;

namespace MHDante.UnityUtils
{
    [SimpleContainer]
    public readonly struct ReadOnlyList<T> //: IReadOnlyList<T>
    {
        public static readonly ReadOnlyList<T> Empty = new List<T>();
        private readonly List<T>? _list;
        public ReadOnlyList(List<T>? list) => _list = list;

        [ShowInInspector, InlineProperty, DoNotDrawAsReference,
         ListDrawerSettings(HideAddButton = true, HideRemoveButton = true, DefaultExpandedState = true)]
        private List<T> List => _list ?? Empty._list!;

        public int Count => List.Count;
        public bool Contains(T item) => List.Contains(item);
        public void CopyTo(T[] array, int arrayIndex) => List.CopyTo(array, arrayIndex);
        public T this[int index] => List[index];
        public List<T>.Enumerator GetEnumerator() => List.GetEnumerator();
        public static implicit operator ReadOnlyList<T>(List<T>? list) => new(list);

        public bool TrueForAll([Static] Predicate<T> predicate) => List.TrueForAll(predicate);
        public bool Exists([Static] Predicate<T> predicate) => List.Exists(predicate);


        public bool TrueForAll<TKey>(TKey arg, [Static] Func<T, TKey, bool> predicate)
        {
            foreach (var item in List) if (!predicate(item, arg)) return false;
            return true;
        }

        public bool Exists<TKey>(TKey arg, [Static] Func<T, TKey, bool> predicate)
        {
            foreach (var item in List) if (predicate(item, arg)) return true;
            return false;
        }

        public IReadOnlyList<T> AsRef() => List;
    }

    [SimpleContainer]
    public readonly struct ReadOnlyArray<T> //: IReadOnlyList<T>
    {
        public static readonly ReadOnlyArray<T> Empty = System.Array.Empty<T>();
        private readonly T[]? _array;
        public ReadOnlyArray(T[]? array) => _array = array;

        [ShowInInspector, InlineProperty, DoNotDrawAsReference,
         ListDrawerSettings(HideAddButton = true, HideRemoveButton = true, DefaultExpandedState = true)]
        private T[] Array => _array ?? Empty._array!;

        public int Count => Array.Length;
        public bool Contains(T item) => System.Array.IndexOf(Array, item) >= 0;
        public void CopyTo(T[] array, int arrayIndex) => Array.CopyTo(array, arrayIndex);
        public T this[int index] => Array[index];
        public Enumerator GetEnumerator() => new(Array);
        public static implicit operator ReadOnlyArray<T>(T[]? array) => new(array);

        public bool TrueForAll([Static] Predicate<T> predicate) => System.Array.TrueForAll(Array, predicate);
        public bool Exists([Static] Predicate<T> predicate) => System.Array.Exists(Array, predicate);
        public IReadOnlyList<T> AsRef() => Array;


        public struct Enumerator : IEnumerator<T>
        {
            private int _index = -1;
            private T[] _list;
            public Enumerator(T[] list) => _list = list;
            public T Current => _list[_index];
            public bool MoveNext() => ++_index < _list.Length;
            public void Reset() => _index = -1;
            object? IEnumerator.Current => Current;
            public void Dispose() => _list = null!;
        }
    }

    [SimpleContainer]
    public readonly struct ReadOnlyDictionary<TKey, TValue>
    {
        public static readonly ReadOnlyDictionary<TKey, TValue> Empty = new(new());
        private readonly Dictionary<TKey, TValue>? _dict;
        public ReadOnlyDictionary(Dictionary<TKey, TValue>? dict) => _dict = dict;

        [ShowInInspector, InlineProperty, DoNotDrawAsReference,
         DictionaryDrawerSettings(DisplayMode = DictionaryDisplayOptions.OneLine)]
        private Dictionary<TKey, TValue> Dict => _dict ?? Empty._dict!;

        public int Count => Dict.Count;
        public TValue this[TKey key] => Dict[key];
        public Dictionary<TKey, TValue>.Enumerator GetEnumerator() => Dict.GetEnumerator();
        public bool ContainsKey(TKey key) => Dict.ContainsKey(key);
        public bool TryGetValue(TKey key, out TValue value) => Dict.TryGetValue(key, out value);
        public Dictionary<TKey, TValue>.KeyCollection Keys => Dict.Keys;
        public Dictionary<TKey, TValue>.ValueCollection Values => Dict.Values;
        public static implicit operator ReadOnlyDictionary<TKey, TValue>(Dictionary<TKey, TValue>? dict) => new(dict);

        public IReadOnlyDictionary<TKey, TValue> AsRef() => Dict;
    }

    [SimpleContainer]
    public readonly struct ReadOnlySet<T>
    {
        public static readonly ReadOnlySet<T> Empty = new HashSet<T>();
        private readonly HashSet<T>? _set;
        public ReadOnlySet(HashSet<T>? set) => _set = set;

        [ShowInInspector, InlineProperty, DoNotDrawAsReference,
         ListDrawerSettings(HideAddButton = true, HideRemoveButton = true, DefaultExpandedState = true)]
        private HashSet<T> Set => _set ?? Empty._set!;

        public int Count => Set.Count;
        public bool Contains(T item) => Set.Contains(item);
        public void CopyTo(T[] array, int arrayIndex) => Set.CopyTo(array, arrayIndex);
        public HashSet<T>.Enumerator GetEnumerator() => Set.GetEnumerator();
        public static implicit operator ReadOnlySet<T>(HashSet<T>? set) => new(set);


        public IReadOnlyCollection<T> AsRef() => Set;
    }
}
