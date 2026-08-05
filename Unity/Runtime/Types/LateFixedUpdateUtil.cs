using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UnityJigs.Types
{
    /// <summary>
    /// A "late fixed update" phase: a player-loop system inserted directly after PhysicsFixedUpdate, so
    /// work runs every fixed step after physics has simulated and OnCollision/OnTrigger callbacks have
    /// fired. Replaces the `while (...) await Awaitable.FixedUpdateAsync(ct)` pump pattern, which on Mono
    /// pays ~200B of GC per await: Awaitable.WireupCancellation registers on the token every call
    /// (allocating a callback node) inside ExecutionContext.SuppressFlow (which Mono implements by
    /// cloning the execution context). Here cancellation is only ever a token *read* — cancelled
    /// subscribers are dropped at their next tick and cancelled awaiters throw OperationCanceledException
    /// on resume — so both entry points are allocation-free per step. Main thread only.
    /// </summary>
    public static class LateFixedUpdateUtil
    {
        // Marker type: how the system appears in the player loop and profiler.
        public struct LateFixedUpdate { }

        private struct Subscription
        {
            public Action Callback;
            public CancellationToken Token;
        }

        private static readonly List<Subscription> Subscriptions = new();
        // Double-buffered so a re-await inside a resumed async body lands in the next step, not this one.
        private static List<Action> _current = new();
        private static List<Action> _next = new();

        /// <summary>
        /// Invoke <paramref name="callback"/> every late fixed update until the token is cancelled.
        /// Callbacks run in subscription order, before any <see cref="WaitAsync"/> continuations.
        /// A callback subscribed during a tick starts on the next step; a cancelled entry is dropped
        /// (without being invoked) at its next tick.
        /// </summary>
        public static void Subscribe(Action callback, CancellationToken cancellationToken)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            Subscriptions.Add(new Subscription { Callback = callback, Token = cancellationToken });
        }

        /// <summary>
        /// Await to resume at the next late fixed update. An already-cancelled token completes
        /// synchronously; a token cancelled while pending throws OperationCanceledException on resume
        /// (one step later) — same observable semantics as Awaitable.FixedUpdateAsync(ct) in a loop,
        /// minus the per-await registration cost.
        /// </summary>
        public static LateFixedAwaitable WaitAsync(CancellationToken cancellationToken = default) =>
            new(cancellationToken);

        internal static void EnqueueContinuation(Action continuation) => _next.Add(continuation);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Init()
        {
            // Fresh state every play session, even with domain reload disabled.
            Subscriptions.Clear();
            _current.Clear();
            _next.Clear();
            Install();
#if UNITY_EDITOR
            EditorApplication.playModeStateChanged -= OnPlayModeState;
            EditorApplication.playModeStateChanged += OnPlayModeState;
#endif
        }

        private static void Install()
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            var phases = loop.subSystemList;
            for (var i = 0; i < phases.Length; i++)
            {
                if (phases[i].type != typeof(FixedUpdate)) continue;
                var subs = new List<PlayerLoopSystem>(phases[i].subSystemList);
                subs.RemoveAll(static s => s.type == typeof(LateFixedUpdate)); // idempotent re-install
                var idx = subs.FindIndex(static s => s.type == typeof(FixedUpdate.PhysicsFixedUpdate));
                subs.Insert(idx + 1, new PlayerLoopSystem { type = typeof(LateFixedUpdate), updateDelegate = Tick });
                phases[i].subSystemList = subs.ToArray();
                PlayerLoop.SetPlayerLoop(loop);
                return;
            }

            Debug.LogError("LateFixedUpdateUtil: FixedUpdate phase not found in the player loop.");
        }

        private static void Tick()
        {
            // 1) Subscribers, in order, compacting cancelled entries out in place.
            var subs = Subscriptions;
            var count = subs.Count; // snapshot: entries added during callbacks start next step
            var write = 0;
            int read;
            for (read = 0; read < count; read++)
            {
                var s = subs[read];
                if (s.Token.IsCancellationRequested) continue;
                try { s.Callback(); }
                catch (Exception e) { Debug.LogException(e); }
                if (write != read) subs[write] = s;
                write++;
            }

            // Preserve anything appended during the callbacks.
            for (; read < subs.Count; read++, write++) subs[write] = subs[read];
            subs.RemoveRange(write, subs.Count - write);

            // 2) Awaiter continuations. Swap first so re-awaits enqueue into the other list.
            (_current, _next) = (_next, _current);
            for (var i = 0; i < _current.Count; i++)
            {
                try { _current[i](); }
                catch (Exception e) { Debug.LogException(e); }
            }

            _current.Clear();
        }

#if UNITY_EDITOR
        private static void OnPlayModeState(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingPlayMode) return;
            // Drop pending work so nothing resumes into a torn-down scene. Abandoned async machines
            // become unreachable and are collected; the system itself never ticks outside play mode.
            Subscriptions.Clear();
            _current.Clear();
            _next.Clear();
        }
#endif
    }

    /// <summary>Awaitable returned by <see cref="LateFixedUpdateUtil.WaitAsync"/>.</summary>
    public readonly struct LateFixedAwaitable
    {
        private readonly CancellationToken _token;
        public LateFixedAwaitable(CancellationToken token) => _token = token;
        public Awaiter GetAwaiter() => new(_token);

        public readonly struct Awaiter : ICriticalNotifyCompletion
        {
            private readonly CancellationToken _token;
            public Awaiter(CancellationToken token) => _token = token;
            // Already-cancelled tokens skip the enqueue and throw synchronously in GetResult.
            public bool IsCompleted => _token.IsCancellationRequested;
            public void OnCompleted(Action continuation) => UnsafeOnCompleted(continuation);
            public void UnsafeOnCompleted(Action continuation) => LateFixedUpdateUtil.EnqueueContinuation(continuation);
            public void GetResult() => _token.ThrowIfCancellationRequested();
        }
    }
}
