using System;
using System.Collections;
using System.Collections.Generic;
using JetBrains.Annotations;
using MHDante.UnityUtils.Attributes.Odin;
using Sirenix.OdinInspector;

namespace MHDante.UnityUtils.ReadOnlyCollections
{
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

        public bool TrueForAll([RequireStaticDelegate] Predicate<T> predicate) => System.Array.TrueForAll(Array, predicate);
        public bool Exists([RequireStaticDelegate] Predicate<T> predicate) => System.Array.Exists(Array, predicate);
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
}
