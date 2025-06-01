using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UnityJigs.Extensions
{
    public readonly struct IntEnumerable2D : IEnumerable<Vector2Int>
    {
        private readonly Vector2Int _start;
        private readonly Vector2Int _end;
        private readonly Vector2Int _increment;
        private readonly bool _xThenY;

        public IntEnumerable2D(Vector2Int start, Vector2Int end, Vector2Int? increment = null, bool xThenY = true)
        {
            _increment = increment ?? Vector2Int.one;
            if (_increment.x == 0 || _increment.y == 0) throw new ArgumentException("increment cannot be 0");
            _start = start;
            _end = end;
            _xThenY = xThenY;
        }

        public IntEnumerable2D(Vector2Int end, Vector2Int? increment = null) : this(Vector2Int.zero, end, increment) { }

        public IEnumerator<Vector2Int> GetEnumerator() => new Enumerator(this);
        IEnumerator IEnumerable.GetEnumerator()=> GetEnumerator();

        public struct Enumerator : IEnumerator<Vector2Int>
        {
            private readonly IntEnumerable2D _enumerable;
            private Vector2Int current;
            public Vector2Int Current => current;
            private bool isStarted;

            public Enumerator(IntEnumerable2D enumerable)
            {
                current = default;
                isStarted = false;
                _enumerable = enumerable;
            }

            public bool MoveNext()
            {

                if (!isStarted)
                {
                    current = _enumerable._start;
                    isStarted = true;

                    if (_enumerable._start.x >= _enumerable._end.x) return false;
                    if (_enumerable._start.y >= _enumerable._end.y) return false;
                    return true;
                }

                if (_enumerable._xThenY)
                {
                    current.x += _enumerable._increment.x;
                    if (current.x >= _enumerable._end.x)
                    {
                        current.x = _enumerable._start.x;
                        current.y += _enumerable._increment.y;
                        if (current.y >= _enumerable._end.y) return false;
                    }
                }
                else
                {
                    current.y += _enumerable._increment.y;
                    if (current.y >= _enumerable._end.y)
                    {
                        current.y = _enumerable._start.y;
                        current.x += _enumerable._increment.x;
                        if (current.x >= _enumerable._end.x) return false;
                    }
                }

                return true;
            }
            public void Reset() => isStarted = false;
            object IEnumerator.Current => Current;
            public void Dispose() { }
        }
    }
}
