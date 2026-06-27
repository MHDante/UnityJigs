using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace UnityJigs.Assistant.Editor
{
    /// Turns a ShaderGraph into compact, token-efficient pseudo-shadercode: properties become
    /// uniforms, the active master-stack blocks are the per-stage outputs, and the graph between
    /// them is emitted as SSA let-bindings in topological order (shared intermediates once).
    public static class ShaderGraphReader
    {
        public static string Decompile(string path) => Emit(SgReflection.Load(path));

        /// Decompile an already-extracted snapshot (e.g. a live editing session's current state).
        public static string Decompile(SgGraph graph) => Emit(graph);

        static string Emit(SgGraph g)
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

            // (toNode, toSlot) -> the edge feeding it
            var incoming = new Dictionary<(string, int), SgEdge>();
            foreach (var e in g.Edges) incoming[(e.ToNode, e.ToSlot)] = e;

            var ctx = new EmitContext(g, incoming);

            var vertexBlocks = g.Nodes.Where(n => n.Type == "BlockNode" && n.Name.StartsWith("VertexDescription.")).ToList();
            var fragBlocks = g.Nodes.Where(n => n.Type == "BlockNode" && n.Name.StartsWith("SurfaceDescription.")).ToList();

            EmitStage(sb, "VertexDescription", vertexBlocks, ctx);
            EmitStage(sb, "SurfaceDescription", fragBlocks, ctx);

            var reachable = ctx.Visited.Count;
            var dead = g.Nodes.Count(n => n.Type != "BlockNode") - g.Nodes.Count(n => n.Type != "BlockNode" && ctx.Visited.Contains(n.Id));
            if (dead > 0)
            {
                sb.AppendLine();
                sb.Append("// ").Append(dead).AppendLine(" unreferenced node(s) not shown");
            }

            return sb.ToString();
        }

        static void EmitStage(StringBuilder sb, string stageName, List<SgNode> blocks, EmitContext ctx)
        {
            if (blocks.Count == 0) return;

            // Reverse reachability from the block inputs, post-order => dependencies first.
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
            foreach (var b in blocks) Visit(b);

            // Intermediates: everything except block sinks, property reads, and redirect dots
            // (all of which are inlined / passed through, not emitted as their own line).
            // Assign every var name up front so block/arg references resolve regardless of order.
            var intermediates = order.Where(n => n.Type is not ("BlockNode" or "PropertyNode" or "RedirectNodeData")).ToList();
            foreach (var n in intermediates) ctx.VarName(n);

            // Collapse a stage whose every block is an unconnected default (e.g. vertex identity).
            var blockExprs = blocks.Select(b =>
            {
                var inSlot = b.Inputs.FirstOrDefault();
                return (Field: b.Name[(b.Name.IndexOf('.') + 1)..], Expr: inSlot != null ? ctx.RefInput(b, inSlot) : "?");
            }).ToList();

            sb.AppendLine();
            if (blockExprs.All(b => b.Expr == "default"))
            {
                sb.Append(stageName).AppendLine(":  identity");
                return;
            }
            sb.Append(stageName).AppendLine(":");

            foreach (var n in intermediates)
            {
                var v = ctx.VarName(n);
                // Show connected inputs and inputs with a meaningful literal; omit value-less
                // defaults (samplers, mesh-UV, all-default subgraph ports) as noise.
                var shown = n.Inputs.Where(s => ctx.Connected(n, s) || !string.IsNullOrEmpty(s.Value)).ToList();
                var nameArgs = shown.Count > 1;
                var args = shown.Select(s => nameArgs ? $"{s.Name}: {ctx.RefInput(n, s)}" : ctx.RefInput(n, s));
                sb.Append("    ").Append(v).Append(" = ").Append(NodeLabel(n)).Append('(')
                    .Append(string.Join(", ", args)).Append(')');
                if (n.Outputs.Count > 1)
                    sb.Append("   // -> ").Append(string.Join(", ", n.Outputs.Select(o => o.Name)));
                sb.AppendLine();
            }

            // Block sinks: assign each active output field.
            foreach (var (field, expr) in blockExprs)
                sb.Append("    ").Append(field).Append(" = ").Append(expr).AppendLine();
        }

        static string NodeLabel(SgNode n)
        {
            if (n.Type == "SubGraphNode") return n.Name;
            return n.Type.EndsWith("Node") ? n.Type[..^4] : n.Type;
        }

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
