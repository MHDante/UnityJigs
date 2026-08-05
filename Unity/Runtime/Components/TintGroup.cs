using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace UnityJigs.Components
{
    /// <summary>
    /// CanvasGroup, but for tint: multiplies a colour over every descendant Graphic via
    /// CanvasRenderer.SetColor — the render-time multiply channel CrossFadeColor uses — so it
    /// composes with Graphic.color owners (code or animation clips) without fighting them.
    /// Nested groups multiply like nested CanvasGroup alphas: each Graphic is written only by
    /// its nearest ancestor group, using the product of all ancestor tints. Applied every frame
    /// (CanvasRenderer colour resets when a graphic re-enables, so polling is also the fix).
    /// </summary>
    [ExecuteAlways]
    public class TintGroup : MonoBehaviour
    {
        public Color Tint = Color.white;

        private readonly List<Graphic> _graphics = new();

        // On disable, restore graphics that no longer have any enabled group; an
        // ancestor group (if present) re-tints its subtree on its next poll.
        private void OnDisable()
        {
            GetComponentsInChildren(true, _graphics);
            foreach (var graphic in _graphics)
                if (NearestGroup(graphic.transform) == null)
                    graphic.canvasRenderer.SetColor(Color.white);
        }

        private void Update() => Apply(EffectiveTint());

        private Color EffectiveTint()
        {
            var tint = Tint;
            for (var t = transform.parent; t != null; t = t.parent)
                if (t.TryGetComponent(out TintGroup ancestor) && ancestor.isActiveAndEnabled)
                    tint *= ancestor.Tint;
            return tint;
        }

        private void Apply(Color tint)
        {
            GetComponentsInChildren(true, _graphics);
            foreach (var graphic in _graphics)
            {
                if (NearestGroup(graphic.transform) != this) continue;
                graphic.canvasRenderer.SetColor(tint);
            }
        }

        private static TintGroup? NearestGroup(Transform from)
        {
            for (var t = from; t != null; t = t.parent)
                if (t.TryGetComponent(out TintGroup group) && group.isActiveAndEnabled)
                    return group;
            return null;
        }
    }
}
