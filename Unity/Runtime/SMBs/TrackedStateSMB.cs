using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;

namespace UnityJigs.SMBs
{
    // Generic StateMachineBehaviour base with overlap-safe, per-entry tracking. It counts state
    // enters/exits and keeps the live AnimatorStateInfo(s), so subclasses can fire exactly once per entry
    // (surviving a state that re-enters itself via a blend) or query the active window via AnyState.
    //
    // Skater-agnostic: this is the shared backbone under both the generic timing infra (TimedEventSMB ->
    // ShakeSMB) and NorthShore's skater SMB bases, which add their SkaterAnimator registration on top.
    public abstract class TrackedStateSMB : StateMachineBehaviour
    {
        protected int EnterCounts { get; private set; }
        protected int ExitCounts { get; private set; }
        protected readonly List<AnimatorStateInfo> KnownStates = new();

        private int _frame;
        private int _updateCount;

        public sealed override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex,
            AnimatorControllerPlayable controller)
        {
            base.OnStateEnter(animator, stateInfo, layerIndex, controller);
            var isOverlap = EnterCounts > ExitCounts;
            EnterCounts++;
            KnownStates.Add(stateInfo);
            OnEnter(animator, stateInfo, layerIndex, controller, isOverlap);
        }

        public sealed override void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex,
            AnimatorControllerPlayable controller)
        {
            base.OnStateUpdate(animator, stateInfo, layerIndex, controller);
            _updateCount = _frame != Time.frameCount ? 0 : _updateCount + 1;
            _frame = Time.frameCount;
            KnownStates[_updateCount] = stateInfo;
            OnUpdate(animator, stateInfo, layerIndex, controller);
        }

        public sealed override void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex,
            AnimatorControllerPlayable controller)
        {
            base.OnStateExit(animator, stateInfo, layerIndex, controller);
            KnownStates.RemoveAt(0);
            ExitCounts++;
            var isOverlap = EnterCounts > ExitCounts;
            OnExit(animator, stateInfo, layerIndex, controller, isOverlap);
        }

        public sealed override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex) =>
            base.OnStateEnter(animator, stateInfo, layerIndex);

        public sealed override void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex) =>
            base.OnStateExit(animator, stateInfo, layerIndex);

        public sealed override void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex) =>
            base.OnStateUpdate(animator, stateInfo, layerIndex);

        protected virtual void OnEnter(Animator animator, AnimatorStateInfo info, int layer,
            AnimatorControllerPlayable controller, bool isOverlap) { }

        protected virtual void OnUpdate(Animator animator, AnimatorStateInfo info, int layer,
            AnimatorControllerPlayable controller) { }

        protected virtual void OnExit(Animator animator, AnimatorStateInfo info, int layer,
            AnimatorControllerPlayable controller, bool isOverlap) { }

        protected static bool AnyState<T>(T smb, Func<T, AnimatorStateInfo, bool> predicate)
            where T : TrackedStateSMB
        {
            foreach (var state in smb.KnownStates)
                if (predicate(smb, state)) return true;
            return false;
        }
    }
}
