using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MHDante.UnityUtils.Editor.Settings
{
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