using System;
using Sirenix.OdinInspector.Editor;
using Sirenix.OdinInspector.Editor.ValueResolvers;
using UnityEditor;
using UnityEngine;
using UnityJigs.Attributes;
using UnityJigs.Editor.Odin;

namespace UnityJigs.Editor.CustomDrawers
{
    /// <summary>
    /// Draws a symmetric 3D bounds handle for Vector3 fields marked with [ExtentsHandle].
    /// The value represents half-extents in local space, centered on the host component's transform.
    /// Supports dynamic color resolution via Odin expressions or fields.
    /// </summary>
    [DrawerPriority(DrawerPriorityLevel.SuperPriority)]
    public sealed class ExtentsHandleDrawer : OdinAttributeDrawer<ExtentsHandle, Vector3>, IDisposable
    {
        private ValueResolver<Color>? _colorResolver;
        private bool _drawWire;
        private static readonly Color DefaultColor = new(0.2f, 0.7f, 1f, 0.5f);

        // cached delegate to avoid per-frame allocations
        private Action<SceneView>? _cachedHandler;
        private Component? _component;
        private Transform? _transform;

        protected override void Initialize()
        {
            if (!string.IsNullOrEmpty(Attribute.ColorResolver))
                _colorResolver = ValueResolver.Get<Color>(Property, Attribute.ColorResolver!);

            _drawWire = Attribute.DrawWire;

            // Cache target references
            _component = Property.Tree.WeakTargets[0] as Component;
            _transform = _component ? _component.transform : null;

            // Prebuild delegate once
            _cachedHandler = OnSceneGUI;
            SceneView.duringSceneGui -= _cachedHandler;
            SceneView.duringSceneGui += _cachedHandler;
        }

        protected override void DrawPropertyLayout(GUIContent label)
        {
            CallNextDrawer(label);
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (!_component || !_transform)
                return;

            if (!SceneView.currentDrawingSceneView)
                return;

            Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;

            var extents = ValueEntry.SmartValue;
            var color = DefaultColor;

            if (_colorResolver != null)
            {
                try { color = _colorResolver.GetValue(); }
                catch { /* fallback to default */ }
            }

            EditorGUI.BeginChangeCheck();
            var newExtents = OdinUtils.DrawSymmetricBoundsHandles(_transform, extents, color, _drawWire);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_component, $"Adjust {Property.NiceName}");
                ValueEntry.SmartValue = newExtents;
                EditorUtility.SetDirty(_component);
            }
        }

        public void Dispose()
        {
            if (_cachedHandler != null)
                SceneView.duringSceneGui -= _cachedHandler;
        }
    }
}
