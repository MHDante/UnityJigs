using System;
using System.Collections;
using System.Collections.Generic;
using JetBrains.Annotations;
using UnityEngine;

namespace UnityJigs.Types
{
    [Serializable]
    public class SerializedDict<TKey, TValue> : IDictionary<TKey, TValue>, ISerializationCallbackReceiver
    {
        private Dictionary<TKey, TValue> _dictionary = new();
        private IDictionary<TKey, TValue> Dictionary => _dictionary;
        [SerializeField] private List<SerializedKvp> SerializedValue = new();


        public static SerializedDict<TKey,TValue> Create<T>(IEnumerable<T> list, Func<T,TKey> keySelector, Func<T,TValue> valueSelector)
        {
            var dict = new SerializedDict<TKey, TValue>();
            foreach (var input in list) dict.Add(keySelector(input), valueSelector(input));
            return dict;
        }


        public static SerializedDict<TKey, TValue> Create(IEnumerable<TKey> list, Func<TKey,TValue> valueSelector)
        {
            var dict = new SerializedDict<TKey, TValue>();
            foreach (var input in list) dict.Add(input, valueSelector(input));
            return dict;
        }

        public void OnBeforeSerialize()
        {
            SerializedValue.Clear();
            foreach (var kvp in _dictionary) SerializedValue.Add(new() { Key = kvp.Key, Value = kvp.Value });
        }

        public void OnAfterDeserialize()
        {
            _dictionary = new Dictionary<TKey, TValue>();
            foreach (var kvp in SerializedValue)
            {
                if (kvp.Key == null || _dictionary.ContainsKey(kvp.Key)) continue;
                _dictionary.Add(kvp.Key, kvp.Value);
            }
        }

        public bool GetInvalidKey(out TKey? invalidKey)
        {
            var set = new HashSet<TKey>();
            foreach (var kvp in SerializedValue)
            {
                if (kvp.Key != null && set.Add(kvp.Key)) continue;
                invalidKey = kvp.Key;
                return true;
            }

            invalidKey = default;
            return false;
        }

        public TValue this[TKey key]
        {
            get => _dictionary[key];
            set => _dictionary[key] = value;
        }

        ICollection<TKey> IDictionary<TKey, TValue>.Keys => _dictionary.Keys;
        public Dictionary<TKey,TValue>.KeyCollection Keys => _dictionary.Keys;
        ICollection<TValue> IDictionary<TKey, TValue>.Values => _dictionary.Values;
        public Dictionary<TKey, TValue>.ValueCollection Values => _dictionary.Values;
        public int Count => _dictionary.Count;
        public bool IsReadOnly => false;


        public void Add(TKey key, TValue value) => _dictionary.Add(key, value);
        public bool ContainsKey(TKey key) => _dictionary.ContainsKey(key);
        public bool Remove(TKey key) => _dictionary.Remove(key);
        public bool TryGetValue(TKey key, out TValue value) => _dictionary.TryGetValue(key, out value);
        public void Add(KeyValuePair<TKey, TValue> item) => _dictionary.Add(item.Key, item.Value);
        public void Clear() => _dictionary.Clear();
        public bool Contains(KeyValuePair<TKey, TValue> item) =>Dictionary.Contains(item);


        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex) => Dictionary.CopyTo(array, arrayIndex);
        public bool Remove(KeyValuePair<TKey, TValue> item) => _dictionary.Remove(item.Key);

        [MustDisposeResource]
        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator() => GetEnumerator();
        public Dictionary<TKey, TValue>.Enumerator GetEnumerator() => _dictionary.GetEnumerator();

        [MustDisposeResource]
        IEnumerator IEnumerable.GetEnumerator() => _dictionary.GetEnumerator();

        [Serializable]
        public struct SerializedKvp
        {
            public TKey Key;
            public TValue Value;
        }
    }
}

#if UNITY

#endif
