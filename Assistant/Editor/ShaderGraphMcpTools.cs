using System;
using Unity.AI.MCP.Editor.Helpers;
using Unity.AI.MCP.Editor.ToolRegistry;

namespace UnityJigs.Assistant.Editor
{
    /// MCP tools exposing the ShaderGraph decompiler/writer as first-class Unity_* tools, so Claude can
    /// read and edit .shadergraph files without RunCommand snippets. Part of the jigs Assistant module,
    /// gated behind UNITYJIGS_ASSISTANT (it hard-references the Unity AI Assistant MCP package).
    public static class ShaderGraphMcpTools
    {
        public record ReadParams
        {
            [McpDescription("Project-relative path to the .shadergraph file (e.g. 'Assets/Shaders/Toon.shadergraph').", Required = true)]
            public string Path { get; set; } = string.Empty;
        }

        [McpTool("Unity.ShaderGraphRead",
            "Decompile a .shadergraph into compact, legible pseudo-shadercode (properties as uniforms, " +
            "master-stack outputs per stage, the node graph as SSA let-bindings). ~100x fewer tokens than the raw JSON.",
            "Read ShaderGraph", Groups = new[] { "scripting" })]
        public static object Read(ReadParams p)
        {
            try { return Response.Success($"Decompiled {p.Path}", new { text = ShaderGraphReader.Decompile(p.Path) }); }
            catch (Exception e) { return Response.Error(e.Message); }
        }

        public record SetPropertyParams
        {
            [McpDescription("Project-relative path to the .shadergraph file.", Required = true)]
            public string Path { get; set; } = string.Empty;

            [McpDescription("Property reference name, e.g. '_PhaseThres' (the name shown after 'uniform' in the read output).", Required = true)]
            public string ReferenceName { get; set; } = string.Empty;

            [McpDescription("New default value as a comma-separated literal: '0.5', '1,0,0,1', 'true'.", Required = true)]
            public string Value { get; set; } = string.Empty;
        }

        [McpTool("Unity.ShaderGraphSetProperty",
            "Set a shader property's default value safely (mutates the real graph model and re-serializes, " +
            "so the file matches what the ShaderGraph editor would write). Returns the old->new change.",
            "Set ShaderGraph Property", Groups = new[] { "scripting" })]
        public static object SetProperty(SetPropertyParams p)
        {
            try { return Response.Success(ShaderGraphWriter.SetProperty(p.Path, p.ReferenceName, p.Value)); }
            catch (Exception e) { return Response.Error(e.Message); }
        }
    }
}
