#if UNITY_EDITOR
using System;
using Sirenix.OdinInspector.Editor;
using Sirenix.OdinInspector.Editor.ValueResolvers;
using UnityEditor;
using UnityEngine;
using UnityJigs.Attributes;

namespace UnityJigs.Editor.CustomDrawers
{
    /// <summary>
    /// Odin drawer that renders a joystick-style SceneView handle for Vector3 direction fields.
    /// Includes per-axis arrow constraints (local X/Y/Z) for fine directional adjustment.
    /// </summary>
    [DrawerPriority(DrawerPriorityLevel.SuperPriority)]
    public sealed class DirectionHandleDrawer : OdinAttributeDrawer<DirectionHandle, Vector3>, IDisposable
    {
        private ValueResolver<Color>? _colorResolver;
        private static readonly Color DefaultColor = new(0.8f, 0.6f, 0.2f, 0.8f);

        private Component? _component;
        private Transform? _transform;
        private Action<SceneView>? _cachedHandler;

        private float _radius;

        protected override void Initialize()
        {
            if (!string.IsNullOrEmpty(Attribute.ColorResolver))
                _colorResolver = ValueResolver.Get<Color>(Property, Attribute.ColorResolver!);

            _radius = Mathf.Max(0.01f, Attribute.Radius);

            _component = Property.Tree.WeakTargets[0] as Component;
            _transform = _component != null ? _component.transform : null;

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
            if (!_component || _transform == null)
                return;


            if (!SceneView.currentDrawingSceneView)
                return;

            var dir = ValueEntry.SmartValue;
            if (dir == Vector3.zero)
                dir = _transform.forward;

            var origin = _transform.position;
            var color = DefaultColor;

            if (_colorResolver != null)
            {
                try
                {
                    color = _colorResolver.GetValue();
                }
                catch
                {
                    /* fallback */
                }
            }

            Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;

            var handlePos = origin + dir.normalized * _radius;

            // --- Direction line ---
            Handles.color = color;
            Handles.DrawAAPolyLine(3f, origin, handlePos);

            // --- Free drag sphere ---
            var sphereSize = HandleUtility.GetHandleSize(handlePos) * 0.1f;

            EditorGUI.BeginChangeCheck();
            var newPos = Handles.FreeMoveHandle(handlePos, Quaternion.identity, sphereSize, Vector3.zero,
                Handles.SphereHandleCap);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_component, $"Adjust {Property.NiceName}");
                var newDir = newPos - origin;
                if (newDir.sqrMagnitude > 0.0001f)
                    newDir.Normalize();
                ValueEntry.SmartValue = newDir;
                EditorUtility.SetDirty(_component);
                return; // skip axis handles this frame to avoid overlap
            }

            // --- Axis arrows around the sphere ---
            var t = _transform;
            var localAxes = new[]
            {
                (axis: Vector3.right, color: Handles.xAxisColor),
                (axis: Vector3.up, color: Handles.yAxisColor),
                (axis: Vector3.forward, color: Handles.zAxisColor)
            };

            var arrowLen = sphereSize * 2.5f;
            foreach (var (axis, axisColor) in localAxes)
            {
                var worldDir = t.TransformDirection(axis);
                var arrowStart = handlePos;
                var arrowEnd = arrowStart + worldDir * arrowLen;

                Handles.color = axisColor;
                Handles.DrawAAPolyLine(2f, arrowStart, arrowEnd);
                Handles.ConeHandleCap(0, arrowEnd, Quaternion.LookRotation(worldDir), arrowLen * 0.3f,
                    EventType.Repaint);

                // make the arrow draggable
                EditorGUI.BeginChangeCheck();
                var newArrowEnd = Handles.Slider(arrowEnd, worldDir, arrowLen * 0.5f, Handles.ConeHandleCap, 0f);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_component, $"Adjust {Property.NiceName}");
                    var delta = newArrowEnd - arrowEnd;
                    var moveAlong = Vector3.Project(delta, worldDir);
                    var newDir = dir + moveAlong;
                    if (newDir.sqrMagnitude > 0.0001f)
                        newDir.Normalize();
                    ValueEntry.SmartValue = newDir;
                    EditorUtility.SetDirty(_component);
                }
            }
        }


        public void Dispose()
        {
            if (_cachedHandler != null)
                SceneView.duringSceneGui -= _cachedHandler;
        }
    }
}
#endif
