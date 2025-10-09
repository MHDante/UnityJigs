using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 🔮 Reflection helper that locates and proxies any internal Unity Editor type.
/// </summary>
public sealed class ForbiddenEditor : IDisposable
{
    private Editor? _instance;
    private Type? _editorType;
    private readonly string[] _candidates;
    private bool _triedResolve;

    public bool IsValid => _instance != null;
    public string? ResolvedTypeName => _editorType?.FullName;

    public ForbiddenEditor(string[] candidateTypeNames, UnityEngine.Object[] targets)
    {
        _candidates = candidateTypeNames;
        TryResolveAndCreate(targets);
    }

    /// <summary>
    /// Draws the internal inspector if possible, falling back gracefully.
    /// </summary>
    public void DrawInspector()
    {
        if (!_triedResolve)
            _triedResolve = true;

        if (_instance == null)
        {
            EditorGUILayout.HelpBox(
                $"[ForbiddenEditor] Failed to draw internal inspector (none found).",
                MessageType.Info);
            return;
        }

        try { _instance.OnInspectorGUI(); }
        catch (Exception e)
        {
            EditorGUILayout.HelpBox($"[ForbiddenEditor] Exception:\n{e.Message}", MessageType.Error);
        }
    }

    /// <summary> Optionally call OnSceneGUI() if the internal editor defines it. </summary>
    public void DrawSceneGUI()
    {
        if (_instance == null) return;
        try
        {
            var m = _editorType?.GetMethod("OnSceneGUI", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            m?.Invoke(_instance, null);
        }
        catch { /* swallow safely */ }
    }

    /// <summary> Clean up instantiated editor. </summary>
    public void Dispose()
    {
        if (_instance != null)
            UnityEngine.Object.DestroyImmediate(_instance);
        _instance = null;
    }

    // ---------------- helpers ----------------

    private void TryResolveAndCreate(UnityEngine.Object[] targets)
    {
        if (_triedResolve) return;
        _triedResolve = true;

        _editorType = ResolveType(_candidates);
        if (_editorType == null)
        {
            Debug.LogWarning($"[ForbiddenEditor] Could not locate any of: {string.Join(", ", _candidates)}");
            return;
        }

        try
        {
            _instance = Editor.CreateEditor(targets, _editorType);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ForbiddenEditor] Failed to create {_editorType.FullName}: {e.Message}");
            _instance = null;
        }
    }

    private static Type? ResolveType(string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            // Try direct Type.GetType first (assembly-qualified)
            var t = Type.GetType(candidate, throwOnError: false);
            if (t != null) return t;

            // Then search all assemblies for the simple FullName
            var simple = candidate.Split(',')[0].Trim();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.IsDynamic) continue;
                try
                {
                    t = asm.GetType(simple, throwOnError: false);
                    if (t != null) return t;
                }
                catch { /* ignore */ }
            }
        }
        return null;
    }
}
