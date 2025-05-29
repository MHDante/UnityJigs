using System;
using UnityEngine;
using UnityJigs.Extensions;

namespace UnityJigs.Types
{
    [Serializable]
    public abstract class AnimatorParameter
    {
        public string Name = "";
        public int Id;

        public AnimatorControllerParameterType Type { get; }
        [SerializeField] public Animator? Animator;
        private bool _hasLogged;

        private AnimatorParameter(AnimatorControllerParameterType type) => Type = type;

        public Animator? Check(int id)
        {
            var animator = Animator;
            if (id == 0) animator = null;
            else if (animator == null)
            {
                if (!_hasLogged) Debug.LogWarning($"Animator is null: {this}", Animator);
                animator = null;
            }
            else if (Application.isEditor && !_hasLogged && !animator.HasParameter(id))
            {
                Debug.LogWarning($"Animator does not have a parameter with the id {id}.", Animator);
                animator = null;
            }

            _hasLogged = true;
            return animator;
        }

        [Serializable]
        public class Int : AnimatorParameter
        {
            public Int() : base(AnimatorControllerParameterType.Int) { }
            public void Set(int value) => Check(Id)?.SetInteger(Id, value);
        }

        [Serializable]
        public class Enum<T> : AnimatorParameter where T : unmanaged, Enum
        {
            static Enum()
            {
                var underlyingType = typeof(T).GetEnumUnderlyingType();
                if (underlyingType != typeof(int)) throw new TypeLoadException("Enum must be an Int32.");
            }

            public Enum() : base(AnimatorControllerParameterType.Int) { }
            public void Set(T value) => Check(Id)?.SetInteger(Id, Cast(value));
            private static unsafe int Cast(T enumValue) => *(int*)&enumValue;
        }

        [Serializable]
        public class Float : AnimatorParameter
        {
            public Float() : base(AnimatorControllerParameterType.Float) { }
            public bool IsDamped;
            public float DampTime = 0.1f;
            public void Set(bool value, float? deltaTime = null) => Set(value?1:0, deltaTime);
            public void Set(float value, float? deltaTime = null)
            {
                if(IsDamped) Check(Id)?.SetFloat(Id, value, DampTime, deltaTime??Time.deltaTime);
                else Check(Id)?.SetFloat(Id, value);
            }
        }

        [Serializable]
        public class Bool : AnimatorParameter
        {
            public Bool() : base(AnimatorControllerParameterType.Bool) { }
            public void Set(bool value) => Check(Id)?.SetBool(Id, value);
        }

        [Serializable]
        public class Trigger : AnimatorParameter
        {
            public Trigger() : base(AnimatorControllerParameterType.Trigger) { }
            public void Set() => Check(Id)?.SetTrigger(Id);
        }
    }
}
