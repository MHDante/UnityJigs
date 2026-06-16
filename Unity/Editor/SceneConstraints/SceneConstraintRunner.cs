using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityJigs.SceneConstraints;
using Object = UnityEngine.Object;

namespace UnityJigs.Editor.SceneConstraints
{
    /// <summary>
    /// Drives every <see cref="ISceneConstraint"/> in a scene automatically: on scene save, on entering play,
    /// and during build. Logs unfixable problems loudly (with a ping target); never blocks. This is what makes
    /// the [SceneManaged] lists self-maintaining instead of relying on manual "repopulate" buttons or
    /// OnValidate side-effects.
    /// </summary>
    [InitializeOnLoad]
    public static class SceneConstraintRunner
    {
        static SceneConstraintRunner()
        {
            EditorSceneManager.sceneSaving += (scene, _) => Run(scene, "save");
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.ExitingEditMode) return;
                for (var i = 0; i < SceneManager.sceneCount; i++) Run(SceneManager.GetSceneAt(i), "play");
            };
        }

        // Public so it can be invoked on demand (menu / tests / MCP) as well as from the hooks.
        public static void Run(Scene scene, string when)
        {
            if (!scene.IsValid() || !scene.isLoaded) return;
            var ctx = new Context(scene, when);
            foreach (var root in scene.GetRootGameObjects())
            foreach (var constraint in root.GetComponentsInChildren<ISceneConstraint>(true))
            {
                ctx.Current = constraint as Object;
                constraint.EnforceSceneConstraints(ctx);
            }
        }

        [MenuItem("Utils/Scene Constraints/Enforce Active Scene")]
        private static void EnforceActiveScene() => Run(SceneManager.GetActiveScene(), "manual");

        // --- Build-time gate: re-run on the (copied) scene during a build so shipped data can't drift. ---
        private class BuildStep : IProcessSceneWithReport
        {
            public int callbackOrder => 0;
            public void OnProcessScene(Scene scene, BuildReport report) => Run(scene, "build");
        }

        // ------------------------------------------------------------------------------------------------
        private class Context : ISceneConstraintContext
        {
            private readonly Scene _scene;
            private readonly string _when;
            public Object? Current;
            public Context(Scene scene, string when) { _scene = scene; _when = when; }

            public void Problem(Object context, string message) =>
                Debug.LogError($"[SceneConstraint/{_when}] {message}", context ? context : Current);

            public void ApplyManagedList<T>(Component owner, string fieldName, IReadOnlyList<T> members)
                where T : Object
            {
                var ordered = SceneConstraintApply.Order(owner, members);
                SceneConstraintApply.Apply(owner, fieldName, ordered, _when);
            }

            public void ApplyManagedDict<TKey, TValue>(Component owner, string fieldName,
                IReadOnlyList<TValue> values, Func<TValue, TKey> keySelector) where TValue : Object
            {
                var ordered = SceneConstraintApply.Order(owner, values);
                SceneConstraintApply.ApplyDict(owner, fieldName, ordered, o => (object)keySelector((TValue)o), _when);
            }
        }
    }
}
