using System;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace UnityJigs.Types
{
    public class Signal : IValueTaskSource
    {
        public event Action? OnChange;
        private ManualResetValueTaskSourceCore<bool> _event;

        public bool CheckChange(ref short nonce)
        {
            var hit = _event.Version; // copy for thread safety
            if (hit == default) return false;
            if (hit == nonce) return false;
            nonce = hit;
            return true;
        }

        public void Set()
        {
            _event.SetResult(true);
            _event.Reset();
            OnChange?.Invoke();
        }

        public ValueTask WaitAsync() => new(this, _event.Version);
        public void GetResult(short token) => _event.GetResult(token);
        public ValueTaskSourceStatus GetStatus(short token) => _event.GetStatus(token);

        public void OnCompleted(Action<object> continuation, object state, short token,
            ValueTaskSourceOnCompletedFlags flags) =>
            _event.OnCompleted(continuation, state, token, flags);


    }
}
