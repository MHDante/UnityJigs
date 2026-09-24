using Unity.Pipeline.Commands;
using UnityJigs.Tooling.Editor;

namespace UnityJigs.Pipeline.Editor
{
    /// Unity Pipeline (`unity command ...`) front-end for the ShaderGraph decompiler/writer — the CLI twin of
    /// ShaderGraphMcpTools in the Assistant module. Gated behind UNITYJIGS_PIPELINE (it hard-references the
    /// com.unity.pipeline package). Exceptions propagate: the Pipeline server reports them as a failed command.
    static class ShaderGraphCliCommands
    {
        [CliCommand("shadergraph_read",
            "Decompile a .shadergraph into compact, legible pseudo-shadercode (properties as uniforms, " +
            "master-stack outputs per stage, the node graph as SSA let-bindings). ~100x fewer tokens than the raw JSON. " +
            "Custom Function nodes show their HLSL (inline body or .hlsl path); set drill_subgraphs to expand subgraphs.",
            Tags = new[] { "materials/shaders" })]
        public static string Read(
            [CliArg("path", "Project-relative path to the .shadergraph file (e.g. 'Assets/Shaders/Toon.shadergraph').", Required = true)] string path,
            [CliArg("drill_subgraphs", "Also recursively decompile every referenced .shadersubgraph as labelled sections after the main graph.")] bool drillSubgraphs = false,
            [CliArg("expand_functions", "Inline a String-mode Custom Function node's HLSL body. File-mode functions always show their .hlsl path.")] bool expandFunctions = true)
            => ShaderGraphReader.Decompile(path, drillSubgraphs, expandFunctions);

        [CliCommand("shadergraph_set_property",
            "Set a shader property's default value safely (mutates the real graph model and re-serializes, " +
            "so the file matches what the ShaderGraph editor would write). Returns the old->new change.",
            Tags = new[] { "materials/shaders" })]
        public static string SetProperty(
            [CliArg("path", "Project-relative path to the .shadergraph file.", Required = true)] string path,
            [CliArg("reference_name", "Property reference name, e.g. '_PhaseThres' (the name shown after 'uniform' in the read output).", Required = true)] string referenceName,
            [CliArg("value", "New default value as a comma-separated literal: '0.5', '1,0,0,1', 'true'.", Required = true)] string value)
            => ShaderGraphWriter.SetProperty(path, referenceName, value);
    }
}
