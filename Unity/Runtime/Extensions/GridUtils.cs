using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

namespace UnityJigs.Extensions
{
    public static class GridUtils
    {
        static readonly Vector2Int[] Directions = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        private struct Context<TContext>
        {
            public TContext _context;
            public Func<TContext, Vector2Int, bool> CanEnterContext;
            public Func<TContext, Vector2Int, bool> IsTargetContext;
            public Func<Vector2Int, bool> CanEnterSimple;
            public Func<Vector2Int, bool> IsTargetSimple;

            public bool CanEnter(Vector2Int i) => CanEnterContext?.Invoke(_context, i) ?? CanEnterSimple(i);
            public bool IsTarget(Vector2Int i) => IsTargetContext?.Invoke(_context, i) ?? IsTargetSimple(i);
        }

        public static List<Vector2Int>? BFS<TContext>(
            Vector2Int start,
            TContext context,
            Func<TContext, Vector2Int, bool> canEnter,
            Func<TContext, Vector2Int, bool> isTarget)  =>
            BFS<TContext>(start, new() { CanEnterContext = canEnter, IsTargetContext = isTarget, _context = context});

        public static List<Vector2Int>? BFS(
            Vector2Int start,
            Func<Vector2Int, bool> canEnter,
            Func<Vector2Int, bool> isTarget) =>
            BFS<object>(start, new() { CanEnterSimple = canEnter, IsTargetSimple = isTarget });

        private static List<Vector2Int>? BFS<TContext>(Vector2Int start, Context<TContext> context)
        {
            using var _1 = QueuePool<Vector2Int>.Get(out var queue);
            using var _2 = DictionaryPool<Vector2Int, Vector2Int>.Get(out var cameFrom);
            using var _3 = HashSetPool<Vector2Int>.Get(out var visited);

            queue.Enqueue(start);
            visited.Add(start);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();

                if (context.IsTarget(current))
                {
                    var path = new List<Vector2Int>();
                    Vector2Int backtrack = current;
                    path.Add(backtrack);
                    while (cameFrom.ContainsKey(backtrack))
                    {
                        backtrack = cameFrom[backtrack];
                        path.Add(backtrack);
                    }

                    path.Reverse();
                    return path;
                }

                foreach (var dir in Directions)
                {
                    var next = current + dir;
                    if (visited.Contains(next)) continue;
                    if (!context.CanEnter(next)) continue;

                    queue.Enqueue(next);
                    visited.Add(next);
                    cameFrom[next] = current;
                }
            }

            return null; // No path found
        }
    }
}
