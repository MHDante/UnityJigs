using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UnityUtils.Editor.Settings
{
    public abstract class SettingsSingleton<T> :  ScriptableSingleton<T>, ISettingsSingleton where T : SettingsSingleton<T>
    {
        protected abstract string Title { get; }
        protected abstract SettingsScope Scope { get; }
        protected virtual IEnumerable<string>? Keywords => null;

        protected virtual void OnEnable()
        {
            if (this == instance) return;
            Debug.Log("Duplicate Instance of" + this);
            DestroyImmediate(this);
        }

        public virtual SettingsProvider MakeProvider()
        {
            SettingsContext context = new();
            return new SettingsProvider(instance.Title, instance.Scope, instance.Keywords)
            {
                guiHandler = s =>
                {
                    context.SearchContext = s;
                    instance.OnSettingsGUI(context);
                }
            };
        }

        protected virtual void OnSettingsGUI(SettingsContext context)
        {
            UnityEditor.Editor.CreateCachedEditor(instance, null, ref context.CachedEditor);
            context.CachedEditor?.OnInspectorGUI();
            if (EditorUtility.IsDirty(instance))
                instance.Save(true);
        }

    }
}
