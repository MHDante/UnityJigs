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
    /// Drives every <see cref="ISceneConstraint"/> in a scene automatically: on scene save, on prefab-stage
    /// save, on entering play, and during build. Logs unfixable problems loudly (with a ping target); never
    /// blocks. This is what makes the [SceneManaged] lists self-maintaining instead of relying on manual
    /// "repopulate" buttons or OnValidate side-effects.
    ///
    /// The prefab-stage hook makes constraints hold for prefabs that are edited in Prefab Mode and only ever
    /// instantiated at runtime (no scene instance to catch them on scene save). The deepest-common-prefab
    /// recording in SceneConstraintApply already handles the stage correctly: a base prefab's contents have no
    /// corresponding source (entries write straight into the stage root), while a variant's contents are an
    /// instance of its base (base-authored entries record into the base asset, variant-added ones stay local).
    /// </summary>
    [InitializeOnLoad]
    public static class SceneConstraintRunner
    {
        static SceneConstraintRunner()
        {
            EditorSceneManager.sceneSaving += (scene, _) => Run(scene, "save");
            PrefabStage.prefabSaving += root => Run(root, "prefab-save");
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
            foreach (var root in scene.GetRootGameObjects()) Run(root, when);
        }

        // Root-scoped variant: what the prefab-stage hook uses, and what MCP/editor scripts call to enforce a
        // loaded prefab's contents (e.g. PrefabUtility.LoadPrefabContents root) without a scene.
        public static void Run(GameObject root, string when)
        {
            if (!root) return;
            // Never enforce while scripts are compiling/reloading: component queries can
            // come back empty in that window and a [SceneManaged] list would be "fixed"
            // to empty — silent data loss on save. Skipping is safe (next save catches up).
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            var ctx = new Context(when);
            foreach (var constraint in root.GetComponentsInChildren<ISceneConstraint>(true))
            {
                ctx.Current = constraint as Object;
                constraint.EnforceSceneConstraints(ctx);
            }
        }

        [MenuItem("Utils/Scene Constraints/Enforce Active Scene")]
        private static void EnforceActiveScene() => Run(SceneManager.GetActiveScene(), "manual");

        [MenuItem("Utils/Scene Constraints/Enforce Open Prefab Stage")]
        private static void EnforcePrefabStage()
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null) Run(stage.prefabContentsRoot, "manual");
        }

        // --- Build-time gate: re-run on the (copied) scene during a build so shipped data can't drift. ---
        private class BuildStep : IProcessSceneWithReport
        {
            public int callbackOrder => 0;
            public void OnProcessScene(Scene scene, BuildReport report) => Run(scene, "build");
        }

        // ------------------------------------------------------------------------------------------------
        private class Context : ISceneConstraintContext
        {
            private readonly string _when;
            public Object? Current;
            public Context(string when) => _when = when;

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
