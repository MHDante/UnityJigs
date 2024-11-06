using UnityEngine;

namespace MHDante.UnityUtils
{
    // ReSharper disable InconsistentNaming - Unity Naming
    // Used for interfaces that are implemented in Unit
    public interface IComponent
    {
        GameObject gameObject { get; }
        Transform transform { get; }
        Transform InteractionPoint { get; }
    }
}
