using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityJigs.Extensions;

namespace UnityJigs.Types
{
    [Serializable]
    public class SerializedSet<T> : ISet<T>,IReadOnlyCollection<T>, ISerializationCallbackReceiver
    {
        [SerializeField] private List<T> SerializedValue = new();
        private readonly HashSet<T> _set = new();

        public HashSet<T>.Enumerator GetEnumerator() => _set.GetEnumerator();
        public bool Add(T item) => _set.Add(item);
        public void ExceptWith(IEnumerable<T> other) => _set.ExceptWith(other);
        public void IntersectWith(IEnumerable<T> other) => _set.IntersectWith(other);
        public bool IsProperSubsetOf(IEnumerable<T> other) => _set.IsProperSubsetOf(other);
        public bool IsProperSupersetOf(IEnumerable<T> other) => _set.IsProperSupersetOf(other);
        public bool IsSubsetOf(IEnumerable<T> other) => _set.IsSubsetOf(other);
        public bool IsSupersetOf(IEnumerable<T> other) => _set.IsSupersetOf(other);
        public bool Overlaps(IEnumerable<T> other) => _set.Overlaps(other);
        public bool SetEquals(IEnumerable<T> other) => _set.SetEquals(other);
        public void SymmetricExceptWith(IEnumerable<T> other) => _set.SymmetricExceptWith(other);
        public void UnionWith(IEnumerable<T> other) => _set.UnionWith(other);
        public void Clear() => _set.Clear();
        public bool Contains(T item) => _set.Contains(item);
        public void CopyTo(T[] array, int arrayIndex) => _set.CopyTo(array, arrayIndex);
        public bool Remove(T item) => _set.Remove(item);
        public int Count => _set.Count;
        public bool IsReadOnly => ((ISet<T>)_set).IsReadOnly;


        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        void ICollection<T>.Add(T item) => _set.Add(item);

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
            SerializedValue.Clear();
            SerializedValue.AddRange(_set);
        }
        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            _set.Clear();
            _set.AddRange(SerializedValue);
        }
    }
}
