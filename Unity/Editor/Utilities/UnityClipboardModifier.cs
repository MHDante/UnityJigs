using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace UnityJigs.Editor.Utilities
{
    public static class UnityClipboardModifier
    {
        ///<see cref="UnityEditor.Clipboard"/>
        private const string SourceClass = "UnityEditor.Clipboard";

        ///<summary>
        /// Expected Suffix of properties such as:
        /// <see cref="Clipboard.integerValue"/>
        /// <see cref="Clipboard.colorValue"/>
        /// </summary>
        const string ValueSuffix = "Value";

        ///<summary>
        /// Expected Prefix of properties such as:
        /// <see cref="Clipboard.hasBool"/>
        /// <see cref="Clipboard.hasGuid"/>
        /// <see cref="Clipboard.hasHash128"/>
        /// </summary>
        const string HasPrefix = "has";

        private static readonly Dictionary<string, Type> TypesByShortName = new();
        private static readonly Dictionary<Type, (object gettter, object setter)> Accessors = new();
        private static readonly Dictionary<Type, Func<bool>> Checkers = new();

        static UnityClipboardModifier()
    {
        var cls = typeof(UnityEditor.Editor).Assembly.GetType(SourceClass);
        if (cls == null) return;
        var props = cls.GetProperties(BindingFlags.Static | BindingFlags.Public);
        foreach (var prop in props)
        {
            if (!prop.Name.EndsWith(ValueSuffix) || !prop.CanRead || !prop.CanWrite) continue;

            var type = prop.PropertyType;
            if (Accessors.ContainsKey(type))
            {
                Debug.LogWarning($"Two Getters for same clipboard Type: {type.Name}");
                continue;
            }

            var getterType = typeof(Func<>).MakeGenericType(type);
            var getter = prop.GetGetMethod().CreateDelegate(getterType);
            var setterType = typeof(Action<>).MakeGenericType(type);
            var setter = prop.GetSetMethod().CreateDelegate(setterType);
            Accessors.Add(type, (getter, setter));

            // If two properties had the same shortname, they'd have the same name, so add won't throw.
            var shortName = prop.Name[..^ValueSuffix.Length].ToLowerInvariant();
            TypesByShortName.Add(shortName, type);
        }

        foreach (var prop in props)
        {
            if (!prop.Name.StartsWith(HasPrefix) || !prop.CanRead || prop.PropertyType != typeof(bool)) continue;
            var shortName = prop.Name[HasPrefix.Length..].ToLowerInvariant();
            if (!TypesByShortName.TryGetValue(shortName, out var type))
            {
                Debug.LogWarning($"Shortname not recognized{shortName}");
                continue;
            }

            var checker = (Func<bool>)prop.GetGetMethod().CreateDelegate(typeof(Func<bool>));
            Checkers.Add(type, checker);
        }
    }

        public static bool CanCopyPaste<T>() => CanCopyPaste(typeof(T));
        public static bool CanCopyPaste(Type t) => Accessors.ContainsKey(t);

        public static void Copy<T>(T value)
    {
        var setter = Accessors[typeof(T)].setter;
        var fn = (Action<T>)setter;
        fn(value);
    }

        public static T Paste<T>()
    {
        var getter = Accessors[typeof(T)].gettter;
        var fn = (Func<T>)getter;
        return fn();
    }

        public static bool IsReadyToPaste<T>()
    {
        if (!Accessors.ContainsKey(typeof(T))) return false;
        if (!Checkers.TryGetValue(typeof(T), out var fn)) return true;
        return fn();
    }
    }
}
