using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UnityJigs.Components
{
    /// <summary>
    /// Drives this RectTransform to cover its grandparent's rect — the same result as parenting
    /// directly under the grandparent with anchors (0,0)..(1,1) and zero offsets. Useful when an
    /// intermediate parent (layout group cell, animated wrapper) sits in between. Assumes no
    /// rotation between the parent and grandparent; a rect can't represent a rotated rect.
    /// </summary>
    [ExecuteAlways, RequireComponent(typeof(RectTransform))]
    public class MatchGrandparentRect : UIBehaviour, ILayoutSelfController
    {
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
            if (!isActiveAndEnabled) return;
            var rt = RectTransform;
            var parent = rt.parent as RectTransform;
            if (parent == null) return;
            var grandparent = parent.parent as RectTransform;
            if (grandparent == null) return;

            _drtTracker.Clear();
            _drtTracker.Add(this, rt,
                DrivenTransformProperties.Anchors |
                DrivenTransformProperties.AnchoredPosition |
                DrivenTransformProperties.SizeDelta);

            // Grandparent rect corners, expressed in the parent's local space.
            var gpRect = grandparent.rect;
            Vector2 min = parent.InverseTransformPoint(grandparent.TransformPoint(gpRect.min));
            Vector2 max = parent.InverseTransformPoint(grandparent.TransformPoint(gpRect.max));
            var size = max - min;

            // With anchors collapsed onto the parent's pivot, the anchor reference point is the
            // parent's local origin, so anchoredPosition is a plain parent-local offset.
            var pivot = parent.pivot;
            var anchoredPos = min + Vector2.Scale(size, rt.pivot);
            if (rt.anchorMin == pivot && rt.anchorMax == pivot &&
                rt.sizeDelta == size && rt.anchoredPosition == anchoredPos) return;

            rt.anchorMin = pivot;
            rt.anchorMax = pivot;
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;
        }
    }
}
