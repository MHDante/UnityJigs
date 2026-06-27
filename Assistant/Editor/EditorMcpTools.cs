using Unity.AI.MCP.Editor.Helpers;
using Unity.AI.MCP.Editor.ToolRegistry;
using UnityEditor;

namespace UnityJigs.Assistant.Editor
{
    /// General-purpose editor-state MCP tools exposed by the jigs Assistant module.
    public static class EditorMcpTools
    {
        [McpTool("Unity.ExitPlayMode",
            "Exit Unity play mode so pending script edits can compile. No-op if the editor is NOT currently " +
            "in play mode. USE THIS when Unity_RunCommand returns COMPILATION_IN_PROGRESS right after you " +
            "edited C#: that error almost always means the editor is in play mode, which DEFERS compilation, " +
            "so your edit never compiles and RunCommand keeps stalling. Calling this stops play mode and lets " +
            "the pending scripts recompile — then retry your RunCommand a few seconds later.",
            "Exit Play Mode", Groups = new[] { "scripting" })]
        public static object ExitPlayMode()
        {
            if (!EditorApplication.isPlaying)
                return Response.Success("Not in play mode — nothing to do.");

            // ExitPlaymode() is a request: the transition + recompile of deferred edits happen on the next
            // editor ticks, so this returns before play mode has fully stopped.
            EditorApplication.ExitPlaymode();
            return Response.Success("Requested play-mode exit; the editor will stop and recompile any pending " +
                                    "script edits. Retry your RunCommand in a few seconds.");
        }
    }
}
