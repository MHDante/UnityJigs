using UnityEditor;
using UnityEngine;

namespace UnityJigs.Types
{
    public class Preference
    {
        private readonly string _key;
        private readonly bool _isEditor;

        public Preference(string key, bool isEditor)
        {
            _isEditor = isEditor;
            _key = key;
        }

        public abstract class Pref<T> : Preference
        {
            protected readonly T DefaultValue;

            protected Pref(bool isEditor, string key, T defaultValue) : base(key, isEditor) =>
                DefaultValue = defaultValue;

            public T Value
            {
                get => _isEditor ? EditorValue : RuntimeValue;
                set
                {
                    if (_isEditor) EditorValue = value;
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
                get => EditorPrefs.GetString(_key, DefaultValue);
                set => EditorPrefs.SetString(_key, value);
            }
#endif

            protected override string RuntimeValue
            {
                get => PlayerPrefs.GetString(_key, DefaultValue);
                set => PlayerPrefs.SetString(_key, value);
            }
        }

        public class Int : Pref<int>
        {
            public Int(bool isEditor, string key, int defaultValue) : base(isEditor, key, defaultValue) { }

#if UNITY_EDITOR
            protected override int EditorValue
            {
                get => EditorPrefs.GetInt(_key, DefaultValue);
                set => EditorPrefs.SetInt(_key, value);
            }
#endif
            protected override int RuntimeValue
            {
                get => PlayerPrefs.GetInt(_key, DefaultValue);
                set => PlayerPrefs.SetInt(_key, value);
            }
        }

        public class Bool : Pref<bool>
        {
            public Bool(bool isEditor, string key, bool defaultValue) : base(isEditor, key, defaultValue) { }

#if UNITY_EDITOR
            protected override bool EditorValue
            {
                get => EditorPrefs.GetBool(_key, DefaultValue);
                set => EditorPrefs.SetBool(_key, value);
            }
#endif

            protected override bool RuntimeValue
            {
                get => PlayerPrefs.GetInt(_key, DefaultValue ? 1 : 0) != 0;
                set => PlayerPrefs.SetInt(_key, value ? 1 : 0);
            }
        }

        public class Float : Pref<float>
        {
            public Float(bool isEditor, string key, float defaultValue) : base(isEditor, key, defaultValue) { }

#if UNITY_EDITOR
            protected override float EditorValue
            {
                get => EditorPrefs.GetFloat(_key, DefaultValue);
                set => EditorPrefs.SetFloat(_key, value);
            }
#endif

            protected override float RuntimeValue
            {
                get => PlayerPrefs.GetFloat(_key, DefaultValue);
                set => PlayerPrefs.SetFloat(_key, value);
            }
        }
    }
}
