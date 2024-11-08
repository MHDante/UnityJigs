using System;
using System.Collections.Generic;
using MHDante.UnityUtils.Attributes.Odin;
using Sirenix.OdinInspector;
using Static = JetBrains.Annotations.RequireStaticDelegateAttribute;

namespace MHDante.UnityUtils.ReadOnlyCollections
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
}
