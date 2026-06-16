using System.Collections.Generic;
using UnityEngine;

namespace UnityJigs.SceneConstraints
{
    /// <summary>
    /// Implemented by a component that has scene-configuration invariants to enforce — typically a manager
    /// whose serialized [SceneManaged] list must mirror a set of components in the scene/prefab. The
    /// SceneConstraintRunner calls <see cref="EnforceSceneConstraints"/> automatically on scene save, on
    /// entering play, and during build. Gather the members however suits (subtree GetComponentsInChildren,
    /// or a scene-wide FindObjectsByType) and hand them to <paramref name="ctx"/>; report anything that
    /// cannot be auto-fixed via <see cref="ISceneConstraintContext.Problem"/>.
    /// </summary>
    public interface ISceneConstraint
    {
        void EnforceSceneConstraints(ISceneConstraintContext ctx);
    }

    /// <summary>
    /// The editor-side services handed to an <see cref="ISceneConstraint"/> while it runs. Keeps the
    /// component free of any UnityEditor dependency — the implementation (in the runner) owns the prefab
    /// surgery and logging.
    /// </summary>
    public interface ISceneConstraintContext
    {
        /// <summary>
        /// Make the owner's [SceneManaged] list field (named <paramref name="fieldName"/>) contain exactly
        /// <paramref name="members"/>. The runner sorts them deepest-common-prefab → scene and records each
        /// entry at the deepest prefab that contains BOTH the owner and that member, so a relationship
        /// authored in a base prefab lives in the base (inherited by every instance) and only later-added
        /// members become higher-level/scene overrides. Pass the owning component as <paramref name="owner"/>
        /// and the field name via nameof.
        /// </summary>
        void ApplyManagedList<T>(Component owner, string fieldName, IReadOnlyList<T> members) where T : Object;

        /// <summary>An invariant that could not be auto-fixed. Logged loudly with <paramref name="context"/>
        /// as the ping target. Does not block the save/play/build.</summary>
        void Problem(Object context, string message);
    }
}
