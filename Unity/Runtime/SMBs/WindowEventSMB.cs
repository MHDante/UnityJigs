using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Animations;
using UnityJigs.Extensions;

namespace UnityJigs.SMBs
{
    // Ticks OnTick repeatedly over a normalized window while a state plays, at most once per EmitInterval.
    // Overlap-safe: only the primary (non-overlapping) entry ticks, so a blend re-entering the state won't
    // double-emit. Generic counterpart to TimedEventSMB — fire-many-over-a-window vs fire-once.
    public abstract class WindowEventSMB : TrackedStateSMB
    {
        [MinMaxSlider(0, 1, true)] public Vector2 Window = new(0f, 1f);

        [Tooltip("Seconds between ticks while inside the window.")]
        public float EmitInterval = 0.06f;

        private float _emitTimer;

        // True while the active state is inside the window (i.e. while ticks are firing). Overlap-safe —
        // checks every known entry. Same inclusive bounds the tick uses, so this matches when OnTick runs.
        public bool IsInRange() => AnyState(this, static (smb, info) =>
        {
            var t = info.NormalizedTimeClamp01();
            return t >= smb.Window.x && t <= smb.Window.y;
        });

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
