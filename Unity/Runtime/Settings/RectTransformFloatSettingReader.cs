using System;
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

        public bool Negate = false;
        [InlineEditor(Expanded = true)]
        public FloatReference? Source = null;
        public SyncTargets TargetDimension;

        private RectTransform? _rt;
        public RectTransform RectTransform => _rt != null ? _rt : _rt = GetComponent<RectTransform>();

        private void Update()
        {
            if (Source == null) return;
            var value = Source.Value;
            SetTargetValue(Negate ? -value : value);
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
