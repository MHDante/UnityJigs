using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace UnityJigs.Assistant.Editor
{
    /// <summary>
    /// Shrinks the Unity AI Assistant's RunCommand "wait for editor to be ready" timeout from its
    /// default 120s down to <see cref="TimeoutSeconds"/>. RunCommand calls
    /// <c>EditorReadyHelper.RefreshAndWaitForReady()</c> first; if the editor is compiling/updating it
    /// waits up to that timeout, then returns a retryable COMPILATION_IN_PROGRESS. The default 120s means
    /// a stuck/deferred compile (e.g. while in play mode) blocks for two minutes before erroring — this
    /// surfaces it fast instead.
    ///
    /// Sibling to <see cref="McpAssemblyInjector"/> / <see cref="RunCommandNamespaceUnblocker"/>: pure
    /// reflection against the internal <c>EditorReadyHelper.DefaultTimeout</c> static-readonly field,
    /// re-applied after every domain reload. No-op (and silent) if the AI Assistant package isn't
    /// installed or the field has moved — safe to ship in projects that don't have it.
    /// </summary>
    [InitializeOnLoad]
    static class RunCommandTimeoutOverride
    {
        const double TimeoutSeconds = 20;
        const string HelperTypeName = "Unity.AI.MCP.Editor.Helpers.EditorReadyHelper";
        const string FieldName = "DefaultTimeout";

        static RunCommandTimeoutOverride()
        {
            // afterAssemblyReload fires at the end of every domain reload on the main thread regardless of
            // focus (unlike delayCall, which is starved while the Editor is unfocused — the headless-MCP
            // case); delayCall is kept only as a cold-open fallback. Apply early-returns once set, so both
            // hooks firing in one domain is harmless.
            AssemblyReloadEvents.afterAssemblyReload += Apply;
            EditorApplication.delayCall += Apply;
        }

        static void Apply()
        {
            try
            {
                var helper = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => { try { return a.GetTypes(); } catch { return Type.EmptyTypes; } })
                    .FirstOrDefault(t => t.FullName == HelperTypeName);
                if (helper == null) return; // AI Assistant not installed — nothing to do.

                var field = helper.GetField(FieldName, BindingFlags.NonPublic | BindingFlags.Static);
                if (field == null || field.FieldType != typeof(TimeSpan)) return;

                if (field.GetValue(null) is not TimeSpan current) return;
                var target = TimeSpan.FromSeconds(TimeoutSeconds);
                if (current == target) return; // already applied this domain

                field.SetValue(null, target);
                Debug.Log($"[RunCommandTimeoutOverride] RunCommand editor-ready timeout set to {TimeoutSeconds}s (was {current.TotalSeconds}s).");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RunCommandTimeoutOverride] Failed to override timeout: {e.Message}");
            }
        }
    }
}
