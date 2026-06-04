using UnityEngine;
using UnityEngine.Animations;
using UnityJigs.Extensions;

namespace UnityJigs.SMBs
{
    // Fires OnTimedEvent exactly once when the state crosses ExitTime (normalized time). Overlap-safe: the
    // EnterCounts/ExitCounts guard suppresses a duplicate fire when the state re-enters itself via a blend.
    // FireOnExitIfMissed lets a subclass guarantee the beat still fires on exit if the window was skipped.
    //
    // Generic counterpart to NorthShore's skater TimedEventSkaterSMB (which hands back a SkaterAnimator);
    // this one hands back the plain Animator, so it works on any animator (e.g. ShakeSMB).
    public abstract class TimedEventSMB : TrackedStateSMB
    {
        private int _elapseCounts;
        protected abstract bool FireOnExitIfMissed { get; }
        [Range(0, 1)] public float ExitTime = .5f;

        public bool AreAnyExpired()
        {
            foreach (var state in KnownStates)
                if (state.NormalizedTimeClamp01() >= ExitTime) return true;
            return false;
        }

        protected bool AreAnyPending()
        {
            foreach (var state in KnownStates)
                if (state.NormalizedTimeClamp01() < ExitTime) return true;
            return false;
        }

        protected override void OnUpdate(Animator animator, AnimatorStateInfo info, int layer,
            AnimatorControllerPlayable controller)
        {
            var time = info.NormalizedTimeClamp01();
            if (time < ExitTime) return;
            if (EnterCounts == _elapseCounts) return;
            if (EnterCounts - ExitCounts > 1) return;
            _elapseCounts++;
            OnTimedEvent(animator);
        }

        protected override void OnExit(Animator animator, AnimatorStateInfo info, int layer,
            AnimatorControllerPlayable controller, bool isOverlap)
        {
            if (_elapseCounts >= ExitCounts) return;
            _elapseCounts++;
            if (FireOnExitIfMissed) OnTimedEvent(animator);
        }

        protected virtual void OnTimedEvent(Animator animator) { }
    }
}
