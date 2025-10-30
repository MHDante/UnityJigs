using System;
using System.Diagnostics.CodeAnalysis;
using Sirenix.OdinInspector.Editor;
using Sirenix.OdinInspector.Editor.ValueResolvers;
using UnityEditor;
using UnityEngine;

namespace UnityJigs.Editor.Odin
{
    public static class OdinUtils
    {
        [return: NotNullIfNotNull("defaultValue")]
        public static T? GetOrDefault<T>(this PropertyState s, string key, T? defaultValue = default) =>
            s.Exists<T>(key, out _) ? s.Get<T>(key) : defaultValue;

        public static T SetOrCreate<T>(this PropertyState s, string key, T value, bool persistent = false)
        {
            var exists = s.Exists<T>(key, out var isPersistent);
            if (!exists)
            {
                s.Create(key, persistent, value);
                return value;
            }

            if (isPersistent != persistent) throw new Exception("Key already exists with different persistence");
            s.Set(key, value);
            return value;
        }

        public static bool Exists<T>(this PropertyState s, string key) => Exists<T>(s, key, out var _);

        public static bool Exists<T>(this PropertyState s, string key, out bool isPersistent) =>
            s.Exists(key, out isPersistent, out Type type) && type == typeof(T);

        public static bool Toggle(this PropertyState s, string key, bool initialValue = false,
            bool persistent = false) =>
            s.SetOrCreate(key, !s.GetOrDefault(key, initialValue), persistent);

        public static SerializedProperty ToSerializedProperty(this InspectorProperty inspectorProperty)
        {
            string unityPropertyPath = inspectorProperty.UnityPropertyPath;
            return inspectorProperty.Tree.UnitySerializedObject.FindProperty(unityPropertyPath);
        }

        // Tunable visual constants
        private const float MinVisualHandleSize = 0.01f; // absolute floor in world units
        private const float MinRelativeHandleFrac = 0.02f; // fraction of min extent
        private const float MaxRelativeHandleFrac = 0.2f; // fraction of min extent

        public static Vector3 DrawSymmetricBoundsHandles(Transform transform, Vector3 extents, Color color,
            bool drawWire = true)
        {
            var t = transform;
            var worldRot = t.rotation;
            var worldPos = t.position;
            var worldScale = t.lossyScale;

            if (drawWire)
            {
                using (new Handles.DrawingScope(t.localToWorldMatrix))
                {
                    Handles.color = new Color(color.r, color.g, color.b, 0.5f);
                    Handles.DrawWireCube(Vector3.zero, extents * 2f);
                }
            }

            // Ensure extents are non-negative
            extents = new Vector3(
                Mathf.Max(0f, extents.x),
                Mathf.Max(0f, extents.y),
                Mathf.Max(0f, extents.z)
            );

            // Use smallest non-zero axis as baseline; fall back to small constant if all zero
            var minExtent = Mathf.Max(Mathf.Min(extents.x, Mathf.Min(extents.y, extents.z)), 0.001f);
            var newExtents = extents;

            for (int axis = 0; axis < 3; axis++)
            {
                if (Mathf.Abs(worldScale[axis]) < 1e-6f)
                    continue;

                var localDir = axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;
                var worldDir = worldRot * localDir;
                var offset = Vector3.Scale(extents, worldScale);

                for (int side = -1; side <= 1; side += 2)
                {
                    var sign = side;
                    var handlePos = worldPos + worldRot * (localDir * sign * offset[axis]);

                    Handles.color = axis switch
                    {
                        0 => Handles.xAxisColor,
                        1 => Handles.yAxisColor,
                        _ => Handles.zAxisColor
                    };

                    // --- proportional handle size based on smallest non-zero extent ---
                    var sizeByCamera = HandleUtility.GetHandleSize(handlePos) * 0.05f;
                    var sizeByExtent = minExtent * 0.2f;

                    var minRelative = minExtent * MinRelativeHandleFrac;
                    var maxRelative = minExtent * MaxRelativeHandleFrac;

                    var handleSize = Mathf.Max(
                        Mathf.Clamp(Mathf.Lerp(sizeByCamera, sizeByExtent, 0.25f), minRelative, maxRelative),
                        MinVisualHandleSize
                    );

                    var newPos = Handles.Slider(handlePos, worldDir * sign, handleSize, Handles.CubeHandleCap, 0f);
                    var delta = Vector3.Dot(newPos - handlePos, worldDir * sign);

                    if (Mathf.Abs(delta) > Mathf.Epsilon)
                        newExtents[axis] = Mathf.Max(0f, newExtents[axis] + delta / worldScale[axis] * 0.5f);
                }
            }

            return newExtents;
        }

        public static InspectorProperty GetTargetProperty(this ValueResolver resolver)
        {
            ref var ctx = ref resolver.Context;
            return ctx.ContextProperty.Children.Get(ctx.ResolvedString);
        }
    }
}
