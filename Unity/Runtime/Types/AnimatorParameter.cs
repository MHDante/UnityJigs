using System;
using UnityEngine;
using UnityJigs.Extensions;

namespace UnityJigs.Types
{
    [Serializable]
    public abstract class AnimatorParameter
    {
        public string Name;
        public int Id;

        public abstract AnimatorControllerParameterType Type { get; }
        [SerializeField] public Animator? Animator;
        private bool _hasLogged;

        private AnimatorParameter(string name)
        {
            Name = name;
            if (!string.IsNullOrEmpty(name)) Id = Animator.StringToHash(name);
        }

        // ReSharper disable Unity.PerformanceAnalysis
        public Animator? Check(int id)
        {
            // Unconfigured: the Name was never authored in the inspector, so Id is still the
            // constructor's 0 and every Set() is a silent no-op for the life of the build. This used
            // to fall through the `id == 0` skip below and log NOTHING, EVER — the failure mode that
            // lets a blank parameter sit dead on a prefab undetected. Tested on the FIELD, because
            // the ARGUMENT is legitimately 0; see the skip below.
            if (Id == 0) return Warn("has no Name authored - author it in the inspector");

            // A deliberate skip, not a fault: Trigger.SetIf(false) passes 0 to mean "don't fire".
            if (id == 0) return null;

            var animator = Animator;
            if (animator == null || animator.runtimeAnimatorController == null)
                return Warn("has no Animator reference, or that Animator has no controller");

            // Editor-only: a build trusts the authoring and skips the lookup.
            if (Application.isEditor && !animator.HasParameter(id))
                return Warn("is not a parameter on the assigned Animator's controller");

            return animator;
        }

        // Warns at most once per instance and returns null, so callers read `return Warn(...)`.
        // The budget is spent ONLY when something is actually logged: it used to be consumed by the
        // first Check() call whatever the outcome, so a Trigger whose first use was SetIf(false)
        // silenced every genuine warning that followed it.
        private Animator? Warn(string problem)
        {
            if (_hasLogged) return null;
            _hasLogged = true;
            Debug.LogWarning($"AnimatorParameter {this} {problem}.", Animator);
            return null;
        }

        // Identifies the parameter in warnings. Without this, `{this}` printed only the nested class
        // name, which named neither the parameter nor its owner - and the context object is null in
        // precisely the cases that warn, so the console entry led nowhere.
        public override string ToString() =>
            string.IsNullOrEmpty(Name) ? $"({Type}, unnamed)" : $"{Type} '{Name}'";

        [Serializable]
        public class Int : AnimatorParameter
        {
            public override AnimatorControllerParameterType Type => AnimatorControllerParameterType.Int;
            public Int(string name = "") : base(name) { }
            public void Set(int value) => Check(Id)?.SetInteger(Id, value);
            public int Get() => Check(Id)?.GetInteger(Id) ?? 0;
        }

        [Serializable]
        public class Enum<T> : AnimatorParameter where T : unmanaged, Enum
        {
            static Enum()
            {
                var underlyingType = typeof(T).GetEnumUnderlyingType();
                if (underlyingType != typeof(int)) throw new TypeLoadException("Enum must be an Int32.");
            }

            public override AnimatorControllerParameterType Type => AnimatorControllerParameterType.Int;
            public Enum(string name = "") : base(name) { }
            public void Set(T value) => Check(Id)?.SetInteger(Id, Cast(value));

            public T Get()
            {
                var animator = Check(Id);
                return animator == null ? default : Cast(animator.GetInteger(Id));
            }

            private static unsafe int Cast(T enumValue) => *(int*)&enumValue;
            private static unsafe T Cast(int value) => *(T*)&value;
        }

        [Serializable]
        public class Float : AnimatorParameter
        {
            public override AnimatorControllerParameterType Type => AnimatorControllerParameterType.Float;
            public Float(string name = "") : base(name) { }
            public bool IsDamped;
            public float DampTime = 0.1f;
            public void Set(bool value, float? deltaTime = null) => Set(value ? 1 : 0, deltaTime);

            public void Set(float value, float? deltaTime = null)
            {
                if (IsDamped) Check(Id)?.SetFloat(Id, value, DampTime, deltaTime ?? Time.deltaTime);
                else Check(Id)?.SetFloat(Id, value);
            }

            public float Get() => Check(Id)?.GetFloat(Id) ?? 0f;
        }

        [Serializable]
        public class Bool : AnimatorParameter
        {
            public override AnimatorControllerParameterType Type => AnimatorControllerParameterType.Bool;
            public Bool(string name = "") : base( name) { }
            public void Set(bool value) => Check(Id)?.SetBool(Id, value);
            public bool Get() => Check(Id)?.GetBool(Id) ?? false;
        }

        [Serializable]
        public class Trigger : AnimatorParameter
        {
            public override AnimatorControllerParameterType Type => AnimatorControllerParameterType.Trigger;
            private short _nonce;
            public Trigger(string name = "") : base(name) { }
            public void Set() => Check(Id)?.SetTrigger(Id);
            public void SetIf(bool value) => Check(value?Id : 0)?.SetTrigger(Id);
            public void Reset() => Check(Id)?.ResetTrigger(Id);

            public bool Sync(Signal? s)
            {
                if (s?.CheckChange(ref _nonce) != true) return false;
                Set();
                return true;
            }
        }
    }
}
