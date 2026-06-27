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

  s.Decompile()                                 // preview current (unsaved) state
  s.Save();                                     // write canonical MultiJson + reimport

NOTES: slot ids come from Snapshot() (Inputs/Outputs[].Id), NOT positional. Master-stack outputs are
nodes named 'SurfaceDescription.X' / 'VertexDescription.X' with a single input slot (usually id 0).
Connect expects (output slot) -> (input slot). Always Snapshot()/Decompile() to verify before Save().";
    }
}
