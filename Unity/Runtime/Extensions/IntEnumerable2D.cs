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
            private Vector2Int _current;
            public Vector2Int Current => _current;
            private bool _isStarted;

            public Enumerator(IntEnumerable2D enumerable)
            {
                _current = default;
                _isStarted = false;
                _enumerable = enumerable;
            }

            public bool MoveNext()
            {

                if (!_isStarted)
                {
                    _current = _enumerable._start;
                    _isStarted = true;

                    if (_enumerable._start.x >= _enumerable._end.x) return false;
                    if (_enumerable._start.y >= _enumerable._end.y) return false;
                    return true;
                }

                if (_enumerable._xThenY)
                {
                    _current.x += _enumerable._increment.x;
                    if (_current.x >= _enumerable._end.x)
                    {
                        _current.x = _enumerable._start.x;
                        _current.y += _enumerable._increment.y;
                        if (_current.y >= _enumerable._end.y) return false;
                    }
                }
                else
                {
                    _current.y += _enumerable._increment.y;
                    if (_current.y >= _enumerable._end.y)
                    {
                        _current.y = _enumerable._start.y;
                        _current.x += _enumerable._increment.x;
                        if (_current.x >= _enumerable._end.x) return false;
                    }
                }

                return true;
            }
            public void Reset() => _isStarted = false;
            object IEnumerator.Current => Current;
            public void Dispose() { }
        }
    }
}
