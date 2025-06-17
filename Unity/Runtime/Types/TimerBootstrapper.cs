using System;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

#if UNITY_EDITOR
using UnityEditor;
#endif


namespace UnityJigs.Types
{
    [UsedImplicitly(ImplicitUseTargetFlags.Itself | WithInheritors)]
    public abstract class LoopSystem
    {
        private const ImplicitUseTargetFlags WithInheritors = (ImplicitUseTargetFlags)4;
        private PlayerLoopSystem _systemRef;
        protected virtual Type ParentLoopType => typeof(Update);
        protected abstract void Update();
        protected virtual void OnAdd() { }
        protected virtual void OnRemove() { }


        bool InsertSystem(int index)
        {
            PlayerLoopSystem currentPlayerLoop = PlayerLoop.GetCurrentPlayerLoop();
            _systemRef = new PlayerLoopSystem()
            {
                type = GetType(),
                updateDelegate = Update,
                subSystemList = null
            };

            var didInsert = PlayerLoopUtils.InsertSystem(ParentLoopType, ref currentPlayerLoop, in _systemRef, index);
            if (!didInsert)
            {
                Debug.LogError($"Could not initialize system of type {GetType().Name}. " +
                               $"Did you provide a valid parent system type?");
                return false;
            }

            PlayerLoop.SetPlayerLoop(currentPlayerLoop);
            OnAdd();
            return didInsert;
        }

        protected void RemoveSystem()
        {
            PlayerLoopSystem currentPlayerLoop = PlayerLoop.GetCurrentPlayerLoop();
            PlayerLoopUtils.RemoveSystem(ref currentPlayerLoop, in _systemRef);
            PlayerLoop.SetPlayerLoop(currentPlayerLoop);
            OnRemove();
        }


        protected void Initialize()
        {
            if (!InsertSystem(0)) return;

#if UNITY_EDITOR
            EditorApplication.playModeStateChanged += OnPlayModeState;
        }

        private void OnPlayModeState(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode) RemoveSystem();
            EditorApplication.playModeStateChanged -= OnPlayModeState;
        }
#else
        }
#endif
    }
}
