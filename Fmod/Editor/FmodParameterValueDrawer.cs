// FmodParameterValueDrawer.cs

using System.Linq;
using FMODUnity;
using Sirenix.OdinInspector.Editor;
using Sirenix.OdinInspector.Editor.ValueResolvers;
using UnityEditor;
using UnityEngine;
using UnityJigs.Editor.Odin;

namespace UnityJigs.Fmod.Editor
{
    /// <summary>
    /// Draws a float using FMOD parameter UI (slider / enum popup), resolving a nearby FmodParameterRef and EventReference.
    /// The field keeps its own label; meta hint is NOT shown here.
    /// </summary>
    public sealed class FmodParameterValueDrawer : OdinAttributeDrawer<FmodParameterValuePickerAttribute, float>
    {
        private ValueResolver<FmodParameterRef>? _paramRefResolver;
        private ValueResolver<EventReference>? _eventRefResolver;

        protected override void Initialize()
        {
            var attr = Property.GetAttribute<FmodParameterValuePickerAttribute>();

            // Parameter ref resolver (required)
            var paramPath = attr?.ParamRefMember ?? "Parameter";
            _paramRefResolver = ValueResolver.Get<FmodParameterRef>(Property, paramPath);
            var prop = _paramRefResolver.GetTargetProperty();

            var attr2 = prop.GetAttribute<FmodParameterPickerAttribute>();
            // Event reference resolver (optional; defaults to "EventReference")
            var evtPath = attr2?.EventRefMember ?? "EventReference";
            _eventRefResolver = ValueResolver.Get<EventReference>(Property, evtPath);
        }

        protected override void DrawPropertyLayout(GUIContent? label)
        {
            var value = ValueEntry.SmartValue;

            // Resolve dependencies
            var paramRef = _paramRefResolver?.GetValue() ?? default;
            var evtRef = _eventRefResolver?.GetValue() ?? default;

            // Fallback: if we can't resolve the context, draw a normal float field.
            if (evtRef.IsNull || string.IsNullOrEmpty(paramRef.Name))
            {
                ValueEntry.SmartValue = EditorGUILayout.FloatField(label, value);
                return;
            }

            // Find metadata for the selected parameter on this event.
            var metas = FmodParameterMetadataCache.GetParameters(evtRef);
            var meta = metas.FirstOrDefault(m => m.Name == paramRef.Name);

            // If missing, fallback to plain float field.
            if (string.IsNullOrEmpty(meta.Name))
            {
                ValueEntry.SmartValue = EditorGUILayout.FloatField(label, value);
                return;
            }

            // Prefer live value if an instance is bound.
            var displayValue = value;
            if (paramRef.TryReadLive(out var live)) displayValue = live;

            // Single-row layout: draw the label, then draw ONLY the control (no meta) in the field rect.
            var r = EditorGUILayout.GetControlRect();
            var lw = EditorGUIUtility.labelWidth;
            var labelRect = new Rect(r.x, r.y, lw, r.height);
            var fieldRect = new Rect(r.x + lw, r.y, r.width - lw, r.height);

            EditorGUI.LabelField(labelRect, label ?? new GUIContent(Property.NiceName));
            var next = FmodParameterRefDrawer.DrawValueField(fieldRect, meta, displayValue);

            if (!Mathf.Approximately(next, value))
            {
                // Update the serialized float
                ValueEntry.SmartValue = next;

                // Push to the live FMOD instance if present
                paramRef.Set(next);

                // Keep inspector snappy while scrubbing
                Sirenix.Utilities.Editor.GUIHelper.RequestRepaint();
            }
        }
    }
}
