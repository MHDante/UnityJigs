using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using static UnityJigs.Assistant.Editor.SgReflection;

namespace UnityJigs.Assistant.Editor
{
    /// A live editing session over a .shadergraph, meant to be driven from Unity_RunCommand.
    /// Open() loads the real internal GraphData; every mutator goes through the real model (so node
    /// objectIds and slot wiring can never be corrupted); Save() re-serializes via MultiJson. Use
    /// Snapshot()/Decompile() to navigate and preview between edits. Call <see cref="Help"/> for usage.
    ///
    /// Nodes are addressed by objectId (the 32-hex <see cref="SgNode.Id"/> from Snapshot()); input/output
    /// slots by their int <see cref="SgSlot.Id"/>. Typical flow:
    /// <code>
    /// var s = ShaderGraphSession.Open(path);
    /// var g = s.Snapshot();
    /// var mul = g.Nodes.First(n =&gt; n.Name == "Multiply");
    /// s.SetInput(mul.Id, mul.Inputs[1].Id, "0.5");
    /// s.Save();
    /// </code>
    public class ShaderGraphSession
    {
        readonly string _path;
        readonly object _graph;

        static Type TGraph => Type("UnityEditor.ShaderGraph.GraphData");
        static Type TAMN => Type("UnityEditor.ShaderGraph.AbstractMaterialNode");
        static Type TSlot => Type("UnityEditor.ShaderGraph.MaterialSlot");

        ShaderGraphSession(string path, object graph) { _path = path; _graph = graph; }

        public static ShaderGraphSession Open(string path) => new(path, Deserialize(path));

        /// Navigable POCO snapshot of the CURRENT (possibly mutated, unsaved) state.
        public SgGraph Snapshot() => Extract(_graph, Path.GetFileNameWithoutExtension(_path));

        /// Pseudo-shadercode of the current state — preview edits before Save().
        public string Decompile() => ShaderGraphReader.Decompile(Snapshot());

        // ---------- mutators (each returns a human-readable status string) ----------

        /// Set a shader property's default value (by reference name). value: "0.5", "1,0,0,1", "true".
        public string SetProperty(string referenceName, string value)
        {
            if (GraphProperties().FirstOrDefault(p =>
                    p.GetType().GetProperty("referenceName")?.GetValue(p)?.ToString() == referenceName) is not { } prop)
                return $"property not found: {referenceName}";
            var vp = prop.GetType().GetProperty("value");
            if (vp == null) return $"property '{referenceName}' has no settable value";
            if (!TryParse(vp.PropertyType, value, out var parsed, out var err)) return $"property '{referenceName}': {err}";
            var old = vp.GetValue(prop);
            vp.SetValue(prop, parsed);
            return $"property {referenceName}: {old} -> {vp.GetValue(prop)}";
        }

        /// Set an (unconnected) input slot's default value on a node. value parsed like SetProperty.
        public string SetInput(string nodeId, int slotId, string value)
        {
            var node = Node(nodeId);
            var slot = FindSlot(node, slotId);
            if (slot == null) return $"slot {slotId} not found on node {nodeId}";
            var vp = slot.GetType().GetProperty("value");
            if (vp == null) return $"slot {slotId} on {nodeId} has no settable value";
            if (!TryParse(vp.PropertyType, value, out var parsed, out var err)) return $"slot {nodeId}.{slotId}: {err}";
            var old = vp.GetValue(slot);
            vp.SetValue(slot, parsed);
            return $"input {nodeId}.{slotId}: {old} -> {vp.GetValue(slot)}";
        }

        /// Wire an output slot to an input slot (replacing any existing edge into the target input).
        public string Connect(string fromNodeId, int fromSlotId, string toNodeId, int toSlotId)
        {
            Disconnect(toNodeId, toSlotId); // input slots take a single edge — replace, like the editor
            var from = SlotRef(Node(fromNodeId), fromSlotId);
            var to = SlotRef(Node(toNodeId), toSlotId);
            var connect = TGraph.GetMethod("Connect", new[] { from.GetType(), to.GetType() })!;
            var edge = connect.Invoke(_graph, new[] { from, to });
            return edge != null
                ? $"connected {fromNodeId}.{fromSlotId} -> {toNodeId}.{toSlotId}"
                : "connect failed (incompatible slot stages/types?)";
        }

        /// Remove all edges feeding an input slot.
        public string Disconnect(string toNodeId, int toSlotId)
        {
            var to = SlotRef(Node(toNodeId), toSlotId);
            var getEdges = TGraph.GetMethods().First(m =>
                m.Name == "GetEdges" && m.GetParameters().Length == 1 &&
                m.GetParameters()[0].ParameterType == to.GetType() && m.ReturnType != typeof(void));
            var edges = ((IEnumerable)getEdges.Invoke(_graph, new[] { to })!).Cast<object>().ToList();
            var removeEdge = TGraph.GetMethod("RemoveEdge")!;
            foreach (var e in edges) removeEdge.Invoke(_graph, new[] { e });
            return $"disconnected {edges.Count} edge(s) into {toNodeId}.{toSlotId}";
        }

        /// Create a node by its C# class name (e.g. "MultiplyNode" — the read view shows it as "Multiply",
        /// so append "Node"). Returns the new node's objectId; re-Snapshot() to see its slot ids.
        public string AddNode(string typeName)
        {
            var nodeType = Asm.GetTypes().FirstOrDefault(t =>
                TAMN.IsAssignableFrom(t) && !t.IsAbstract &&
                (t.Name == typeName || t.Name == typeName + "Node"));
            if (nodeType == null) return $"node type not found: '{typeName}' (use the C# class name, e.g. 'MultiplyNode')";
            var node = Activator.CreateInstance(nodeType, nonPublic: true)!;
            TGraph.GetMethod("AddNode", new[] { TAMN, typeof(bool) })!.Invoke(_graph, new[] { node, (object)true });
            return $"added {nodeType.Name} -> id {TAMN.GetProperty("objectId")!.GetValue(node)}";
        }

        public string RemoveNode(string nodeId)
        {
            TGraph.GetMethod("RemoveNode", new[] { TAMN })!.Invoke(_graph, new[] { Node(nodeId) });
            return $"removed node {nodeId}";
        }

        /// Move a node in graph space (for layout-preserving rebuilds). Read source positions from
        /// Snapshot()'s SgNode.X/Y. New nodes from AddNode start at (0,0) — position them with this.
        public string SetNodePosition(string nodeId, float x, float y)
        {
            var node = Node(nodeId);
            var pDraw = TAMN.GetProperty("drawState", Inst)!;
            var ds = pDraw.GetValue(node)!;
            var pPos = ds.GetType().GetProperty("position")!;
            var r = (Rect)pPos.GetValue(ds)!;
            pPos.SetValue(ds, new Rect(x, y, r.width, r.height));
            pDraw.SetValue(node, ds); // DrawState is a struct — write the modified copy back
            return $"moved {nodeId} -> ({x}, {y})";
        }

        // ---------- target settings (the master-stack / render-state lever) ----------

        /// Set an active-target render setting by property name, on every active target that exposes it.
        /// Bools: "true"/"false"/"1"/"0". Enums by name. Common: alphaClip, surfaceType (Opaque/Transparent),
        /// renderFace (Front/Back/Both), zWriteControl, zTestMode, alphaMode, castShadows.
        /// NOTE: turning alphaClip OFF is what restores early-Z (removes the forced clip()).
        public string SetTarget(string property, string value)
        {
            var targets = ActiveTargets().ToList();
            if (targets.Count == 0) return "no active targets on this graph";
            var hits = 0; var detail = "";
            foreach (var t in targets)
            {
                var p = t.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance);
                if (p?.GetSetMethod() == null) continue;
                object parsed;
                if (p.PropertyType == typeof(bool))
                {
                    var b = value.Trim();
                    parsed = b == "1" || b.Equals("true", StringComparison.OrdinalIgnoreCase);
                }
                else if (p.PropertyType.IsEnum)
                {
                    try { parsed = Enum.Parse(p.PropertyType, value, true); }
                    catch { return $"'{value}' is not a {p.PropertyType.Name} (options: {string.Join("/", Enum.GetNames(p.PropertyType))})"; }
                }
                else return $"unsupported target property type {p.PropertyType.Name} for '{property}'";
                var old = p.GetValue(t);
                p.SetValue(t, parsed);
                hits++; detail = $"{old} -> {p.GetValue(t)}";
            }
            return hits == 0
                ? $"no active target has a writable '{property}' (try: alphaClip, surfaceType, renderFace, zWriteControl, zTestMode, alphaMode, castShadows)"
                : $"target {property}: {detail} ({hits} target(s))";
        }

        // ---------- blackboard: properties & keywords ----------

        static readonly Dictionary<string, string> PropAlias = new(StringComparer.OrdinalIgnoreCase)
        {
            { "float", "Vector1ShaderProperty" }, { "vector1", "Vector1ShaderProperty" },
            { "vector2", "Vector2ShaderProperty" }, { "vector3", "Vector3ShaderProperty" }, { "vector4", "Vector4ShaderProperty" },
            { "color", "ColorShaderProperty" }, { "bool", "BooleanShaderProperty" }, { "boolean", "BooleanShaderProperty" },
            { "texture2d", "Texture2DShaderProperty" }, { "texture", "Texture2DShaderProperty" },
        };

        /// Add a blackboard PROPERTY (exposed material parameter). type: Float/Vector2..4/Color/Bool/Texture2D
        /// (or the exact class name). value optional ("0.5", "1,0,0,1", "true"); ignored for textures.
        public string AddProperty(string type, string referenceName, string? displayName = null, string? value = null)
        {
            var className = PropAlias.TryGetValue(type, out var c) ? c
                : type.EndsWith("ShaderProperty") ? type : type + "ShaderProperty";
            var pt = Asm.GetTypes().FirstOrDefault(t => t.Name == className && !t.IsAbstract);
            if (pt == null) return $"unknown property type '{type}' (Float/Vector2-4/Color/Bool/Texture2D)";
            var prop = Activator.CreateInstance(pt, nonPublic: true)!;
            SetMember(prop, "displayName", displayName ?? referenceName);
            SetMember(prop, "generatePropertyBlock", true);
            AddGraphInput(prop);
            SetMember(prop, "overrideReferenceName", referenceName);
            if (value != null)
            {
                var vp = pt.GetProperty("value");
                if (vp != null && TryParse(vp.PropertyType, value, out var pv, out _)) vp.SetValue(prop, pv);
            }
            return $"added property {referenceName} ({pt.Name})";
        }

        /// Add a blackboard KEYWORD. definition: ShaderFeature/MultiCompile/Predefined/DynamicBranch
        /// (DynamicBranch = one variant, branches on a uniform at runtime — the modern ubershader toggle).
        /// type: Boolean or Enum. scope: Local/Global.
        public string AddKeyword(string referenceName, string? displayName = null,
            string definition = "DynamicBranch", string type = "Boolean", string scope = "Local")
        {
            var tKw = Type("UnityEditor.ShaderGraph.ShaderKeyword");
            var kwType = Enum.Parse(EnumType("KeywordType"), type, true);
            var kw = Activator.CreateInstance(tKw, new[] { kwType })!;
            SetMember(kw, "displayName", displayName ?? referenceName);
            SetMember(kw, "keywordDefinition", Enum.Parse(EnumType("KeywordDefinition"), definition, true));
            SetMember(kw, "keywordScope", Enum.Parse(EnumType("KeywordScope"), scope, true));
            AddGraphInput(kw);
            SetMember(kw, "overrideReferenceName", referenceName);
            return $"added keyword {referenceName} ({type}/{definition}/{scope})";
        }

        // ---------- subgraph + custom-function authoring ----------

        /// Insert a SubGraphNode referencing a .shadersubgraph asset (its slots populate from the asset).
        public string AddSubGraphNode(string subGraphPath)
        {
            var tSub = Type("UnityEditor.ShaderGraph.SubGraphNode");
            var assetType = Type("UnityEditor.ShaderGraph.SubGraphAsset");
            var asset = AssetDatabase.LoadAssetAtPath(subGraphPath, assetType);
            if (asset == null) return $"subgraph asset not found: {subGraphPath}";
            var node = Activator.CreateInstance(tSub, nonPublic: true)!;
            tSub.GetProperty("asset")!.SetValue(node, asset);
            TGraph.GetMethod("AddNode", new[] { TAMN, typeof(bool) })!.Invoke(_graph, new[] { node, (object)true });
            return $"added SubGraphNode -> id {TAMN.GetProperty("objectId")!.GetValue(node)} ({subGraphPath})";
        }

        /// Configure a CustomFunctionNode (add it first via AddNode(""CustomFunctionNode""), then AddSlot the I/O).
        /// sourceType: "String" (inline body) or "File" (nameOrPath = .hlsl asset path; name from the file).
        public string SetCustomFunction(string nodeId, string functionName, string sourceType = "String",
            string? body = null, string? hlslPath = null)
        {
            var node = Node(nodeId);
            var nt = node.GetType();
            if (nt.Name != "CustomFunctionNode") return $"node {nodeId} is {nt.Name}, not CustomFunctionNode";
            nt.GetField("m_SourceType", Inst)!.SetValue(node, Enum.Parse(EnumTypeAny("HlslSourceType"), sourceType, true));
            nt.GetField("m_FunctionName", Inst)!.SetValue(node, functionName);
            if (sourceType.Equals("String", StringComparison.OrdinalIgnoreCase) && body != null)
                nt.GetField("m_FunctionBody", Inst)!.SetValue(node, body);
            if (sourceType.Equals("File", StringComparison.OrdinalIgnoreCase) && hlslPath != null)
                nt.GetField("m_FunctionSource", Inst)!.SetValue(node, AssetDatabase.AssetPathToGUID(hlslPath));
            return $"configured CustomFunctionNode {nodeId} ({sourceType}: {functionName})";
        }

        /// Add an input/output slot to a node (mainly for CustomFunctionNode). type: Vector1/2/3/4 or Boolean.
        /// inout: "in" or "out". slotId must be unique on the node.
        public string AddSlot(string nodeId, int slotId, string name, string type, string inout)
        {
            var node = Node(nodeId);
            var slotClass = type switch
            {
                "Float" or "Vector1" => "Vector1MaterialSlot",
                "Vector2" => "Vector2MaterialSlot",
                "Vector3" => "Vector3MaterialSlot",
                "Vector4" => "Vector4MaterialSlot",
                "Bool" or "Boolean" => "BooleanMaterialSlot",
                _ => type.EndsWith("MaterialSlot") ? type : type + "MaterialSlot",
            };
            var tSlot = Asm.GetTypes().FirstOrDefault(t => t.Name == slotClass);
            if (tSlot == null) return $"unknown slot type '{type}'";
            var slotType = Enum.Parse(EnumTypeAny("SlotType"),
                inout.StartsWith("out", StringComparison.OrdinalIgnoreCase) ? "OutputSlot" : "InputSlot", true);
            var slot = CreateSlot(tSlot, slotId, name, slotType);
            node.GetType().GetMethod("AddSlot", new[] { TSlot, typeof(bool) })!.Invoke(node, new[] { slot, (object)true });
            return $"added {inout} slot {slotId} '{name}' ({type}) to {nodeId}";
        }

        /// Add a PropertyNode that reads an existing blackboard property (by reference name) into the graph,
        /// so its value can be wired into the node graph. Returns the new node objectId.
        public string AddPropertyNode(string referenceName)
        {
            var prop = GraphProperties().FirstOrDefault(p =>
                p.GetType().GetProperty("referenceName")?.GetValue(p)?.ToString() == referenceName);
            if (prop == null) return $"property not found: {referenceName}";
            var tPN = Type("UnityEditor.ShaderGraph.PropertyNode");
            var node = Activator.CreateInstance(tPN, nonPublic: true)!;
            TGraph.GetMethod("AddNode", new[] { TAMN, typeof(bool) })!.Invoke(_graph, new[] { node, (object)true });
            tPN.GetProperty("property", Inst)!.SetValue(node, prop); // the setter binds the property AND builds the output slot
            (tPN.GetMethod("SetupSlots", Inst) ?? tPN.GetMethod("UpdateNodeAfterDeserialization", Inst))?.Invoke(node, null);
            return S(TAMN.GetProperty("objectId")!.GetValue(node));
        }

        /// Add an output to a subgraph (only valid on a .shadersubgraph) — a slot on its SubGraphOutputNode.
        /// type: Float/Vector2/Vector3/Vector4/Color/Bool/Texture2D.
        public string AddSubGraphOutput(string type)
        {
            var outNode = GetNodes().FirstOrDefault(n => n.GetType().Name == "SubGraphOutputNode");
            if (outNode == null) return "no SubGraphOutputNode on this graph (is it a subgraph?)";
            var tCSVT = EnumTypeAny("ConcreteSlotValueType");
            var add = outNode.GetType().GetMethod("AddSlot", new[] { tCSVT });
            if (add == null) return "SubGraphOutputNode.AddSlot(ConcreteSlotValueType) not found";
            add.Invoke(outNode, new[] { Enum.Parse(tCSVT, MapConcrete(type), true) });
            return $"added subgraph output ({type})";
        }

        /// Faithfully clone nodes (with their config, internal edges, and any properties they reference) from
        /// another graph/subgraph into this one — uses ShaderGraph's own copy/paste (id-remapped, byte-faithful,
        /// works across graphs and into subgraphs). Returns the new node objectIds (space-separated, source order).
        /// Position/Connect them afterwards; referenced properties arrive as blackboard inputs.
        public string ImportNodes(string sourcePath, params string[] nodeIds)
        {
            var src = Open(sourcePath);
            var pObjId = TAMN.GetProperty("objectId")!;
            var requested = nodeIds.Select(id => (id, node: src.Node(id))).ToList();
            var compute = requested.Where(x => x.node.GetType().Name != "PropertyNode").ToList();
            var propNodes = requested.Where(x => x.node.GetType().Name == "PropertyNode").ToList();
            var computeIds = new HashSet<string>(compute.Select(x => x.id));

            // 1) faithfully clone the COMPUTE nodes + their internal edges via copy/paste (config preserved).
            var tCPG = Type("UnityEditor.ShaderGraph.CopyPasteGraph");
            var cpg = Activator.CreateInstance(tCPG, nonPublic: true)!;
            var addNode = tCPG.GetMethods(Inst).First(m => m.Name == "AddNode");
            var addEdge = tCPG.GetMethods(Inst).First(m => m.Name == "AddEdge");
            foreach (var x in compute) addNode.Invoke(cpg, new[] { x.node });
            foreach (var e in SrcEdges(src._graph))
            {
                var (f, t, _, _) = EdgeFull(e, pObjId);
                if (computeIds.Contains(f) && computeIds.Contains(t)) addEdge.Invoke(cpg, new[] { e });
            }
            var cpg2 = tCPG.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .First(m => m.Name == "FromJson").Invoke(null, new object[] { Serialize(cpg), _graph })!;
            var tEdge = EdgeType(_graph);
            var remapped = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(TAMN))!;
            var remappedEdges = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(tEdge))!;
            TGraph.GetMethods(Inst).First(m => m.Name == "PasteGraph").Invoke(_graph, new object[] { cpg2, remapped, remappedEdges });

            // map source compute id -> new target id (paste order matches add order)
            var newIds = remapped.Cast<object>().Select(n => S(pObjId.GetValue(n))).ToList();
            var idMap = new Dictionary<string, string>();
            for (var i = 0; i < compute.Count && i < newIds.Count; i++) idMap[compute[i].id] = newIds[i];

            // 2) recreate referenced PROPERTIES as subgraph inputs + bound PropertyNodes, and re-wire them into
            //    the cloned compute nodes (the copy/paste 'inputs' path NREs in PasteGraph, so we do it ourselves).
            foreach (var p in propNodes)
            {
                if (p.node.GetType().GetField("m_Property", Inst)?.GetValue(p.node) is not { } jref ||
                    jref.GetType().GetProperty("value")?.GetValue(jref) is not { } prop) continue;
                var refName = S(prop.GetType().GetProperty("referenceName")?.GetValue(prop));
                if (GraphProperties().All(gp => S(gp.GetType().GetProperty("referenceName")?.GetValue(gp)) != refName))
                {
                    var disp = prop.GetType().GetProperty("displayName")?.GetValue(prop)?.ToString();
                    var val = prop.GetType().GetProperty("value")?.GetValue(prop);
                    AddProperty(prop.GetType().Name, refName, disp, FormatVal(val));
                }
                var pnId = AddPropertyNode(refName);
                var pnOut = Snapshot().Nodes.First(n => n.Id == pnId).Outputs[0].Id;
                foreach (var e in SrcEdges(src._graph))
                {
                    var (f, t, _, toSlot) = EdgeFull(e, pObjId);
                    if (f == p.id && idMap.TryGetValue(t, out var tgt)) Connect(pnId, pnOut, tgt, toSlot);
                }
            }
            return string.Join(" ", newIds);
        }

        IEnumerable<object> SrcEdges(object graph) =>
            TGraph.GetProperty("edges", Inst)?.GetValue(graph) is IEnumerable e ? e.Cast<object>() : Enumerable.Empty<object>();

        static (string from, string to, int fromSlot, int toSlot) EdgeFull(object edge, PropertyInfo pObjId)
        {
            var et = edge.GetType();
            var outRef = et.GetProperty("outputSlot")!.GetValue(edge)!;
            var inRef = et.GetProperty("inputSlot")!.GetValue(edge)!;
            var rt = outRef.GetType();
            var fromNode = rt.GetProperty("node")!.GetValue(outRef)!;
            var toNode = rt.GetProperty("node")!.GetValue(inRef)!;
            return (S(pObjId.GetValue(fromNode)), S(pObjId.GetValue(toNode)),
                (int)rt.GetProperty("slotId")!.GetValue(outRef)!, (int)rt.GetProperty("slotId")!.GetValue(inRef)!);
        }

        static string FormatVal(object? v) => v switch
        {
            float f => f.ToString(CultureInfo.InvariantCulture),
            Vector2 v2 => $"{v2.x},{v2.y}",
            Vector3 v3 => $"{v3.x},{v3.y},{v3.z}",
            Vector4 v4 => $"{v4.x},{v4.y},{v4.z},{v4.w}",
            Color c => $"{c.r},{c.g},{c.b},{c.a}",
            bool b => b ? "true" : "false",
            _ => "0",
        };

        Type EdgeType(object graph) => SrcEdges(graph).FirstOrDefault()?.GetType() ?? Type("UnityEditor.Graphing.Edge");

        IEnumerable<object> GetNodes()
        {
            var m = TGraph.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .First(x => x.Name == "GetNodes" && x.IsGenericMethodDefinition).MakeGenericMethod(TAMN);
            return m.Invoke(_graph, null) is IEnumerable e ? e.Cast<object>() : Enumerable.Empty<object>();
        }

        /// Strip a copied-template subgraph down to a blank one: remove every node except the SubGraphOutputNode,
        /// remove all blackboard inputs (properties + keywords), and clear the output node's slots. Run this right
        /// after CopyAsset'ing a template .shadersubgraph, before ImportNodes/AddSubGraphOutput.
        public string ClearForSubGraph()
        {
            var outNode = GetNodes().FirstOrDefault(n => n.GetType().Name == "SubGraphOutputNode");
            var removeNode = TGraph.GetMethod("RemoveNode", new[] { TAMN })!;
            foreach (var n in GetNodes().Where(n => n.GetType().Name != "SubGraphOutputNode").ToList())
                removeNode.Invoke(_graph, new[] { n });
            var removeInput = TGraph.GetMethods(Inst).First(m => m.Name == "RemoveGraphInput");
            foreach (var inp in GraphInputs().ToList()) removeInput.Invoke(_graph, new[] { inp });
            if (outNode != null)
            {
                var slotList = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(TSlot))!;
                TAMN.GetMethods().First(m => m.Name == "GetSlots" && m.IsGenericMethodDefinition)
                    .MakeGenericMethod(TSlot).Invoke(outNode, new object[] { slotList });
                var pSlotId = TSlot.GetProperty("id")!;
                var removeSlot = TAMN.GetMethod("RemoveSlot", new[] { typeof(int) })!;
                foreach (var sl in slotList.Cast<object>().ToList())
                    removeSlot.Invoke(outNode, new object[] { (int)pSlotId.GetValue(sl)! });
            }
            return "cleared to blank subgraph";
        }

        IEnumerable<object> GraphInputs()
        {
            var list = new List<object>();
            if (TGraph.GetProperty("properties", Inst)?.GetValue(_graph) is IEnumerable props) list.AddRange(props.Cast<object>());
            if (TGraph.GetProperty("keywords", Inst)?.GetValue(_graph) is IEnumerable kws) list.AddRange(kws.Cast<object>());
            return list;
        }

        static object MakeJsonRef(Type jsonRefT, object value)
        {
            var op = jsonRefT.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "op_Implicit" && m.GetParameters().Length == 1 && m.ReturnType == jsonRefT);
            if (op != null) return op.Invoke(null, new[] { value })!;
            var ctor = jsonRefT.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(c => c.GetParameters().Length == 1);
            return ctor != null ? ctor.Invoke(new[] { value })! : throw new Exception("cannot construct JsonRef");
        }

        static string S(object? o) => o?.ToString() ?? "";

        static string MapConcrete(string t) => t switch
        {
            "Float" or "Vector1" => "Vector1",
            "Vector2" => "Vector2", "Vector3" => "Vector3",
            "Vector4" or "Color" => "Vector4",
            "Bool" or "Boolean" => "Boolean",
            "Texture2D" => "Texture2D",
            _ => t,
        };

        // ---------- mutator helpers ----------

        IEnumerable<object> ActiveTargets() =>
            TGraph.GetProperty("activeTargets", Inst)?.GetValue(_graph) is IEnumerable e ? e.Cast<object>() : Enumerable.Empty<object>();

        void AddGraphInput(object input) =>
            TGraph.GetMethod("AddGraphInput", new[] { Type("UnityEditor.ShaderGraph.Internal.ShaderInput"), typeof(int) })!
                .Invoke(_graph, new object[] { input, -1 });

        static Type EnumType(string name) =>
            Asm.GetType("UnityEditor.ShaderGraph.Internal." + name) ?? EnumTypeAny(name);
        static Type EnumTypeAny(string name) =>
            Asm.GetTypes().First(t => t.Name == name && t.IsEnum);

        /// Set a property (incl. non-public setter) or its backing field, walking the type hierarchy.
        /// Handles ShaderGraph's get-only auto-properties (e.g. keyword settings) and the displayName/refName
        /// fields that don't follow the m_Xxx convention.
        static bool SetMember(object o, string name, object val)
        {
            const BindingFlags bf = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            for (var t = o.GetType(); t != null && t != typeof(object); t = t.BaseType)
            {
                var p = t.GetProperty(name, bf);
                if (p?.GetSetMethod(true) != null) { p.SetValue(o, val); return true; }
                foreach (var fn in new[]
                {
                    $"<{name}>k__BackingField", "m_" + Cap(name), "m_" + name, name,
                    name == "displayName" ? "m_Name" : null,
                    name == "overrideReferenceName" ? "m_OverrideReferenceName" : null,
                })
                {
                    if (fn == null) continue;
                    var f = t.GetField(fn, bf);
                    if (f != null) { f.SetValue(o, val); return true; }
                }
            }
            return false;
        }

        static string Cap(string s) => char.ToUpperInvariant(s[0]) + s.Substring(1);

        /// Build a MaterialSlot via its longest ctor, filling params by name/type with sane defaults.
        static object CreateSlot(Type tSlot, int id, string name, object slotType)
        {
            var ctor = tSlot.GetConstructors().Where(c => c.GetParameters().Length > 0)
                .OrderByDescending(c => c.GetParameters().Length).First();
            var ps = ctor.GetParameters();
            var args = new object?[ps.Length];
            for (var i = 0; i < ps.Length; i++)
            {
                var pt = ps[i].ParameterType;
                args[i] = ps[i].Name switch
                {
                    "slotId" => id,
                    "displayName" => name,
                    "shaderOutputName" => name,
                    "slotType" => slotType,
                    _ => pt.Name == "ShaderStageCapability" ? Enum.Parse(pt, "All", true)
                        : pt == typeof(string) ? ""
                        : pt.IsValueType ? Activator.CreateInstance(pt)
                        : null,
                };
            }
            return ctor.Invoke(args)!;
        }

        /// Persist the current state to disk (canonical MultiJson) and reimport the asset.
        /// Only reimports when the path is a project asset; a scratch/external path is just written.
        public string Save()
        {
            File.WriteAllText(_path, Serialize(_graph));
            var p = _path.Replace('\\', '/');
            if (p.StartsWith("Assets/") || p.StartsWith("Packages/"))
                AssetDatabase.ImportAsset(_path);
            return $"saved {_path}";
        }

        // ---------- internals ----------

        IEnumerable<object> GraphProperties() =>
            TGraph.GetProperty("properties", Inst)?.GetValue(_graph) is IEnumerable e ? e.Cast<object>() : Enumerable.Empty<object>();

        object Node(string nodeId)
        {
            var get = TGraph.GetMethods().First(m =>
                m.Name == "GetNodeFromId" && !m.IsGenericMethod && m.GetParameters().Length == 1);
            return get.Invoke(_graph, new object[] { nodeId }) ?? throw new Exception($"node not found: {nodeId}");
        }

        static object SlotRef(object node, int slotId) =>
            TAMN.GetMethod("GetSlotReference", new[] { typeof(int) })!.Invoke(node, new object[] { slotId })!;

        static object? FindSlot(object node, int slotId) =>
            TAMN.GetMethods().First(m => m.Name == "FindSlot" && m.IsGenericMethodDefinition)
                .MakeGenericMethod(TSlot).Invoke(node, new object[] { slotId });

        static bool TryParse(Type t, string s, out object value, out string error)
        {
            try { value = ParseValue(t, s); error = ""; return true; }
            catch (Exception e) { value = null!; error = $"can't parse '{s}' as {t.Name} ({e.Message})"; return false; }
        }

        static object ParseValue(Type t, string s)
        {
            s = s.Trim();
            if (t == typeof(bool)) return s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase);
            var n = s.Split(',').Select(x => float.Parse(x.Trim(), CultureInfo.InvariantCulture)).ToArray();
            if (t == typeof(float)) return n[0];
            if (t == typeof(Vector2)) return new Vector2(n[0], n[1]);
            if (t == typeof(Vector3)) return new Vector3(n[0], n[1], n[2]);
            if (t == typeof(Vector4)) return new Vector4(n[0], n[1], n[2], n.Length > 3 ? n[3] : 1f);
            if (t == typeof(Color)) return new Color(n[0], n[1], n[2], n.Length > 3 ? n[3] : 1f);
            throw new NotSupportedException($"unsupported value type {t.Name}");
        }

        /// One-screen cheatsheet for a fresh agent driving this from RunCommand.
        public static string Help() => @"ShaderGraph editing API (UnityJigs.Assistant.Editor) — drive from Unity_RunCommand.

READ (token-efficient, no session needed):
  ShaderGraphReader.Decompile(path)            -> pseudo-shadercode string
  (or the MCP tool Unity_ShaderGraphRead)

EDIT (session — mutate the real model, then Save once):
  var s = ShaderGraphSession.Open(path);       // loads live GraphData
  var g = s.Snapshot();                         // SgGraph: .Nodes/.Edges/.Properties
       //  SgNode  { Id, Type, Name, Inputs[], Outputs[] }   (Id = 32-hex objectId)
       //  SgSlot  { Id (int), Name, Type, Value }
       //  SgProp  { DisplayName, RefName, Type }
  // find what you want to touch:
  var n = g.Nodes.First(x => x.Name == ""Multiply"");
  var blk = g.Nodes.First(x => x.Name == ""SurfaceDescription.BaseColor"");

  s.SetProperty(refName, ""0.5"")               // property default ('1,0,0,1', 'true' also ok)
  s.SetInput(nodeId, slotId, ""0.5"")           // an unconnected input slot's default
  s.Connect(fromNodeId, fromSlotId, toNodeId, toSlotId)   // replaces existing edge into target
  s.Disconnect(toNodeId, toSlotId)             // clears edges into an input
  s.AddNode(""MultiplyNode"")                    // returns new objectId; re-Snapshot() for its slots
  s.RemoveNode(nodeId)

  // --- target / blackboard / authoring (added for ubershader + alpha-clip work) ---
  s.SetTarget(""alphaClip"", ""false"")            // render-state: alphaClip OFF restores early-Z;
       //  also surfaceType/renderFace/zWriteControl/zTestMode/alphaMode/castShadows
  s.AddProperty(""Float"", ""_Foo"", ""Foo"", ""0.5"")     // Float/Vector2-4/Color/Bool/Texture2D blackboard prop
  s.AddKeyword(""_FEATURE_ROCK"", ""Rock"", ""DynamicBranch"", ""Boolean"")  // DynamicBranch = 1 variant, uniform branch
  s.AddSubGraphNode(""Assets/.../Toon.shadersubgraph"")   // returns objectId; slots populate from the asset
  s.AddNode(""CustomFunctionNode"");  s.SetCustomFunction(id, ""MyFn"", ""String"", body: ""Out = A;"")
  s.AddSlot(id, 0, ""A"", ""Vector3"", ""in"");  s.AddSlot(id, 1, ""Out"", ""Vector3"", ""out"")

  s.Decompile()                                 // preview current (unsaved) state
  s.Save();                                     // write canonical MultiJson + reimport

NOTES: slot ids come from Snapshot() (Inputs/Outputs[].Id), NOT positional. Master-stack outputs are
nodes named 'SurfaceDescription.X' / 'VertexDescription.X' with a single input slot (usually id 0).
Connect expects (output slot) -> (input slot). Always Snapshot()/Decompile() to verify before Save().";
    }
}
