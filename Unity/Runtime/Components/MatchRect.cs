using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UnityJigs.Components
{
    /// <summary>
    /// Drives this RectTransform to cover a target RectTransform's rect (the grandparent when
    /// Target is null) — the same result as parenting directly under the target with anchors
    /// (0,0)..(1,1) and zero offsets. Useful when an intermediate parent (layout group cell,
    /// animated wrapper) sits in between, or to track a rect on another hierarchy branch.
    /// Assumes no rotation between the parent and the target; a rect can't represent a
    /// rotated rect. Non-ancestor targets that move in the same frame can lag by one frame.
    /// </summary>
    [ExecuteAlways, RequireComponent(typeof(RectTransform))]
    public class MatchRect : UIBehaviour, ILayoutSelfController
    {
        [Tooltip("RectTransform to match. Leave null to match the grandparent.")]
        public RectTransform? Target;

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
            // Writing to the rect re-fires OnRectTransformDimensionsChange synchronously; with
            // several rect-driving components in one hierarchy that can cycle without converging.
            // The guard caps any such cycle at depth 1 — the per-frame poll reconverges next tick.
            if (_updating) return;
            if (!isActiveAndEnabled) return;
            var rt = RectTransform;
            var parent = rt.parent as RectTransform;
            if (parent == null) return;
            var target = Target;
            if (target == null) target = parent.parent as RectTransform;
            if (target == null) return;

            _drtTracker.Clear();
            _drtTracker.Add(this, rt,
                DrivenTransformProperties.Anchors |
                DrivenTransformProperties.AnchoredPosition |
                DrivenTransformProperties.SizeDelta);

            // Target rect corners, expressed in the parent's local space.
            var targetRect = target.rect;
            Vector2 min = parent.InverseTransformPoint(target.TransformPoint(targetRect.min));
            Vector2 max = parent.InverseTransformPoint(target.TransformPoint(targetRect.max));
            var size = max - min;

            // With anchors collapsed onto the parent's pivot, the anchor reference point is the
            // parent's local origin, so anchoredPosition is a plain parent-local offset.
            var pivot = parent.pivot;
            var anchoredPos = min + Vector2.Scale(size, rt.pivot);
            if (rt.anchorMin == pivot && rt.anchorMax == pivot &&
                rt.sizeDelta == size && rt.anchoredPosition == anchoredPos) return;

            _updating = true;
            try
            {
                rt.anchorMin = pivot;
                rt.anchorMax = pivot;
                rt.sizeDelta = size;
                rt.anchoredPosition = anchoredPos;
            }
            finally
            {
                _updating = false;
            }
        }
    }
}
