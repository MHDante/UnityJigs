using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace UnityJigs.Assistant.Editor
{
    /// Turns a ShaderGraph into compact, token-efficient pseudo-shadercode: properties become
    /// uniforms, the active master-stack blocks (or a subgraph's outputs) are the per-stage outputs,
    /// and the graph between them is emitted as SSA let-bindings in topological order.
    ///
    /// Two opt-in knobs for graphs built from subgraphs / Custom Function nodes:
    ///   expandFunctions (default true) — inline a String-mode Custom Function node's HLSL body.
    ///   drillSubgraphs  (default false) — also decompile every referenced .shadersubgraph, recursively,
    ///                                     as labelled sections after the main graph.
    public static class ShaderGraphReader
    {
        public static string Decompile(string path, bool drillSubgraphs = false, bool expandFunctions = true)
        {
            var sb = new StringBuilder();
            var main = SgReflection.Load(path);
            sb.Append(Emit(main, expandFunctions));

            if (drillSubgraphs)
            {
                var visited = new HashSet<string> { path };
                var queue = new Queue<string>(SubgraphPaths(main));
                while (queue.Count > 0)
                {
                    var sp = queue.Dequeue();
                    if (string.IsNullOrEmpty(sp) || !visited.Add(sp)) continue;
                    SgGraph sub;
                    try { sub = SgReflection.Load(sp); }
                    catch { sb.Append("\n// (could not load subgraph: ").Append(sp).Append(")\n"); continue; }
                    sb.AppendLine();
                    sb.Append("════════ subgraph: ").Append(sub.Name).Append("  (").Append(sp).Append(") ════════").AppendLine();
                    sb.Append(Emit(sub, expandFunctions));
                    foreach (var s in SubgraphPaths(sub)) queue.Enqueue(s);
                }
            }
            return sb.ToString();
        }

        /// Decompile an already-extracted snapshot (e.g. a live editing session's current state).
        public static string Decompile(SgGraph graph) => Emit(graph, expand: true);

        static IEnumerable<string> SubgraphPaths(SgGraph g) =>
            g.Nodes.Where(n => n.Type == "SubGraphNode" && !string.IsNullOrEmpty(n.SubGraphPath))
                .Select(n => n.SubGraphPath!).Distinct();

        static string Emit(SgGraph g, bool expand)
        {
            var sb = new StringBuilder();
            sb.Append("shader ").Append(g.Name);
            if (!string.IsNullOrEmpty(g.Target)) sb.Append("  :  ").Append(g.Target);
            sb.AppendLine();

            if (g.Properties.Count > 0)
            {
                sb.AppendLine();
                foreach (var p in g.Properties)
                    sb.Append("uniform ").Append(ShortPropType(p.Type)).Append(' ').Append(p.RefName)
                        .Append("   // ").Append(p.DisplayName).AppendLine();
            }

            var incoming = new Dictionary<(string, int), SgEdge>();
            foreach (var e in g.Edges) incoming[(e.ToNode, e.ToSlot)] = e;
            var ctx = new EmitContext(g, incoming);

            var vertexBlocks = g.Nodes.Where(n => n.Type == "BlockNode" && n.Name.StartsWith("VertexDescription.")).ToList();
            var fragBlocks = g.Nodes.Where(n => n.Type == "BlockNode" && n.Name.StartsWith("SurfaceDescription.")).ToList();

            if (vertexBlocks.Count > 0 || fragBlocks.Count > 0)
            {
                EmitBody(sb, "VertexDescription", BlockOutputs(vertexBlocks), ctx, expand, collapseIdentity: true);
                EmitBody(sb, "SurfaceDescription", BlockOutputs(fragBlocks), ctx, expand, collapseIdentity: true);
            }
            else if (g.Nodes.FirstOrDefault(n => n.Type == "SubGraphOutputNode") is { } outNode)
            {
                // A subgraph: its outputs are the SubGraphOutputNode's input slots.
                var outs = outNode.Inputs.Select(s => (s.Name, outNode, s)).ToList();
                EmitBody(sb, "Outputs", outs, ctx, expand, collapseIdentity: false);
            }

            var dead = g.Nodes.Count(n => !IsSink(n) && !ctx.Visited.Contains(n.Id));
            if (dead > 0)
            {
                sb.AppendLine();
                sb.Append("// ").Append(dead).AppendLine(" unreferenced node(s) not shown");
            }
            return sb.ToString();
        }

        static bool IsSink(SgNode n) => n.Type is "BlockNode" or "SubGraphOutputNode";

        static List<(string field, SgNode node, SgSlot slot)> BlockOutputs(List<SgNode> blocks)
        {
            var outs = new List<(string, SgNode, SgSlot)>();
            foreach (var b in blocks)
            {
                var slot = b.Inputs.FirstOrDefault();
                if (slot != null) outs.Add((b.Name[(b.Name.IndexOf('.') + 1)..], b, slot));
            }
            return outs;
        }

        static void EmitBody(StringBuilder sb, string header, List<(string field, SgNode node, SgSlot slot)> outputs,
            EmitContext ctx, bool expand, bool collapseIdentity)
        {
            if (outputs.Count == 0) return;

            // Reverse reachability from each output, post-order => dependencies first.
            var order = new List<SgNode>();
            var visiting = new HashSet<string>();
            void Visit(SgNode n)
            {
                if (!visiting.Add(n.Id)) return;
                ctx.Visited.Add(n.Id);
                foreach (var inSlot in n.Inputs)
                    if (ctx.Incoming.TryGetValue((n.Id, inSlot.Id), out var e) && ctx.Graph.ById.TryGetValue(e.FromNode, out var src))
                        Visit(src);
                order.Add(n);
            }
            foreach (var node in outputs.GroupBy(o => o.node.Id).Select(grp => grp.First().node)) Visit(node);

            // Intermediates: everything except sinks, property reads, and redirect dots.
            var intermediates = order.Where(n => !IsSink(n) && n.Type is not ("PropertyNode" or "RedirectNodeData")).ToList();
            foreach (var n in intermediates) ctx.VarName(n);

            var outExprs = outputs.Select(o => (o.field, expr: ctx.RefInput(o.node, o.slot))).ToList();

            sb.AppendLine();
            if (collapseIdentity && outExprs.All(o => o.expr == "default"))
            {
                sb.Append(header).AppendLine(":  identity");
                return;
            }
            sb.Append(header).AppendLine(":");

            foreach (var n in intermediates) EmitNode(sb, n, ctx, expand);
            foreach (var (field, expr) in outExprs)
                sb.Append("    ").Append(field).Append(" = ").Append(expr).AppendLine();
        }

        static void EmitNode(StringBuilder sb, SgNode n, EmitContext ctx, bool expand)
        {
            var v = ctx.VarName(n);
            // Show connected inputs and inputs with a literal; omit value-less defaults (samplers, mesh-UV…).
            var shown = n.Inputs.Where(s => ctx.Connected(n, s) || !string.IsNullOrEmpty(s.Value)).ToList();
            var nameArgs = shown.Count > 1;
            var args = shown.Select(s => nameArgs ? $"{s.Name}: {ctx.RefInput(n, s)}" : ctx.RefInput(n, s));

            sb.Append("    ").Append(v).Append(" = ").Append(NodeLabel(n)).Append('(').Append(string.Join(", ", args)).Append(')');

            if (n.Type == "SubGraphNode" && !string.IsNullOrEmpty(n.SubGraphPath))
                sb.Append("   // subgraph: ").Append(n.SubGraphPath);
            else if (n.Type == "CustomFunctionNode")
                sb.Append(n.FunctionMode == "File" && !string.IsNullOrEmpty(n.FunctionSourcePath)
                    ? "   // [File: " + n.FunctionSourcePath + "]"
                    : "   // [" + (n.FunctionMode ?? "?") + "]");
            else if (n.Outputs.Count > 1)
                sb.Append("   // -> ").Append(string.Join(", ", n.Outputs.Select(o => o.Name)));
            sb.AppendLine();

            // Inline a String-mode Custom Function's HLSL body when requested.
            if (expand && n.Type == "CustomFunctionNode" && n.FunctionMode == "String"
                && !string.IsNullOrWhiteSpace(n.FunctionBody) && n.FunctionBody.Trim() != "Enter function body here...")
            {
                sb.AppendLine("        ⟪hlsl⟫");
                foreach (var line in n.FunctionBody.Replace("\r\n", "\n").Split('\n'))
                    sb.Append("        ").AppendLine(line);
                sb.AppendLine("        ⟪/hlsl⟫");
            }
        }

        static string NodeLabel(SgNode n)
        {
            if (n.Type == "SubGraphNode") return n.Name;
            if (n.Type == "CustomFunctionNode")
                return "CustomFn:" + (string.IsNullOrEmpty(n.FunctionName) ? StripSuffix(n.Name, " (Custom Function)") : n.FunctionName);
            return n.Type.EndsWith("Node") ? n.Type[..^4] : n.Type;
        }

        static string StripSuffix(string s, string suffix) => s.EndsWith(suffix) ? s[..^suffix.Length] : s;

        static string ShortPropType(string propTypeName) =>
            propTypeName.EndsWith("ShaderProperty") ? propTypeName[..^"ShaderProperty".Length] : propTypeName;

        /// Carries the per-emit mutable state (var-name assignment) and the lookups.
        class EmitContext
        {
            public readonly SgGraph Graph;
            public readonly Dictionary<(string, int), SgEdge> Incoming;
            public readonly HashSet<string> Visited = new();
            readonly Dictionary<string, string> _varNames = new();
            readonly HashSet<string> _used = new();

            public EmitContext(SgGraph graph, Dictionary<(string, int), SgEdge> incoming)
            {
                Graph = graph;
                Incoming = incoming;
            }

            public string VarName(SgNode n)
            {
                if (_varNames.TryGetValue(n.Id, out var existing)) return existing;
                var baseName = Sanitize(n.Name);
                if (baseName.Length == 0) baseName = "v";
                var name = baseName;
                var i = 2;
                while (!_used.Add(name)) name = baseName + i++;
                _varNames[n.Id] = name;
                return name;
            }

            public bool Connected(SgNode n, SgSlot s) => Incoming.ContainsKey((n.Id, s.Id));

            public string RefInput(SgNode consumer, SgSlot slot)
            {
                if (Incoming.TryGetValue((consumer.Id, slot.Id), out var e) && Graph.ById.TryGetValue(e.FromNode, out var src))
                {
                    // Pass through redirect dots (pure wire routing) to the real producer.
                    while (src.Type == "RedirectNodeData")
                    {
                        var rin = src.Inputs.FirstOrDefault();
                        if (rin != null && Incoming.TryGetValue((src.Id, rin.Id), out var re) && Graph.ById.TryGetValue(re.FromNode, out var rsrc))
                        { e = re; src = rsrc; }
                        else return rin != null && !string.IsNullOrEmpty(rin.Value) ? rin.Value : "default";
                    }

                    if (src.Type == "PropertyNode") return src.PropertyRef ?? Sanitize(src.Name);
                    var v = _varNames.TryGetValue(src.Id, out var vn) ? vn : Sanitize(src.Name);
                    if (src.Outputs.Count > 1)
                    {
                        var outSlot = src.Outputs.FirstOrDefault(o => o.Id == e.FromSlot);
                        if (outSlot != null) v += "." + outSlot.Name;
                    }
                    return v;
                }
                return string.IsNullOrEmpty(slot.Value) ? "default" : slot.Value;
            }

            static string Sanitize(string name)
            {
                var sb = new StringBuilder();
                var upperNext = false;
                foreach (var c in name)
                {
                    if (char.IsLetterOrDigit(c))
                    {
                        sb.Append(upperNext ? char.ToUpperInvariant(c) : c);
                        upperNext = false;
                    }
                    else upperNext = sb.Length > 0;
                }
                var s = sb.ToString();
                return s.Length > 0 ? char.ToLowerInvariant(s[0]) + s[1..] : s;
            }
        }
    }
}
