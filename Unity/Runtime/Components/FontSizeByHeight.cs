using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace UnityJigs.Components
{
    /// <summary>
    /// Drives a TMP text's font size proportionally to its own rect height, so text scales
    /// with an anchor-scaled rig instead of staying at a fixed point size. Reset captures the
    /// current fontSize/height ratio, preserving the authored look. Composes with
    /// FitWidthToText one-directionally: height → font size → preferred width → panel width.
    /// Don't combine with TMP Auto Size.
    /// </summary>
    [ExecuteAlways, RequireComponent(typeof(TMP_Text))]
    public class FontSizeByHeight : UIBehaviour
    {
        [Tooltip("Font size per unit of rect height. Reset captures the current ratio.")]
        public float Ratio = 0.5f;

        private TMP_Text? _text;
        private bool _updating;
        private TMP_Text Text => _text != null ? _text : _text = GetComponent<TMP_Text>();

        protected override void OnEnable() => UpdateFontSize();
        protected override void OnRectTransformDimensionsChange() => UpdateFontSize();
        private void Update() => UpdateFontSize();

#if UNITY_EDITOR
        protected override void Reset()
        {
            base.Reset();
            var height = ((RectTransform)transform).rect.height;
            if (height > 0) Ratio = Text.fontSize / height;
        }
#endif

        private void UpdateFontSize()
        {
            // Same reentrancy cap as the other rect-driving jigs: font size changes make TMP
            // regenerate, which can synchronously re-enter layout callbacks.
            if (_updating) return;
            if (!isActiveAndEnabled) return;
            var height = ((RectTransform)transform).rect.height;
            if (height <= 0) return;
            var size = height * Ratio;
            if (Mathf.Approximately(Text.fontSize, size)) return;
            _updating = true;
            try { Text.fontSize = size; }
            finally { _updating = false; }
        }
    }
}
