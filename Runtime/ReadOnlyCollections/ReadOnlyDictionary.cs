using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityUtils.Attributes.Odin;

namespace UnityUtils.ReadOnlyCollections
{
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
}
