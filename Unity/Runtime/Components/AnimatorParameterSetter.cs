using System;
using System.Collections.Generic;
using UnityEngine;
using UnityJigs.Extensions;

namespace UnityJigs.Components
{
    /// <summary>
    /// Sets parameters on an Animator from numbers held on this component, so whatever animates this component
    /// (an animator higher up) drives the animator named here. Each row of Parameters is one parameter, and its
    /// number is the field Value0, Value1 and so on, of the same index.
    ///
    /// The numbers are plain fields, not part of the rows, because Unity's animation can't key a value inside a
    /// list (tried: no binding is offered and a curve on such a path moves nothing). That is also why there are
    /// MaxRows of them and no more; and since a clip keys "Value3" by name, reordering or removing a row moves
    /// the rows under the curves. One of these per GameObject: animation only ever reaches the first component
    /// of a type on an object.
    /// </summary>
    [DisallowMultipleComponent]
    public class AnimatorParameterSetter : MonoBehaviour
    {
        public enum SetTimes
        {
            OnEnable = 0,   // once, each time this component is enabled
            EveryFrame = 1, // every LateUpdate, after this frame's animation has written the values
        }

        // One parameter. Its value is one number whatever the parameter's type, so one kind of curve drives them
        // all: a Float takes it as it is, an Int its nearest whole number, a Bool is on above 0.5, and a Trigger
        // fires each time it rises past 0.5.
        [Serializable]
        public struct Entry
        {
            public string Name;
            public int Id; // Animator.StringToHash(Name); 0 = no parameter chosen, the row does nothing
            public AnimatorControllerParameterType Type;
        }

        public const int MaxRows = 8;

        [Tooltip("The animator whose parameters are set.")]
        public Animator? Animator;

        [Tooltip("On Enable: once, each time this is enabled. Every Frame: every frame, so animated values follow.")]
        public SetTimes When = SetTimes.OnEnable;

        public List<Entry> Parameters = new();

        // Row i's number. Drawn beside its row, never on their own.
        [HideInInspector] public float Value0, Value1, Value2, Value3, Value4, Value5, Value6, Value7;

        public float ValueOf(int row) => row switch
        {
            0 => Value0, 1 => Value1, 2 => Value2, 3 => Value3,
            4 => Value4, 5 => Value5, 6 => Value6, 7 => Value7,
            _ => 0f,
        };

        private void OnValidate()
        {
            if (Parameters.Count > MaxRows) Parameters.RemoveRange(MaxRows, Parameters.Count - MaxRows);
        }

        private const float High = 0.5f;

        // Per row: whether its value was above High at the last set (a Trigger fires on the rise only).
        private bool[] _wasHigh = Array.Empty<bool>();
        private bool _owed;    // On Enable: the set has not reached the animator yet
        private bool _checked; // the rows were compared with the controller once (editor only)

        private void OnEnable()
        {
            Array.Clear(_wasHigh, 0, _wasHigh.Length); // a value already high fires its Trigger on enable
            _owed = !Apply();
        }

        private void LateUpdate()
        {
            // An animator enabled in the same frame as this is not ready at OnEnable and would drop the set:
            // On Enable keeps trying until it lands.
            if (When == SetTimes.EveryFrame || _owed) _owed = !Apply();
        }

        /// <summary>Sets every row's parameter now. False when the animator isn't ready to take them.</summary>
        public bool Apply()
        {
            var animator = Animator;
            if (animator == null || !animator.isActiveAndEnabled || !animator.isInitialized) return false;
            if (_wasHigh.Length != Parameters.Count) _wasHigh = new bool[Parameters.Count];
            if (Application.isEditor && !_checked) Check(animator);

            for (var i = 0; i < Parameters.Count; i++)
            {
                var entry = Parameters[i];
                var value = ValueOf(i);
                var high = value > High;
                if (entry.Id != 0) switch (entry.Type)
                {
                    case AnimatorControllerParameterType.Float:
                        animator.SetFloat(entry.Id, value);
                        break;
                    case AnimatorControllerParameterType.Int:
                        animator.SetInteger(entry.Id, Mathf.RoundToInt(value));
                        break;
                    case AnimatorControllerParameterType.Bool:
                        animator.SetBool(entry.Id, high);
                        break;
                    case AnimatorControllerParameterType.Trigger:
                        if (high && !_wasHigh[i]) animator.SetTrigger(entry.Id);
                        break;
                }
                _wasHigh[i] = high;
            }
            return true;
        }

        // Once, in the editor: a row naming a parameter the controller doesn't have says so. A build trusts the
        // authoring.
        private void Check(Animator animator)
        {
            _checked = true;
            foreach (var entry in Parameters)
                if (entry.Id != 0 && !animator.HasParameter(entry.Id))
                    Debug.LogWarning($"AnimatorParameterSetter: '{entry.Name}' is not a parameter of {animator.name}'s " +
                                     "controller; that row sets nothing.", this);
        }
    }
}
