using System;
using System.Reflection;
using System.Threading.Tasks;
using Unity.AI.MCP.Editor.Helpers;
using Unity.AI.MCP.Editor.ToolRegistry;
using UnityEditor;
using UnityEngine;

namespace UnityJigs.Assistant.Editor
{
    /// Wraps the built-in <c>Unity_RunCommand</c> tool so that its hardcoded, uninformative
    /// COMPILATION_IN_PROGRESS error gets enriched with the one fact that tells the agent what to do next:
    /// whether the editor is in PLAY MODE. In play mode Unity defers compilation, so a code edit never
    /// compiles and RunCommand stalls indefinitely — the fix is to leave play mode (<c>Unity_ExitPlayMode</c>),
    /// not to keep retrying. The package's error string can't be reflection-patched (it's a literal in a
    /// compiled method body), so we delegate to the real handler and rewrite only that one error.
    ///
    /// Delegates everything else verbatim to the original handler (captured by reflection), so behavior and
    /// schema are unchanged. Installed/repaired by <see cref="RunCommandPlayModeWrapperInstaller"/>.
    class RunCommandPlayModeWrapper : IUnityMcpTool
    {
        readonly object _original;          // the original IToolHandler (internal type)
        readonly MethodInfo _exec;          // Task<object> ExecuteAsync(JObject)
        readonly MethodInfo _inSchema;      // object GetInputSchema()
        readonly MethodInfo _outSchema;     // object GetOutputSchema()

        public RunCommandPlayModeWrapper(object originalHandler)
        {
            _original = originalHandler;
            var t = originalHandler.GetType();
            _exec = t.GetMethod("ExecuteAsync");
            _inSchema = t.GetMethod("GetInputSchema");
            _outSchema = t.GetMethod("GetOutputSchema");
        }

        /// True only if we can actually delegate — the installer refuses to register an inert wrapper.
        public bool CanDelegate => _exec != null;

        public async Task<object> ExecuteAsync(object parameters)
        {
            // Fail fast: when the editor defers compilation until play ends, a fresh code edit will never
            // compile while playing, so the package's RefreshAndWaitForReady would just block for the full
            // timeout before erroring. Detect that up front with the SAME isCompiling/isUpdating signal it
            // uses — but checked once, no wait — and return the actionable error immediately.
            if (IsDeferredPlayModeCompile())
            {
                AssetDatabase.Refresh(); // surface any pending import/compile (the first thing RunCommand does)
                if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                    return PlayModeStallError();
            }

            var task = (Task<object>)_exec.Invoke(_original, new[] { parameters });
            var result = await task;
            try { return MaybeEnrich(result); }
            catch { return result; } // enrichment must never break the underlying command
        }

        public object GetInputSchema() => _inSchema?.Invoke(_original, null);
        public object GetOutputSchema() => _outSchema?.Invoke(_original, null);

        static object MaybeEnrich(object result)
        {
            if (result == null) return result;
            var rt = result.GetType();
            // RunCommand's COMPILATION_IN_PROGRESS path returns Response.Error(...): { success=false, code, error, data }.
            if (rt.GetProperty("success")?.GetValue(result) as bool? != false) return result;
            var code = rt.GetProperty("code")?.GetValue(result) as string
                       ?? rt.GetProperty("error")?.GetValue(result) as string;
            if (code == null || !code.StartsWith("COMPILATION_IN_PROGRESS")) return result;

            // Post-timeout fallback (the pre-check should have caught the play-mode case already).
            if (IsDeferredPlayModeCompile()) return PlayModeStallError();
            return Response.Error(
                "COMPILATION_IN_PROGRESS: Unity is compiling/importing and is not a play-mode stall — a normal " +
                "in-progress build. Just retry in a few seconds.",
                new { isCompiling = true, isPlaying = EditorApplication.isPlaying });
        }

        // EditorPrefs "ScriptCompilationDuringPlay": 0 = RecompileAndContinuePlaying,
        // 1 = RecompileAfterFinishedPlaying, 2 = StopPlayingAndRecompile. Only value 1 defers compilation
        // until play ends — the case where waiting for a compile is futile.
        const int k_RecompileAfterFinishedPlaying = 1;

        static bool IsDeferredPlayModeCompile() =>
            EditorApplication.isPlaying &&
            EditorPrefs.GetInt("ScriptCompilationDuringPlay", k_RecompileAfterFinishedPlaying) == k_RecompileAfterFinishedPlaying;

        static object PlayModeStallError() =>
            Response.Error(
                "COMPILATION_IN_PROGRESS: the editor is in PLAY MODE with 'Recompile After Finished Playing', so " +
                "your last code edit will NOT compile until you leave play mode — RunCommand cannot proceed and " +
                "retrying will not clear it. Call Unity_ExitPlayMode, then retry this command.",
                new { isCompiling = true, isPlaying = true });
    }

    /// Keeps <see cref="RunCommandPlayModeWrapper"/> installed over <c>Unity_RunCommand</c>. The registry
    /// clears + re-discovers tools on every domain reload (RefreshTools) and can do so again at runtime, which
    /// would drop the wrapper — so we (re)apply on afterAssemblyReload, delayCall, AND every ToolsChanged.
    [InitializeOnLoad]
    static class RunCommandPlayModeWrapperInstaller
    {
        const string ToolName = "Unity.RunCommand";   // sanitizes to the registry key below
        const string SanitizedName = "Unity_RunCommand";
        static bool s_Installing;                      // re-entrancy guard (RegisterTool fires ToolsChanged)

        static RunCommandPlayModeWrapperInstaller()
        {
            AssemblyReloadEvents.afterAssemblyReload += Install;
            EditorApplication.delayCall += Install;
            McpToolRegistry.ToolsChanged += _ => Install();
        }

        static void Install()
        {
            if (s_Installing) return;
            try
            {
                s_Installing = true;

                var getTool = typeof(McpToolRegistry).GetMethod("GetTool", BindingFlags.NonPublic | BindingFlags.Static);
                if (getTool == null)
                {
                    AssistantHackGuard.ReportMissing("RunCommandPlayModeWrapper", "McpToolRegistry.GetTool");
                    return;
                }

                var handler = getTool.Invoke(null, new object[] { SanitizedName });
                if (handler == null) return;        // RunCommand not registered yet — a later ToolsChanged retries
                if (IsOurWrapper(handler)) return;  // already installed

                var wrapper = new RunCommandPlayModeWrapper(handler);
                if (!wrapper.CanDelegate)
                {
                    AssistantHackGuard.ReportMissing("RunCommandPlayModeWrapper", $"{handler.GetType().Name}.ExecuteAsync");
                    return; // refuse to install an inert wrapper that would break RunCommand
                }

                // Preserve the original tool's metadata so the exposed schema/description are unchanged.
                var attr = handler.GetType().GetProperty("Attribute")?.GetValue(handler);
                var desc = attr?.GetType().GetProperty("Description")?.GetValue(attr) as string;
                var groups = attr?.GetType().GetProperty("Groups")?.GetValue(attr) as string[];
                var enabled = attr?.GetType().GetProperty("EnabledByDefault")?.GetValue(attr) as bool? ?? true;

                McpToolRegistry.RegisterTool(ToolName, wrapper, desc, enabled, groups);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RunCommandPlayModeWrapper] install failed: {e.Message}");
            }
            finally
            {
                s_Installing = false;
            }
        }

        static bool IsOurWrapper(object handler)
        {
            // A registered IUnityMcpTool lives behind a ClassToolHandler with an `m_ToolInstance` field.
            var inst = handler.GetType().GetField("m_ToolInstance", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(handler);
            return inst is RunCommandPlayModeWrapper;
        }
    }
}
