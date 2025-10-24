using System;

namespace UnityJigs.Attributes
{
    /// <summary>
    /// Draws an interactive handle in the Scene view for editing a normalized direction Vector3.
    /// Combines a draggable joystick-style tip with an optional Unity-style rotation handle overlay.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class DirectionHandle : Attribute
    {
        public string? ColorResolver { get; }
        public float Radius { get; }
        public bool ShowRotationHandle { get; }
        public bool UseLocalUp { get; }

        /// <param name="colorResolver">Odin resolver for handle color.</param>
        /// <param name="radius">Distance from origin to the handle tip.</param>
        /// <param name="showRotationHandle">If true, shows a tri-axis rotation handle overlay.</param>
        /// <param name="useLocalUp">If true, uses the transform’s local up vector instead of world up.</param>
        public DirectionHandle(string? colorResolver = null, float radius = 1f, bool showRotationHandle = false, bool useLocalUp = false)
        {
            ColorResolver = colorResolver;
            Radius = radius;
            ShowRotationHandle = showRotationHandle;
            UseLocalUp = useLocalUp;
        }
    }
}
