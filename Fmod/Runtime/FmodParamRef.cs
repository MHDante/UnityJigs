using System;
using UnityEngine;

namespace UnityJigs.Fmod
{
    /// <summary>
    /// Serializable reference to a single FMOD Event parameter (+ current value).
    /// Holds a non-serialized EventInstance for live preview and exposes Set().
    /// </summary>
    [Serializable]
    public struct FmodParam
    {
        // Parameter selection (by name) and authoring value:
        [SerializeField] public string Name; // parameter name chosen in the dropdown
        [SerializeField] public float Value; // authoring value (or last pushed)

        // A live instance you can bind at runtime or in-editor for preview.
        // Not serialized; you can re-bind on Awake/OnEnable/etc.
        [NonSerialized] public FMOD.Studio.EventInstance EventInstance;

        /// <summary>Set the parameter to a new value and push it to the bound EventInstance (if valid).</summary>
        public void Set(float value)
        {
            Value = value;
            if (!string.IsNullOrEmpty(Name) && EventInstance.isValid())
                EventInstance.setParameterByName(Name, value);
        }

        /// <summary>Convenience: bind a live EventInstance and immediately sync the current Value.</summary>
        public void Bind(FMOD.Studio.EventInstance instance)
        {
            EventInstance = instance;
            if (!string.IsNullOrEmpty(Name) && EventInstance.isValid())
                EventInstance.setParameterByName(Name, Value);
        }

        /// <summary>Try to pull the current value from the live instance (if any).</summary>
        public bool TryReadLive(out float liveValue)
        {
            liveValue = Value;
            if (string.IsNullOrEmpty(Name) || !EventInstance.isValid()) return false;
            // FMOD C# wrapper supports this call; ignores non-game-controlled params.
            EventInstance.getParameterByName(Name, out liveValue);
            return true;
        }
    }
}
