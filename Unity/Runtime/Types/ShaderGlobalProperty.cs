using System;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Serialization;

namespace UnityJigs.Types
{
    [Serializable, LabelText(""), InlineProperty,
     OnValueChanged("@$value." + nameof(OnValueChanged) + "()", true),
     InlineButton("@$value." + nameof(Apply) + "()", label: nameof(Apply), ShowIf = "@!$value." + nameof(AutoApply))]
    public abstract class GlobalShaderProp
    {
        protected readonly int PropertyId;
        protected readonly bool AutoApply;

        private protected GlobalShaderProp(string propertyName, bool autoApply = true)
        {
            PropertyId = Shader.PropertyToID(propertyName);
            AutoApply = autoApply;
        }

        private void OnValueChanged()
        {
            if (AutoApply) Apply();
        }

        public abstract void Apply();

        [Serializable]
        public sealed class Float : GlobalShaderProp
        {
            public readonly float Min;
            public readonly float Max;

            private bool HasRange => Min != float.MinValue && Max != float.MaxValue;

            [ShowInInspector, LabelText("@$property.Parent.Label"), MinValue(nameof(Min)), MaxValue(nameof(Max)),
             HideIf(nameof(HasRange))]
            public float Value
            {
                get => FloatValue;
                set => FloatValue = Mathf.Clamp(value, Min, Max);
            }


            [ShowInInspector, LabelText("@$property.Parent.Label"), PropertyRange(nameof(Min), nameof(Max)),
             ShowIf(nameof(HasRange))]
            private float ClampValue
            {
                get => Value;
                set => Value = value;
            }

            [SerializeField, HideInInspector, FormerlySerializedAs("Value")]
            public float FloatValue;

            public Float(string propertyName, float defaultValue = 0f, float min = float.MinValue,
                float max = float.MaxValue, bool autoApply = true)
                : base(propertyName, autoApply)
            {
                Min = min;
                Max = max;
                Value = defaultValue;
            }

            public override void Apply() => Shader.SetGlobalFloat(PropertyId, Value);
        }


        [Serializable]
        public sealed class Int : GlobalShaderProp
        {
            [LabelText("@$property.Parent.Label")] public int Value;

            public Int(string propertyName, int defaultValue = 0, bool autoApply = true)
                : base(propertyName, autoApply) => Value = defaultValue;

            public override void Apply() => Shader.SetGlobalInt(PropertyId, Value);
        }

        [Serializable]
        public sealed class Color : GlobalShaderProp
        {
            [LabelText("@$property.Parent.Label")] public UnityEngine.Color Value;

            public Color(string propertyName, UnityEngine.Color defaultValue = default, bool autoApply = true)
                : base(propertyName, autoApply) => Value = defaultValue;

            public override void Apply() => Shader.SetGlobalColor(PropertyId, Value);
        }

        [Serializable]
        public sealed class Vector2 : GlobalShaderProp
        {
            [LabelText("@$property.Parent.Label")] public UnityEngine.Vector2 Value;

            public Vector2(string propertyName, UnityEngine.Vector2 defaultValue = default, bool autoApply = true)
                : base(propertyName, autoApply) => Value = defaultValue;

            public override void Apply() => Shader.SetGlobalVector(PropertyId, new(Value.x, Value.y, 0f, 0f));
        }

        [Serializable]
        public sealed class Vector3 : GlobalShaderProp
        {
            [LabelText("@$property.Parent.Label")] public UnityEngine.Vector3 Value;

            public Vector3(string propertyName, UnityEngine.Vector3 defaultValue = default, bool autoApply = true)
                : base(propertyName, autoApply) => Value = defaultValue;

            public override void Apply() => Shader.SetGlobalVector(PropertyId, new(Value.x, Value.y, Value.z, 0f));
        }

        [Serializable]
        public sealed class Vector4 : GlobalShaderProp
        {
            [LabelText("@$property.Parent.Label")] public UnityEngine.Vector4 Value;

            public Vector4(string propertyName, UnityEngine.Vector4 defaultValue = default, bool autoApply = true)
                : base(propertyName, autoApply) => Value = defaultValue;

            public override void Apply() => Shader.SetGlobalVector(PropertyId, Value);
        }

        [Serializable]
        public sealed class Texture : GlobalShaderProp
        {
            [LabelText("@$property.Parent.Label")] public UnityEngine.Texture? Value;

            public Texture(string propertyName, UnityEngine.Texture? defaultValue = null, bool autoApply = true)
                : base(propertyName, autoApply) => Value = defaultValue;

            public override void Apply() => Shader.SetGlobalTexture(PropertyId, Value);
        }
    }
}