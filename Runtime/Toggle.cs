using System;
using System.Diagnostics.CodeAnalysis;
using Sirenix.OdinInspector;

namespace MHDante.UnityUtils
{
    [Serializable, InlineProperty]
    public struct Toggle<T>
    {
        [HorizontalGroup(width:15f), LabelText("")]
        public bool Enabled;
        [HorizontalGroup, LabelText(""), EnableIf("Enabled")]
        public T Value;

        public Toggle(bool enabled, T value) => (Enabled, Value) = (enabled, value);
        public static implicit operator T(Toggle<T> t) => t.Value;

        [return: NotNullIfNotNull(nameof(defaultValue))]
        public T? GetValueOrDefault(T? defaultValue = default) => Enabled ? Value : defaultValue;
    }
}
