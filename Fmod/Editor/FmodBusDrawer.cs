using System;
using System.Collections.Generic;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace UnityJigs.Fmod.Editor
{
    /// <summary>
    /// Dropdown of every bus in the built banks (via the FMOD preview system). Falls back to a plain text
    /// field when nothing can be enumerated (banks not built yet), and keeps a now-missing path selectable
    /// so a stale reference is visible rather than silently rewritten.
    /// </summary>
    public sealed class FmodBusDrawer : OdinValueDrawer<FmodBus>
    {
        private const string MissingSuffix = "  (missing)";

        protected override void DrawPropertyLayout(GUIContent? label)
        {
            var bus = ValueEntry.SmartValue;
            var paths = FmodEditorUtils.GetBusPathsCached();
            var dropdownLabel = label ?? new GUIContent(Property.NiceName);

            if (paths.Count == 0)
            {
                var typed = EditorGUILayout.TextField(dropdownLabel, bus.Path);
                if (typed != bus.Path) ValueEntry.SmartValue = new FmodBus(typed);
                return;
            }

            var options = new List<string>(paths.Count + 1);
            options.AddRange(paths);
            var current = options.IndexOf(bus.Path);
            if (current < 0 && bus.IsValid)
            {
                options.Add(bus.Path + MissingSuffix);
                current = options.Count - 1;
            }

            var next = EditorGUILayout.Popup(dropdownLabel, Math.Max(0, current), options.ToArray());
            if (next == current) return;
            ValueEntry.SmartValue = new FmodBus(next < paths.Count ? paths[next] : bus.Path);
        }
    }
}
