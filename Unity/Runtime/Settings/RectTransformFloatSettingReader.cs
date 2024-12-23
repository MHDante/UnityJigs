using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace UnityJigs.Settings
{
    [ExecuteAlways, RequireComponent(typeof(RectTransform))]
    public class RectTransformFloatSettingReader : MonoBehaviour
    {
        public enum SyncTargets
        {
            None,
            Width,
            Height,
            Top,
            Bottom,
            Left,
            Right,
        }

        [Serializable]
        public struct Entry
        {
            [TableColumnWidth(5)] public bool Negate;
            [Required, InlineEditor(Expanded = true)] public FloatReference Source;
        }

        [HideInInspector] public bool Negate = false;
        [HideInInspector] public FloatReference? Source = null;

        [TableList]
        public List<Entry> Entries = new();

        public SyncTargets TargetDimension;

        private RectTransform? _rt;
        public RectTransform RectTransform => _rt != null ? _rt : _rt = GetComponent<RectTransform>();

        private void Update()
        {
            if (Source == null) return;
            var value = 0f;

            foreach (var entry in Entries)
            {
                var source = entry.Source;
                if(!source) continue;
                value += entry.Negate ? -source.Value : source.Value;
            }
            SetTargetValue(value);
        }

        private void OnValidate()
        {
            if (Entries.Count < 1) Entries.Add(new() { Negate = Negate, Source = Source! });
        }

        private void SetTargetValue(float value)
        {
            switch (TargetDimension)
            {
                case SyncTargets.None:
                    return;
                case SyncTargets.Width:
                    RectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, value);
                    break;
                case SyncTargets.Height:
                    RectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, value);
                    break;
                case SyncTargets.Top:
                    RectTransform.offsetMax = RectTransform.offsetMax with { y = -value };
                    break;
                case SyncTargets.Bottom:
                    RectTransform.offsetMin = RectTransform.offsetMin with { y = value };
                    break;
                case SyncTargets.Left:
                    RectTransform.offsetMin = RectTransform.offsetMin with { x = value };
                    break;
                case SyncTargets.Right:
                    RectTransform.offsetMax = RectTransform.offsetMax with { x = -value };
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(TargetDimension), TargetDimension, null);
            }
        }
    }
}
