using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace UnityJigs.Editor.Utilities
{
    public static class AnimatorMenuItems
    {
        [MenuItem("Utils/Animator/Rename blend trees to state name")]
        public static void RenameBlends()
        {
            RuntimeAnimatorController? rac = null;
            rac ??= Selection.activeObject as RuntimeAnimatorController;
            rac ??= (Selection.activeObject as Animator)?.runtimeAnimatorController;
            rac ??= Selection.activeGameObject?.GetComponent<Animator>()?.runtimeAnimatorController;

            var animator = rac as AnimatorController;
            animator ??= (rac as AnimatorOverrideController)?.runtimeAnimatorController as AnimatorController;
            if (animator == null)
            {
                Debug.LogError("No Selected animator");
                return;
            }

            foreach (var layer in animator.layers)
            {
                var machine = layer.stateMachine;
                var states = machine.states;
                var stateMachines = machine.stateMachines;
                RenameMachine(states, stateMachines);
            }
        }

        private static void RenameMachine(ChildAnimatorState[] states, ChildAnimatorStateMachine[] stateMachines)
        {
            foreach (var animState in states)
            {
                var state = animState.state;
                if (state.motion is BlendTree b)
                {
                    var oldName = b.name;
                    var newName = state.name + " Blend";
                    if(oldName == newName) continue;
                    b.name = newName;
                    Debug.Log($"Renamed {oldName} to {newName}");
                }
            }

            foreach (var subMachine in stateMachines)
            {
                RenameMachine(subMachine.stateMachine.states, subMachine.stateMachine.stateMachines);
            }
        }
    }


}
