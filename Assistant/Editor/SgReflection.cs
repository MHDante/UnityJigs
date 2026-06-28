using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;

namespace UnityJigs.Assistant.Editor
{
    /// A plain, reflection-free snapshot of a ShaderGraph, extracted from Unity's
    /// internal <c>GraphData</c> model. The emitter works only against these POCOs.
    public class SgGraph
    {
        public string Name = "";
        public string Target = "";
        public List<SgProp> Properties = new();
        public List<SgNode> Nodes = new();
        public List<SgEdge> Edges = new();
        public readonly Dictionary<string, SgNode> ById = new();
    }

    public class SgProp
    {
        public string DisplayName = "";
        public string RefName = "";
        public string Type = "";
    }

    public class SgNode
    {
        public string Id = "";        // 32-hex objectId
        public string Type = "";      // e.g. "SampleTexture2DNode"
        public string Name = "";      // node title (subgraph name for SubGraphNode)
        public string? PropertyRef;   // PropertyNode: referenceName of the property it reads

        // CustomFunctionNode
        public string? FunctionName;        // m_FunctionName
        public string? FunctionMode;        // "String" (inline body) or "File"
        public string? FunctionBody;        // inline HLSL (String mode)
        public string? FunctionSourcePath;  // resolved .hlsl asset path (File mode)

        // SubGraphNode
        public string? SubGraphPath;        // resolved .shadersubgraph asset path

        public List<SgSlot> Inputs = new();
        public List<SgSlot> Outputs = new();
    }

    public class SgSlot
    {
        public int Id;
        public string Name = "";   // display name
        public string Type = "";   // concreteValueType (Vector1/2/3/4, Texture2D, ...)
        public string Value = "";  // default literal, used when the slot is unconnected
    }

    public class SgEdge
    {
        public string FromNode = "";
        public int FromSlot;
        public string ToNode = "";
        public int ToSlot;
    }

    /// Loads a <c>.shadergraph</c> file into an <see cref="SgGraph"/> by reflecting over the
    /// internal <c>Unity.ShaderGraph.Editor</c> object model. Reflection keeps us decoupled
    /// from the package's internals (no InternalsVisibleTo / asmdef reference required), so the
    /// tool survives package updates.
    public static class SgReflection
    {
        static Assembly? _asm;
        internal static Assembly Asm => _asm ??= AppDomain.CurrentDomain.GetAssemblies()
            .First(a => a.GetName().Name == "Unity.ShaderGraph.Editor");

        internal static Type Type(string fullName) =>
            Asm.GetType(fullName) ?? throw new Exception($"ShaderGraph type not found: {fullName}");

        /// Deserializes a .shadergraph file into the live internal <c>GraphData</c> object.
        /// Used by both the POCO extractor (<see cref="Load"/>) and the writer (which mutates this
        /// object via the real model and re-serializes it).
        internal static object Deserialize(string path)
        {
            var tGraph = Type("UnityEditor.ShaderGraph.GraphData");
            var tMulti = Type("UnityEditor.ShaderGraph.Serialization.MultiJson");

            var graph = Activator.CreateInstance(tGraph, nonPublic: true)!;
            var mmField = tGraph.GetField("messageManager", Inst);
            var tMM = Asm.GetType("UnityEditor.Graphing.Util.MessageManager");
            if (mmField != null && tMM != null)
                mmField.SetValue(graph, Activator.CreateInstance(tMM, nonPublic: true));

            var json = File.ReadAllText(path, Encoding.UTF8);
            var deserialize = tMulti.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == "Deserialize" && m.IsGenericMethodDefinition)
                .MakeGenericMethod(tGraph);
            var dp = deserialize.GetParameters();
            var dargs = new object?[dp.Length];
            dargs[0] = graph;
            dargs[1] = json;
            for (var i = 2; i < dp.Length; i++)
                dargs[i] = dp[i].HasDefaultValue ? dp[i].DefaultValue
                    : dp[i].ParameterType.IsValueType ? Activator.CreateInstance(dp[i].ParameterType) : null;
            deserialize.Invoke(null, dargs);
            return graph;
        }

        /// Serializes a live <c>GraphData</c> back to its canonical .shadergraph text via MultiJson.
        internal static string Serialize(object graph)
        {
            var tMulti = Type("UnityEditor.ShaderGraph.Serialization.MultiJson");
            var serialize = tMulti.GetMethods(BindingFlags.Public | BindingFlags.Static).First(m => m.Name == "Serialize");
            return (string)serialize.Invoke(null, new[] { graph })!;
        }

        public static SgGraph Load(string path) => Extract(Deserialize(path), Path.GetFileNameWithoutExtension(path));

        /// Builds a POCO snapshot from an already-live GraphData (used by an editing session to
        /// re-snapshot its current, possibly-mutated state).
        internal static SgGraph Extract(object graph, string name)
        {
            var tGraph = Type("UnityEditor.ShaderGraph.GraphData");
            var tAMN = Type("UnityEditor.ShaderGraph.AbstractMaterialNode");
            var tSlot = Type("UnityEditor.ShaderGraph.MaterialSlot");

            var result = new SgGraph { Name = name };

            // --- properties ---
            if (tGraph.GetProperty("properties", Inst)?.GetValue(graph) is IEnumerable props)
                foreach (var p in props)
                {
                    var pt = p.GetType();
                    result.Properties.Add(new SgProp
                    {
                        DisplayName = Str(pt.GetProperty("displayName")?.GetValue(p)),
                        RefName = Str(pt.GetProperty("referenceName")?.GetValue(p)),
                        Type = pt.Name,
                    });
                }

            // --- nodes + slots ---
            var pObjId = tAMN.GetProperty("objectId")!;
            var pName = tAMN.GetProperty("name")!;
            var getSlots = tAMN.GetMethods()
                .First(m => m.Name == "GetSlots" && m.IsGenericMethodDefinition).MakeGenericMethod(tSlot);
            var slotListType = typeof(List<>).MakeGenericType(tSlot);
            var pSlotId = tSlot.GetProperty("id")!;
            var pSlotName = tSlot.GetProperty("displayName")!;
            var pSlotIsInput = tSlot.GetProperty("isInputSlot")!;
            var pSlotConcrete = tSlot.GetProperty("concreteValueType");
            var pSlotValue = tSlot.GetProperty("value");

            var getNodes = tGraph.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .First(m => m.Name == "GetNodes" && m.IsGenericMethodDefinition).MakeGenericMethod(tAMN);
            if (getNodes.Invoke(graph, null) is IEnumerable nodeList)
                foreach (var node in nodeList)
                {
                    var n = new SgNode
                    {
                        Id = Str(pObjId.GetValue(node)),
                        Name = Str(pName.GetValue(node)),
                        Type = node.GetType().Name,
                    };

                    if (n.Type == "PropertyNode")
                        try
                        {
                            if (node.GetType().GetProperty("property")?.GetValue(node) is { } refProp)
                                n.PropertyRef = refProp.GetType().GetProperty("referenceName")?.GetValue(refProp)?.ToString();
                        }
                        catch { /* best effort */ }
                    else if (n.Type == "CustomFunctionNode")
                        try
                        {
                            var nt = node.GetType();
                            n.FunctionName = nt.GetField("m_FunctionName", Inst)?.GetValue(node)?.ToString();
                            n.FunctionMode = nt.GetField("m_SourceType", Inst)?.GetValue(node)?.ToString();
                            n.FunctionBody = nt.GetField("m_FunctionBody", Inst)?.GetValue(node)?.ToString();
                            var srcGuid = nt.GetField("m_FunctionSource", Inst)?.GetValue(node)?.ToString();
                            if (n.FunctionMode == "File" && !string.IsNullOrEmpty(srcGuid))
                                n.FunctionSourcePath = AssetDatabase.GUIDToAssetPath(srcGuid);
                        }
                        catch { /* best effort */ }
                    else if (n.Type == "SubGraphNode")
                        try
                        {
                            var guid = ParseGuid(node.GetType().GetField("m_SerializedSubGraph", Inst)?.GetValue(node)?.ToString());
                            if (guid != null) n.SubGraphPath = AssetDatabase.GUIDToAssetPath(guid);
                        }
                        catch { /* best effort */ }

                    var slots = (IList)Activator.CreateInstance(slotListType)!;
                    getSlots.Invoke(node, new object[] { slots });
                    foreach (var s in slots)
                    {
                        var slot = new SgSlot
                        {
                            Id = (int)pSlotId.GetValue(s)!,
                            Name = CleanSlotName(Str(pSlotName.GetValue(s))),
                            Type = Str(pSlotConcrete?.GetValue(s)),
                        };
                        try { slot.Value = Str(pSlotValue?.GetValue(s)); }
                        catch { /* not all slot types expose a scalar value */ }
                        if ((bool)pSlotIsInput.GetValue(s)!) n.Inputs.Add(slot);
                        else n.Outputs.Add(slot);
                    }

                    result.Nodes.Add(n);
                    result.ById[n.Id] = n;
                }

            // --- edges ---
            if (tGraph.GetProperty("edges", Inst)?.GetValue(graph) is IEnumerable edges)
                foreach (var e in edges)
                {
                    var et = e.GetType();
                    var outRef = et.GetProperty("outputSlot")?.GetValue(e);
                    var inRef = et.GetProperty("inputSlot")?.GetValue(e);
                    if (outRef == null || inRef == null) continue;
                    var rt = outRef.GetType();
                    var fromNode = rt.GetProperty("node")?.GetValue(outRef);
                    var toNode = rt.GetProperty("node")?.GetValue(inRef);
                    if (fromNode == null || toNode == null) continue;
                    result.Edges.Add(new SgEdge
                    {
                        FromNode = Str(pObjId.GetValue(fromNode)),
                        FromSlot = (int)rt.GetProperty("slotId")!.GetValue(outRef)!,
                        ToNode = Str(pObjId.GetValue(toNode)),
                        ToSlot = (int)rt.GetProperty("slotId")!.GetValue(inRef)!,
                    });
                }

            // --- target settings (pipeline / lit-mode / surface / cull / zwrite / clip) ---
            try
            {
                if (tGraph.GetProperty("activeTargets", Inst)?.GetValue(graph) is IEnumerable targets)
                    foreach (var t in targets) { result.Target = SummarizeTarget(t); break; }
            }
            catch { /* best effort */ }

            return result;
        }

        /// Builds a one-line target summary e.g. "URP/Unlit · Opaque · Cull:Off · ZWrite:ForceDisabled".
        /// Reads the target's private enum/bool fields and uses their names (Unity enums ToString cleanly).
        static string SummarizeTarget(object target)
        {
            var tt = target.GetType();
            var pipeline = tt.Name.EndsWith("Target") ? tt.Name[..^"Target".Length] : tt.Name;
            if (pipeline == "Universal") pipeline = "URP";

            var mode = "";
            if (tt.GetProperty("activeSubTarget", Inst)?.GetValue(target) is { } sub)
                mode = sub.GetType().Name.Replace("Universal", "").Replace("SubTarget", "");

            string? Field(string name) => tt.GetField(name, Inst)?.GetValue(target)?.ToString();

            var parts = new List<string>();
            if (Field("m_SurfaceType") is { } surf) parts.Add(surf);
            if (Field("m_AlphaMode") is { } blend && Field("m_SurfaceType") == "Transparent") parts.Add($"Blend:{blend}");
            if (Field("m_RenderFace") is { } face && face != "Front") parts.Add($"Cull:{(face == "Both" ? "Off" : face)}");
            if (Field("m_ZWriteControl") is { } zw && zw != "Auto") parts.Add($"ZWrite:{zw}");
            if (Field("m_AlphaClip") == "True") parts.Add("AlphaClip");
            if (Field("m_CastShadows") == "False") parts.Add("NoShadows");

            var head = string.IsNullOrEmpty(mode) ? pipeline : $"{pipeline}/{mode}";
            return parts.Count > 0 ? $"{head}  ·  {string.Join("  ·  ", parts)}" : head;
        }

        internal const BindingFlags Inst = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        static string Str(object? o) => o?.ToString() ?? "";

        /// SubGraphNode stores its target as a serialized asset ref blob; pull the first 32-hex GUID out of it.
        static string? ParseGuid(string? s)
        {
            if (s == null) return null;
            for (var k = 0; k + 32 <= s.Length; k++)
            {
                var ok = true;
                for (var j = 0; j < 32; j++)
                    if (!Uri.IsHexDigit(s[k + j])) { ok = false; break; }
                if (ok && (k + 32 == s.Length || !Uri.IsHexDigit(s[k + 32])) && (k == 0 || !Uri.IsHexDigit(s[k - 1])))
                    return s.Substring(k, 32);
            }
            return null;
        }

        /// Unity appends a short type-code suffix to slot display names: channel counts like
        /// "RGBA(4)"/"R(1)" and type hints like "Sampler(SS)"/"Predicate(B)"/"Texture(T2)".
        /// Strip it — that information is already in <see cref="SgSlot.Type"/>.
        static string CleanSlotName(string name)
        {
            if (!name.EndsWith(")")) return name;
            var open = name.LastIndexOf('(');
            if (open <= 0) return name;
            var inner = name.Substring(open + 1, name.Length - open - 2);
            return inner.Length is > 0 and <= 3 && inner.All(char.IsLetterOrDigit) ? name[..open].TrimEnd() : name;
        }
    }
}
