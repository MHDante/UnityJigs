using System;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using UnityEngine;

// ReSharper disable AccessToModifiedClosure

namespace UnityJigs.Extensions
{
    public static class TaskUtils
    {
        public static Task<TResult> Subscribe<TSource, TResult>(
            TSource source,
            Action<TSource, Action<TResult>> subscribe,
            Action<TSource, Action<TResult>> unsubscribe
        )
        {
            var tcs = new TaskCompletionSource<TResult>();
            var subFn = new Action<TResult>(t => tcs.SetResult(t));
            subscribe(source, subFn);
            Action<TResult> listener = null!;
            listener = result =>
            {
                tcs.SetResult(result);
                unsubscribe(source, listener);
            };

            return tcs.Task;
        }

        public static Task Subscribe<TSource>(
            TSource source,
            Action<TSource, Action> subscribe,
            Action<TSource, Action> unsubscribe
        )
        {
            var tcs = new TaskCompletionSource<object?>();
            var subFn = new Action(() => tcs.SetResult(null));
            subscribe(source, subFn);
            Action listener = null!;
            listener = () =>
            {
                tcs.SetResult(null);
                unsubscribe(source, listener);
            };

            return tcs.Task;
        }


        public static void LogErrors(this Task task)
        {
            task.ContinueWith(static it =>
            {
                if (it.Status != TaskStatus.Faulted) return;
                Debug.LogException(it.Exception);
                if (it.Exception?.InnerException != null)
                    Debug.LogException(it.Exception.InnerException);
            });
        }

        public static ValueTask WaitUntil<T>(T context, [RequireStaticDelegate] Func<T, bool> condition) =>
            WaitUntil(context, CancellationToken.None, condition);

        public static async ValueTask WaitUntil<T>(T context, CancellationToken t,
            [RequireStaticDelegate] Func<T, bool> condition)
        {

            while (!condition(context))
            {
#if UNITY_6000_0_OR_NEWER
                await Awaitable.NextFrameAsync(t);
#else
                await Task.Yield();
#endif
            }
        }

        public static ValueTask FixedWaitUntil<T>(T context, [RequireStaticDelegate] Func<T, bool> condition) =>
            FixedWaitUntil(context, CancellationToken.None, condition);

        public static async ValueTask FixedWaitUntil<T>(T context, CancellationToken t,
            [RequireStaticDelegate]Func<T, bool> condition)
        {
            while (!condition(context))
            {
#if UNITY_6000_0_OR_NEWER
                await Awaitable.NextFrameAsync(t);
#else
                await Task.Yield();
#endif
            }
        }



        
    }
}
