using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityJigs.Extensions;

namespace UnityJigs.Components
{
    public class PreferredWidth : UIBehaviour, ILayoutSelfController
    {
        public float Width;
        public float Margin;
        RectTransform RectTransform => (RectTransform)transform;

        protected override void OnRectTransformDimensionsChange() => UpdateRectTransform();
        private void Update() => UpdateRectTransform();
        public void SetLayoutHorizontal() => UpdateRectTransform();
        private DrivenRectTransformTracker _drtTracker;

        protected override void OnDisable() => _drtTracker.Clear();
        protected override void OnEnable() => UpdateRectTransform();


#if UNITY_EDITOR
        protected override void Reset()
        {
            base.Reset();
            var parentRt = (RectTransform)RectTransform.parent;
            var parentWidth = parentRt.rect.width;
            Width = RectTransform.rect.width;
            Margin = parentWidth - Width;
        }
#endif


        private void UpdateRectTransform()
        {
            if (!isActiveAndEnabled) return;
            var rt = (RectTransform)transform;
            var parentRt = (RectTransform)rt.parent;
            var parentWidth = parentRt.rect.width;
            var maxSize = parentWidth - Margin;
            var size = Mathf.Min(maxSize, Width);
            _drtTracker.Add(this, RectTransform, DrivenTransformProperties.SizeDeltaX);
            if (size < 0) return;
            if (Mathf.Approximately(rt.sizeDelta.x, size)) return;
            rt.sizeDelta = rt.sizeDelta.WithX(size);
        }

        public void SetLayoutVertical() { }
    }
}
