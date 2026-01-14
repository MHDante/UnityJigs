using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Scripting;

namespace UnityJigs.Types
{
    [Serializable]
    public sealed class FormattedString<TTuple> : ISerializationCallbackReceiver
        where TTuple : struct
    {
        public FormattedString Base = new();

        [NonSerialized] private bool _isInitialized;

        private delegate void ApplyDelegate(FormattedString baseString, TTuple values);

        private static readonly int Arity;
        private static readonly Type[] ElementTypes;
        private static readonly ApplyDelegate Apply;

        static FormattedString()
        {
            (Arity, ElementTypes) = GetTupleShape(typeof(TTuple));
            ValidateSupportedTypes(ElementTypes);
            Apply = CreateApplyDelegate(Arity, ElementTypes);
        }

        public void Set(TTuple values)
        {
            EnsureInitialized();
            Apply(Base, values);
        }

        public bool UpdateIfChanged()
        {
            EnsureInitialized();
            return Base.UpdateIfChanged();
        }

        public void EnsureInitialized()
        {
            if (_isInitialized)
                return;

            EnsureArgumentsMatchTupleShape();
            _isInitialized = true;
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize() => _isInitialized = false;

        private void EnsureArgumentsMatchTupleShape()
        {
            var args = Base.Arguments;

            if (args.Count != Arity)
            {
                args.Clear();

                for (var i = 0; i < Arity; i++)
                    args.Add(CreateArgForType(ElementTypes[i]));

                Base.Validate();
                return;
            }

            for (var i = 0; i < Arity; i++)
            {
                var expected = ElementTypes[i];
                var current = args[i];

                if (!IsArgCompatible(current, expected))
                    args[i] = CreateArgForType(expected);
            }

            Base.Validate();
        }

        private static bool IsArgCompatible(TemplateArg? arg, Type expectedValueType)
        {
            if (arg == null)
                return false;

            if (expectedValueType == typeof(int))
                return arg is TemplateValueArg<int>;

            if (expectedValueType == typeof(float))
                return arg is TemplateValueArg<float>;

            if (expectedValueType == typeof(string))
                return arg is TemplateValueArg<string>;

            return false;
        }

        private static TemplateArg CreateArgForType(Type valueType) =>
            valueType == typeof(int) ? new TemplateIntArg() :
            valueType == typeof(float) ? new TemplateFloatArg() :
            valueType == typeof(string) ? new TemplateStringArg() :
            throw new NotSupportedException(
                $"Unsupported tuple element type '{valueType.FullName}'. Supported: int, float, string.");

        private static void ValidateSupportedTypes(Type[] elementTypes)
        {
            foreach (var t in elementTypes)
            {
                if (t != typeof(int) && t != typeof(float) && t != typeof(string))
                    throw new NotSupportedException(
                        $"Unsupported tuple element type '{t.FullName}'. Supported: int, float, string.");
            }
        }

        private static ApplyDelegate CreateApplyDelegate(int arity, Type[] elementTypes)
        {
            var methodName = arity switch
            {
                1 => nameof(Apply1),
                2 => nameof(Apply2),
                3 => nameof(Apply3),
                4 => nameof(Apply4),
                _ => throw new NotSupportedException($"Unsupported tuple arity {arity}. Supported: 1..4.")
            };

            var open = typeof(FormattedString<TTuple>).GetMethod(methodName,
                BindingFlags.NonPublic | BindingFlags.Static);
            if (open == null)
                throw new MissingMethodException(typeof(FormattedString<TTuple>).FullName, methodName);

            var closed = open.MakeGenericMethod(elementTypes);
            return (ApplyDelegate)Delegate.CreateDelegate(typeof(ApplyDelegate), closed);
        }

        private static (int arity, Type[] elementTypes) GetTupleShape(Type t)
        {
            if (!t.IsGenericType)
                throw new NotSupportedException($"TTuple must be a ValueTuple. Got non-generic type '{t.FullName}'.");

            var def = t.GetGenericTypeDefinition();
            var args = t.GetGenericArguments();

            return def == typeof(ValueTuple<>) ? (1, args) :
                def == typeof(ValueTuple<,>) ? (2, args) :
                def == typeof(ValueTuple<,,>) ? (3, args) :
                def == typeof(ValueTuple<,,,>) ? (4, args) :
                throw new NotSupportedException($"TTuple must be a ValueTuple arity 1..4. Got '{t.FullName}'.");
        }

        // These ApplyN methods avoid boxing by reinterpreting the tuple as a ValueTuple<T...> using MemoryMarshal.
        // This stays AOT-friendly and doesn't rely on ISpanFormattable.

        [Preserve]
        private static void Apply1<T0>(FormattedString baseString, TTuple values)
        {
            ref readonly var t =
                ref MemoryMarshal.Cast<TTuple, ValueTuple<T0>>(MemoryMarshal.CreateSpan(ref values, 1))[0];
            ((TemplateValueArg<T0>)baseString.Arguments[0]).Value = t.Item1;
        }

        [Preserve]
        private static void Apply2<T0, T1>(FormattedString baseString, TTuple values)
        {
            ref readonly var t =
                ref MemoryMarshal.Cast<TTuple, ValueTuple<T0, T1>>(MemoryMarshal.CreateSpan(ref values, 1))[0];
            ((TemplateValueArg<T0>)baseString.Arguments[0]).Value = t.Item1;
            ((TemplateValueArg<T1>)baseString.Arguments[1]).Value = t.Item2;
        }

        [Preserve]
        private static void Apply3<T0, T1, T2>(FormattedString baseString, TTuple values)
        {
            ref readonly var t =
                ref MemoryMarshal.Cast<TTuple, ValueTuple<T0, T1, T2>>(MemoryMarshal.CreateSpan(ref values, 1))[0];
            ((TemplateValueArg<T0>)baseString.Arguments[0]).Value = t.Item1;
            ((TemplateValueArg<T1>)baseString.Arguments[1]).Value = t.Item2;
            ((TemplateValueArg<T2>)baseString.Arguments[2]).Value = t.Item3;
        }

        [Preserve]
        private static void Apply4<T0, T1, T2, T3>(FormattedString baseString, TTuple values)
        {
            ref readonly var t =
                ref MemoryMarshal.Cast<TTuple, ValueTuple<T0, T1, T2, T3>>(MemoryMarshal.CreateSpan(ref values, 1))[0];
            ((TemplateValueArg<T0>)baseString.Arguments[0]).Value = t.Item1;
            ((TemplateValueArg<T1>)baseString.Arguments[1]).Value = t.Item2;
            ((TemplateValueArg<T2>)baseString.Arguments[2]).Value = t.Item3;
            ((TemplateValueArg<T3>)baseString.Arguments[3]).Value = t.Item4;
        }
    }

    [Serializable]
    public sealed class FormattedString : ISerializationCallbackReceiver
    {
        public string Template = string.Empty;

        [SerializeReference] public List<TemplateArg> Arguments = new();

        public bool UseInvariantCulture = true;

        [NonSerialized] public string Value = string.Empty;
        [NonSerialized] public bool IsTemplateValid = true;
        [NonSerialized] public string TemplateError = string.Empty;

        [NonSerialized] private object[] _argsArray = Array.Empty<object>();
        [NonSerialized] private bool _hasProcessedTemplate;
        [NonSerialized] private string _lastProcessedTemplate = string.Empty;

        [NonSerialized] private bool _validationDirty = true;

        public bool UpdateIfChanged()
        {
            if (_validationDirty)
                Validate();

            if (!IsTemplateValid)
                return false;

            EnsureArgsArray();

            var dirty = !_hasProcessedTemplate ||
                        !string.Equals(Template, _lastProcessedTemplate, StringComparison.Ordinal);

            for (var i = 0; i < Arguments.Count; i++)
            {
                var arg = Arguments[i];
                _argsArray[i] = arg!;

                if (!dirty && arg is { IsDirty: true })
                    dirty = true;
            }

            if (!dirty)
                return false;

            var provider = UseInvariantCulture ? CultureInfo.InvariantCulture : CultureInfo.CurrentCulture;

            try
            {
                Value = string.Format(provider, Template, _argsArray);
            }
            catch (FormatException ex)
            {
                IsTemplateValid = false;
                TemplateError = ex.Message;
                return false;
            }

            _hasProcessedTemplate = true;
            _lastProcessedTemplate = Template;

            foreach (var t in Arguments)
                t?.MarkProcessed();

            return true;
        }

        public void Validate()
        {
            _validationDirty = false;

            var result = TemplateValidator.Validate(Template, Arguments.Count);
            IsTemplateValid = result.IsValid;
            TemplateError = result.Error;
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            _validationDirty = true;
            _hasProcessedTemplate = false;
            _argsArray = Array.Empty<object>();
            Value = string.Empty;
        }

        private void EnsureArgsArray()
        {
            if (_argsArray.Length == Arguments.Count)
                return;

            _argsArray = Arguments.Count == 0 ? Array.Empty<object>() : new object[Arguments.Count];
            _validationDirty = true;
        }
    }
    
    public static class TemplateValidator
    {
        public readonly struct Result
        {
            public readonly bool IsValid;
            public readonly string Error;

            public Result(bool isValid, string error)
            {
                IsValid = isValid;
                Error = error;
            }
        }

        public static Result Validate(string template, int argumentCount)
        {
            for (var i = 0; i < template.Length; i++)
            {
                var c = template[i];

                switch (c)
                {
                    case '{' when i + 1 < template.Length && template[i + 1] == '{':
                        i++;
                        continue;
                    case '{':
                    {
                        i++;
                        if (i >= template.Length)
                            return new Result(false, "Unterminated '{' in template.");

                        SkipWhitespace(template, ref i);

                        if (!TryReadInt(template, ref i, out var index))
                            return new Result(false, "Invalid placeholder: expected an index after '{'.");

                        SkipWhitespace(template, ref i);

                        if (i < template.Length && template[i] == ',')
                        {
                            i++;
                            SkipWhitespace(template, ref i);

                            if (i < template.Length && (template[i] == '+' || template[i] == '-'))
                                i++;

                            if (!TryReadInt(template, ref i, out _))
                                return new Result(false,
                                    "Invalid placeholder: alignment expects an integer after ','.");
                        }

                        SkipWhitespace(template, ref i);

                        if (i < template.Length && template[i] == ':')
                        {
                            i++;
                            while (i < template.Length && template[i] != '}')
                            {
                                if (template[i] == '{')
                                    return new Result(false,
                                        "Invalid placeholder: nested '{' inside a format specifier.");
                                i++;
                            }
                        }

                        if (i >= template.Length || template[i] != '}')
                            return new Result(false, "Unterminated '{...}' placeholder (missing '}').");

                        if (index < 0)
                            return new Result(false, "Placeholder index must be >= 0.");

                        if (index >= argumentCount)
                            return new Result(false,
                                $"Placeholder index {{{index}}} has no matching argument (count={argumentCount}).");
                        break;
                    }
                    case '}' when i + 1 < template.Length && template[i + 1] == '}':
                        i++;
                        continue;
                    case '}':
                        return new Result(false, "Unexpected '}' in template. Use '}}' to escape.");
                }
            }

            return new Result(true, string.Empty);
        }

        private static void SkipWhitespace(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i]))
                i++;
        }

        private static bool TryReadInt(string s, ref int i, out int value)
        {
            value = 0;

            var start = i;
            while (i < s.Length && char.IsDigit(s[i]))
            {
                value = unchecked(value * 10 + (s[i] - '0'));
                i++;
            }

            return i != start;
        }
    }

    [Serializable]
    public abstract class TemplateArg : IFormattable
    {
        public string Name = string.Empty;

        public abstract bool IsDirty { get; }

        public abstract void MarkProcessed();

        public abstract string ToString(string? format, IFormatProvider? formatProvider);

        public override string ToString() => ToString(null, CultureInfo.CurrentCulture);
    }

    [Serializable]
    public abstract class TemplateValueArg<T> : TemplateArg
    {
        public T Value = default!;

        [NonSerialized] private bool _hasProcessedValue;
        [NonSerialized] private T _lastProcessedValue = default!;

        public override bool IsDirty
            => !_hasProcessedValue || !EqualityComparer<T>.Default.Equals(Value, _lastProcessedValue);

        public override void MarkProcessed()
        {
            _lastProcessedValue = Value;
            _hasProcessedValue = true;
        }
    }

    [Serializable]
    public sealed class TemplateIntArg : TemplateValueArg<int>
    {
        public override string ToString(string? format, IFormatProvider? formatProvider)
            => Value.ToString(format, formatProvider);
    }

    [Serializable]
    public sealed class TemplateFloatArg : TemplateValueArg<float>
    {
        public override string ToString(string? format, IFormatProvider? formatProvider)
            => Value.ToString(format, formatProvider);
    }

    [Serializable]
    public sealed class TemplateStringArg : TemplateValueArg<string>
    {
        public TemplateStringArg() => Value = string.Empty;

        public override string ToString(string? format, IFormatProvider? formatProvider) => Value;
    }

    // =========================================================
    // Typed wrapper: FormattedString<TTuple>
    // Reflection picks a closed ApplyN<...> once per closed generic type.
    // Hot path is: EnsureInitialized (once) + Apply delegate + Base.UpdateIfChanged
    // =========================================================
}