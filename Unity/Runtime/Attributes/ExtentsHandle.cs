using System;

namespace UnityJigs.Attributes
{
    /// <summary>
    /// Draws an interactive symmetric 3D bounds handle for editing a Vector3 field in the Scene view.
    /// The value represents half-extents in local space. Supports dynamic color resolution via Odin.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class ExtentsHandle : Attribute
    {
        public string? ColorResolver { get; }
        public bool DrawWire { get; }

        /// <param name="colorResolver">Odin color resolver</param>
        /// <param name="drawWire">if true wire is drawn</param>
        public ExtentsHandle(string? colorResolver = null, bool drawWire = true)
        {
            ColorResolver = colorResolver;
            DrawWire = drawWire;
        }
    }
}
