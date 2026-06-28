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

            [McpDescription("Also recursively decompile every referenced .shadersubgraph as labelled sections after the main graph (default false). Turn on to see logic embedded in subgraphs.")]
            public bool DrillSubgraphs { get; set; } = false;

            [McpDescription("Inline a String-mode Custom Function node's HLSL body (default true). File-mode functions always show their .hlsl path.")]
            public bool ExpandFunctions { get; set; } = true;
        }

        [McpTool("Unity.ShaderGraphRead",
            "Decompile a .shadergraph into compact, legible pseudo-shadercode (properties as uniforms, " +
            "master-stack outputs per stage, the node graph as SSA let-bindings). ~100x fewer tokens than the raw JSON. " +
            "Custom Function nodes show their HLSL (inline body or .hlsl path); set DrillSubgraphs to expand subgraphs.",
            "Read ShaderGraph", Groups = new[] { "scripting" })]
        public static object Read(ReadParams p)
        {
            try { return Response.Success($"Decompiled {p.Path}", new { text = ShaderGraphReader.Decompile(p.Path, p.DrillSubgraphs, p.ExpandFunctions) }); }
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
