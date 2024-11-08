using UnityEngine;

namespace MHDante.UnityUtils.Types
{
    // ReSharper disable InconsistentNaming - Unity Naming
    // Used for interfaces that are implemented in Unity
    public interface IComponent
    {
        GameObject gameObject { get; }
        Transform transform { get; }
    }
}
