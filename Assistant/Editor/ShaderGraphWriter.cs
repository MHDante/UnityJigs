using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using static UnityJigs.Assistant.Editor.SgReflection;

namespace UnityJigs.Assistant.Editor
{
    /// Safe mutation of .shadergraph files. The pattern is always: deserialize the live internal
    /// GraphData, mutate it through the real model, then re-serialize via MultiJson — so the written
    /// file is byte-for-byte what the ShaderGraph editor itself would produce (no risk of corrupting
    /// objectIds or slot wiring by editing JSON directly).
    public static class ShaderGraphWriter
    {
        /// Sets a shader property's default value, addressed by reference name (e.g. "_PhaseThres").
        /// <paramref name="value"/> is a comma-separated literal: "0.5", "1,0,0,1", "true".
        public static string SetProperty(string path, string referenceName, string value)
        {
            var graph = Deserialize(path);
            var prop = FindProperty(graph, referenceName);
            if (prop == null)
                return $"property not found: {referenceName}  (have: {string.Join(", ", PropertyNames(graph))})";

            var valueProp = prop.GetType().GetProperty("value");
            if (valueProp == null) return $"property '{referenceName}' has no settable value";

            object parsed;
            try { parsed = ParseValue(valueProp.PropertyType, value); }
            catch (Exception e) { return $"could not parse '{value}' as {valueProp.PropertyType.Name}: {e.Message}"; }

            var old = valueProp.GetValue(prop);
            valueProp.SetValue(prop, parsed);
            Save(path, graph);
            return $"{referenceName}: {old} -> {parsed}";
        }

        /// Reads a property's current default value (re-deserialized from disk each call).
        public static string ReadProperty(string path, string referenceName)
        {
            var prop = FindProperty(Deserialize(path), referenceName);
            if (prop == null) return $"property not found: {referenceName}";
            return prop.GetType().GetProperty("value")?.GetValue(prop)?.ToString() ?? "<none>";
        }

        static object? FindProperty(object graph, string referenceName)
        {
            if (GraphProperties(graph) is not { } props) return null;
            foreach (var p in props)
                if (p.GetType().GetProperty("referenceName")?.GetValue(p)?.ToString() == referenceName) return p;
            return null;
        }

        static string[] PropertyNames(object graph) =>
            GraphProperties(graph) is { } props
                ? props.Cast<object>().Select(p => p.GetType().GetProperty("referenceName")?.GetValue(p)?.ToString() ?? "?").ToArray()
                : Array.Empty<string>();

        static IEnumerable? GraphProperties(object graph) =>
            Type("UnityEditor.ShaderGraph.GraphData").GetProperty("properties", Inst)?.GetValue(graph) as IEnumerable;

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
            throw new NotSupportedException($"unsupported property value type {t.Name}");
        }

        static void Save(string path, object graph)
        {
            File.WriteAllText(path, Serialize(graph));
            AssetDatabase.ImportAsset(path);
        }
    }
}
