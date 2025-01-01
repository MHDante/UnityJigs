using UnityEngine;

namespace UnityJigs.Types
{
    public class Preference
    {
        private readonly string Key;
        private readonly bool IsEditor;

        public Preference(string key, bool isEditor)
        {
            IsEditor = isEditor;
            Key = key;
        }

        public abstract class Pref<T> : Preference
        {
            protected readonly T DefaultValue;

            protected Pref(bool isEditor, string key, T defaultValue) : base(key, isEditor) =>
                DefaultValue = defaultValue;

            public T Value
            {
                get => IsEditor ? EditorValue : RuntimeValue;
                set
                {
                    if (IsEditor) EditorValue = value;
                    else RuntimeValue = value;
                }
            }

            protected virtual T EditorValue
            {
                get => DefaultValue;
                // ReSharper disable once ValueParameterNotUsed
                set { }
            }

            protected abstract T RuntimeValue { get; set; }
        }

        public class String : Pref<string>
        {
            public String(bool isEditor, string key, string defaultValue) : base(isEditor, key, defaultValue) { }

#if UNITY_EDITOR
            protected override string EditorValue
            {
                get => UnityEditor.EditorPrefs.GetString(Key, DefaultValue);
                set => UnityEditor.EditorPrefs.SetString(Key, value);
            }
#endif

            protected override string RuntimeValue
            {
                get => PlayerPrefs.GetString(Key, DefaultValue);
                set => PlayerPrefs.SetString(Key, value);
            }
        }

        public class Int : Pref<int>
        {
            public Int(bool isEditor, string key, int defaultValue) : base(isEditor, key, defaultValue) { }

#if UNITY_EDITOR
            protected override int EditorValue
            {
                get => UnityEditor.EditorPrefs.GetInt(Key, DefaultValue);
                set => UnityEditor.EditorPrefs.SetInt(Key, value);
            }
#endif
            protected override int RuntimeValue
            {
                get => PlayerPrefs.GetInt(Key, DefaultValue);
                set => PlayerPrefs.SetInt(Key, value);
            }
        }

        public class Bool : Pref<bool>
        {
            public Bool(bool isEditor, string key, bool defaultValue) : base(isEditor, key, defaultValue) { }

#if UNITY_EDITOR
            protected override bool EditorValue
            {
                get => UnityEditor.EditorPrefs.GetBool(Key, DefaultValue);
                set => UnityEditor.EditorPrefs.SetBool(Key, value);
            }
#endif

            protected override bool RuntimeValue
            {
                get => PlayerPrefs.GetInt(Key, DefaultValue ? 1 : 0) != 0;
                set => PlayerPrefs.SetInt(Key, value ? 1 : 0);
            }
        }

        public class Float : Pref<float>
        {
            public Float(bool isEditor, string key, float defaultValue) : base(isEditor, key, defaultValue) { }

#if UNITY_EDITOR
            protected override float EditorValue
            {
                get => UnityEditor.EditorPrefs.GetFloat(Key, DefaultValue);
                set => UnityEditor.EditorPrefs.SetFloat(Key, value);
            }
#endif

            protected override float RuntimeValue
            {
                get => PlayerPrefs.GetFloat(Key, DefaultValue);
                set => PlayerPrefs.SetFloat(Key, value);
            }
        }
    }
}
