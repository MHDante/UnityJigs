using System;
using Sirenix.OdinInspector;

namespace UnityJigs.SceneConstraints
{
    /// <summary>
    /// Marks a serialized list field as MAINTAINED BY the SceneConstraintRunner — it is rebuilt automatically
    /// on scene save / entering play / build (via the owner's <see cref="ISceneConstraint"/>), so it is shown
    /// read-only in the inspector. The runner being the only writer is what keeps the field's order
    /// (deepest-prefab → scene) and its prefab overrides deterministic and minimal — a hand edit would shift
    /// indices and churn the overrides.
    ///
    /// Bundles the Odin read-only rendering via [IncludeMyAttributes] (same trick as
    /// <see cref="UnityJigs.Attributes.AutoPopulateAttribute"/>), so no custom drawer is needed.
    /// </summary>
    [IncludeMyAttributes]
    [ReadOnly]
    [InfoBox("Maintained by SceneConstraintRunner — read-only (rebuilt on save / play / build).",
        InfoMessageType.None)]
    [AttributeUsage(AttributeTargets.Field)]
    public class SceneManagedAttribute : Attribute { }
}
