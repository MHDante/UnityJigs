using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityUtils.Attributes.Odin;

namespace UnityUtils.ReadOnlyCollections
{
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
