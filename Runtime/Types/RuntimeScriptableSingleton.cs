using System;
using UnityEngine;

namespace UnityJigs.Types
{
    public class RuntimeScriptableSingleton<T> : ScriptableObject where T : RuntimeScriptableSingleton<T>
    {
        public static T Instance => Extensions.UnityUtils.GetPreloadedSingleton(ref _Instance);
        private static T _Instance = null!;

        protected virtual void OnEnable()
        {
            if (!_Instance) _Instance = (T)this;
            else throw new Exception("Multiple Singletons Initialized");
        }
    }
}
