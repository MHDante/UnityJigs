using System;
using System.Diagnostics.CodeAnalysis;
using Sirenix.OdinInspector.Editor;

namespace UnityJigs.Editor.Odin
{
    public static class OdinUtils
    {
        [return: NotNullIfNotNull("defaultValue")]
        public static T? GetOrDefault<T>(this PropertyState s, string key, T? defaultValue = default) =>
            s.Exists<T>(key, out _) ? s.Get<T>(key) : defaultValue;

        public static T SetOrCreate<T>(this PropertyState s, string key, T value, bool persistent = false)
    {
        var exists = s.Exists<T>(key, out var isPersistent);
        if (!exists)
        {
            s.Create(key, persistent, value);
            return value;
        }

        if (isPersistent != persistent) throw new Exception("Key already exists with different persistence");
        s.Set(key, value);
        return value;
    }

        public static bool Exists<T>(this PropertyState s, string key) => Exists<T>(s, key, out var _);

        public static bool Exists<T>(this PropertyState s, string key, out bool isPersistent) =>
            s.Exists(key, out isPersistent, out Type type) && type == typeof(T);

        public static bool Toggle(this PropertyState s, string key, bool initialValue = false, bool persistent = false) =>
            s.SetOrCreate(key, !s.GetOrDefault(key, initialValue), persistent);
    }

}
