using System;
using System.Linq;
using FMODUnity;
using Sirenix.OdinInspector.Editor;
using Sirenix.OdinInspector.Editor.ValueResolvers;
using Sirenix.Utilities.Editor;
using UnityEditor;
using UnityEngine;

namespace UnityJigs.Fmod.Editor
{
    public sealed class FmodParameterRefDrawer : OdinValueDrawer<FmodParameterRef>
    {
        private ValueResolver<EventReference>? _eventRefResolver;

        protected override void Initialize()
        {
            var attr = Property.GetAttribute<FmodParameterPickerAttribute>();
            var memberPath = attr?.EventRefMember ?? "EventReference";
            _eventRefResolver = ValueResolver.Get<EventReference>(Property, memberPath);
        }

        protected override void DrawPropertyLayout(GUIContent? label)
        {
            var param = ValueEntry.SmartValue;
            var evtRef = _eventRefResolver?.GetValue() ?? default;

            using var _ = new EditorGUI.DisabledGroupScope(evtRef.IsNull);

            // Parameter dropdown (uses property name as label)
            var metas = FmodParameterMetadataCache.GetParameters(evtRef);
            var names = metas.Select(m => m.Name).ToArray();

            var dropdownLabel = label ?? new GUIContent(Property.NiceName);
            var currentIndex = Mathf.Max(0, Array.FindIndex(names, n => n == param.Name));
            var nextIndex = EditorGUILayout.Popup(dropdownLabel, currentIndex, names);

            if (nextIndex >= 0 && nextIndex < metas.Count)
            {
                var chosen = metas[nextIndex];
                param.Name = chosen.Name;

                var displayValue = param.Value;
                if (param.TryReadLive(out var live)) displayValue = live;

                // Draw inline value control with meta hint in the label slot.
                displayValue = DrawValueControlInline(chosen, displayValue, drawMetaHint: true);

                if (!Mathf.Approximately(displayValue, param.Value))
                {
                    param.Set(displayValue);
                    param.Value = displayValue;
                    GUIHelper.RequestRepaint();
                }
            }

            ValueEntry.SmartValue = param;
        }

        /// <summary>
        /// Draws a single-row value control. If drawMetaHint is true, the meta hint is drawn in the label slot;
        /// otherwise the full rect is used for the control (no meta/label).
        /// </summary>
        public static float DrawValueControlInline(FmodParameterMeta meta, float value, bool drawMetaHint)
        {
            var r = EditorGUILayout.GetControlRect();
            if (drawMetaHint)
            {
                var lw = EditorGUIUtility.labelWidth;
                var labelRect = new Rect(r.x, r.y, lw, r.height);
                var fieldRect = new Rect(r.x + lw, r.y, r.width - lw, r.height);

                var hint = meta.HasRange
                    ? $"default {meta.Default:0.###}  •  [{meta.Min:0.###}, {meta.Max:0.###}]"
                    : $"default {meta.Default:0.###}";

                EditorGUI.LabelField(labelRect, hint, EditorStyles.miniLabel);
                return DrawValueField(fieldRect, meta, value);
            }

            // No meta: just use the whole row as the field area.
            return DrawValueField(r, meta, value);
        }

        /// <summary>
        /// Draws ONLY the control into the provided rect (no label/meta). Reusable by other drawers.
        /// </summary>
        public static float DrawValueField(Rect fieldRect, FmodParameterMeta meta, float value)
        {
            if (meta is { IsLabeled: true, Labels.Length: > 0 })
            {
                var intVal = Mathf.Clamp(Mathf.RoundToInt(value), 0, Mathf.Max(0, meta.Labels!.Length - 1));
                var next = EditorGUI.Popup(fieldRect, intVal, meta.Labels);
                return next;
            }

            if (meta.HasRange)
            {
                var min = meta.Min;
                var max = meta.Max;
                if (max > min && !float.IsInfinity(min) && !float.IsInfinity(max))
                {
                    return EditorGUI.Slider(fieldRect, value, min, max);
                }
            }

            return EditorGUI.FloatField(fieldRect, value);
        }
    }

    // ... (FmodParameterMetadataCache and FmodParameterMeta remain unchanged) ...
}
