using UnityEditor;
using UnityEngine;

namespace UnityJigs.Editor.Utilities
{
    public static class RectTransformMenuItems
    {
        private const string BakeName = "Bake Position Into Anchors";

        [MenuItem("CONTEXT/RectTransform/" + BakeName)]
        private static void BakeContext(MenuCommand cmd) => Bake((RectTransform)cmd.context);

        [MenuItem("Utils/RectTransform/" + BakeName)]
        public static void BakeSelection()
        {
            var any = false;
            foreach (var t in Selection.transforms)
            {
                if (t is not RectTransform rt) continue;
                Bake(rt);
                any = true;
            }
            if (!any) Debug.LogError("No RectTransform selected.");
        }

        /// <summary>
        /// Rewrites the anchors so they sit exactly at the rect's current corners, leaving
        /// left/right/top/bottom offsets at 0. The rect itself does not move.
        /// </summary>
        private static void Bake(RectTransform rt)
        {
            var parent = rt.parent as RectTransform;
            if (parent == null)
            {
                Debug.LogError($"{rt.name}: no RectTransform parent to anchor against.", rt);
                return;
            }

            var pRect = parent.rect;
            if (pRect.width <= 0 || pRect.height <= 0)
            {
                Debug.LogError($"{rt.name}: parent rect has zero size; can't normalize anchors.", rt);
                return;
            }

            // Rect corners in parent space, straight from the anchor data. Exact regardless of
            // the child's own scale/rotation — those apply after the rect is placed.
            var refMin = pRect.min + Vector2.Scale(pRect.size, rt.anchorMin);
            var refMax = pRect.min + Vector2.Scale(pRect.size, rt.anchorMax);
            var cornerMin = refMin + rt.offsetMin;
            var cornerMax = refMax + rt.offsetMax;

            Undo.RecordObject(rt, BakeName);
            rt.anchorMin = (cornerMin - pRect.min) / pRect.size;
            rt.anchorMax = (cornerMax - pRect.min) / pRect.size;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            PrefabUtility.RecordPrefabInstancePropertyModifications(rt);
        }
    }
}
