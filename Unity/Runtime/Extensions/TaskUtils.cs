using System;
using System.Threading.Tasks;

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

    }
}
