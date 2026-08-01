using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UnityJigs.Components
{
    /// <summary>
    /// Extends this RectTransform's right edge so a TMP text fits inside it with padding.
    /// The authored anchor-span width is the minimum: X offsets stay zero until the text's
    /// preferred width + padding exceeds it, then offsetMax.x grows by the shortfall — the
    /// left edge never moves and the anchors are untouched, so the rect keeps scaling with
    /// its parent. Author the rect with zero X offsets. Pair with a centered text alignment
    /// to keep the text centered in the grown rect. Y is never driven.
    /// </summary>
    [ExecuteAlways, RequireComponent(typeof(RectTransform))]
    public class FitWidthToText : UIBehaviour, ILayoutSelfController
    {
        [Tooltip("Text to fit. Found in children when null (cached on first use).")]
        public TMP_Text? Text;
        [Tooltip("Gap kept on each side of the text once it overflows the minimum width.")]
        public float Padding = 9;

        private TMP_Text? _foundText;
        private bool _updating;
        private DrivenRectTransformTracker _drtTracker;
        private RectTransform RectTransform => (RectTransform)transform;

        protected override void OnEnable() => UpdateRectTransform();
        protected override void OnDisable() => _drtTracker.Clear();
        protected override void OnRectTransformDimensionsChange() => UpdateRectTransform();
        private void Update() => UpdateRectTransform();
        public void SetLayoutHorizontal() => UpdateRectTransform();
        public void SetLayoutVertical() { }

        private void UpdateRectTransform()
        {
            // Writing to the rect (or reading TMP preferredWidth) can re-fire
            // OnRectTransformDimensionsChange synchronously; with several rect-driving
            // components in one hierarchy that can cycle without converging. The guard caps
            // any such cycle at depth 1 — the per-frame poll reconverges next tick.
            if (_updating) return;
            if (!isActiveAndEnabled) return;
            _updating = true;
            try
            {
                var rt = RectTransform;
                var parent = rt.parent as RectTransform;
                if (parent == null) return;

                var text = Text;
                if (text == null)
                {
                    if (_foundText == null) _foundText = GetComponentInChildren<TMP_Text>(true);
                    text = _foundText;
                }
                if (text == null) return;

                var span = (rt.anchorMax.x - rt.anchorMin.x) * parent.rect.width;
                var needed = text.preferredWidth + Padding * 2;
                var extra = Mathf.Max(0, needed - span);

                _drtTracker.Clear();
                _drtTracker.Add(this, rt,
                    DrivenTransformProperties.AnchoredPositionX | DrivenTransformProperties.SizeDeltaX);

                if (Mathf.Approximately(rt.offsetMin.x, 0) && Mathf.Approximately(rt.offsetMax.x, extra)) return;
                rt.offsetMin = new Vector2(0, rt.offsetMin.y);
                rt.offsetMax = new Vector2(extra, rt.offsetMax.y);
            }
            finally
            {
                _updating = false;
            }
        }
    }
}
