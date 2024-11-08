using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MHDante.UnityUtils.Editor
{
    public abstract class SettingsSingleton<T> :  ScriptableSingleton<T>, ISettingsSingleton where T : SettingsSingleton<T>
    {
        protected abstract string Title { get; }
        protected abstract SettingsScope Scope { get; }
        protected virtual IEnumerable<string>? Keywords => null;
        protected void Awake() => Save(true);

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
            EditorGUI.BeginChangeCheck();
            context.CachedEditor?.OnInspectorGUI();
            if (EditorGUI.EndChangeCheck()) instance.Save(true);
        }

        protected class SettingsContext
        {
            public UnityEditor.Editor? CachedEditor;
            public string? SearchContext = null!;
        }
    }

    internal interface ISettingsSingleton
    {
        public SettingsProvider MakeProvider();

        [SettingsProviderGroup]
        public static SettingsProvider[] GetProviders()
        {
            var targetTypes = TypeCache.GetTypesDerivedFrom(typeof(SettingsSingleton<>));
            if (targetTypes.Count == 0) return Array.Empty<SettingsProvider>();

            List<SettingsProvider> result = new();

            foreach (var targetType in targetTypes)
            {
                var singletonType = typeof(ScriptableSingleton<>).MakeGenericType(targetType);
                var instanceProp = singletonType.GetProperty("instance");
                var instance = instanceProp?.GetValue(null) as ISettingsSingleton;

                if (instance == null)
                {
                    Debug.LogError("Type Error: " + targetType.Name);
                    continue;
                }

                var newProvider = instance.MakeProvider();
                result.Add(newProvider);
            }

            return result.ToArray();
        }

    }

}
