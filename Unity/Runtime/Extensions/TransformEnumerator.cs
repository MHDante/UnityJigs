using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UnityJigs.Extensions
{
    public struct TransformEnumerable : IEnumerable<Transform>
    {
        public TransformEnumerable(Transform transform) => Transform = transform;
        public Transform Transform { get; }
        public IEnumerator<Transform> GetEnumerator() => new TransformEnumerator(Transform);
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public struct TransformEnumerator : IEnumerator<Transform>
    {
        public Transform Transform { get; }

        private int _index;
        private Transform? _current;

        public TransformEnumerator(Transform transform)
        {
            Transform = transform;
            _index = -1;
            _current = null;
        }

        public Transform Current => _current ?? throw new IndexOutOfRangeException();
        object IEnumerator.Current => _current?? throw new IndexOutOfRangeException();

        public bool MoveNext()
        {
            if (Transform == null)
                return false;

            var next = _index + 1;
            if (next >= Transform.childCount)
            {
                _current = null;
                return false;
            }

            _index = next;
            _current = Transform.GetChild(_index);
            return true;
        }

        public void Reset()
        {
            _index = -1;
            _current = null;
        }

        public void Dispose() { }
    }
}
