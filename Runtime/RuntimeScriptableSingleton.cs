using System;
using UnityEngine;

namespace MHDante.UnityUtils
{
    public class RuntimeScriptableSingleton<T> : ScriptableObject where T : RuntimeScriptableSingleton<T>
    {
        public static T Instance => UnityUtils.GetPreloadedSingleton(ref _Instance);
        private static T _Instance = null!;

        protected virtual void OnEnable()
        {
            if (!_Instance) _Instance = (T)this;
            else throw new Exception("Multiple Singletons Initialized");
        }
    }
}
