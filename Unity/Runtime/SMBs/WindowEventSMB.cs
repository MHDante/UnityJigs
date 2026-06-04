using UnityEngine;
using UnityEngine.Animations;
using UnityJigs.Extensions;

namespace UnityJigs.SMBs
{
    // Ticks OnTick repeatedly over the window while a state plays, at most once per EmitInterval.
    // Overlap-safe: only the primary (non-overlapping) entry ticks, so a blend re-entering the state won't
    // double-emit. Fire-many-over-a-window (vs TimedEventSMB's fire-once). Window + IsInRange from WindowSMB.
    public abstract class WindowEventSMB : WindowSMB
    {
        [Tooltip("Seconds between ticks while inside the window.")]
        public float EmitInterval = 0.06f;

        private float _emitTimer;

        protected override void OnEnter(Animator animator, AnimatorStateInfo info, int layer,
            AnimatorControllerPlayable controller, bool isOverlap)
        {
            if (!isOverlap) _emitTimer = 0f;
        }

        protected override void OnUpdate(Animator animator, AnimatorStateInfo info, int layer,
            AnimatorControllerPlayable controller)
        {
            if (EnterCounts - ExitCounts > 1) return; // overlap re-entry: only the primary entry ticks
            var nt = info.NormalizedTimeClamp01();
            if (nt < Window.x || nt > Window.y) return;
            _emitTimer -= Time.deltaTime;
            if (_emitTimer > 0f) return;
            _emitTimer = Mathf.Max(EmitInterval, 1e-3f);
            OnTick(animator, Mathf.InverseLerp(Window.x, Window.y, nt));
        }

        protected virtual void OnTick(Animator animator, float windowT) { }
    }
}
