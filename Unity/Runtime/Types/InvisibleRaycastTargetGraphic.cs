using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A UI Graphic that never renders any geometry but still receives UI raycasts.
/// Use this when you want a clickable/touchable area with ZERO overdraw.
/// </summary>
[DisallowMultipleComponent]
public class InvisibleRaycastTargetGraphic : MaskableGraphic
{
    // Ensure the component never tries to generate a mesh.
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear(); // no verts -> no draw calls / overdraw
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        // Extra safety: if anything tries to assign transparent verts, this still culls them.
        canvasRenderer.cullTransparentMesh = true;
        // Keep it fully transparent in the inspector for clarity.
        color = new Color(1f, 1f, 1f, 0f);
    }

    // Optional: skip unnecessary dirty passes (purely cosmetic perf win for this non-drawing Graphic)
    public override void SetMaterialDirty() { /* noop */ }
    public override void SetVerticesDirty() { /* noop */ }

    // Raycast behavior is inherited from MaskableGraphic: it uses the RectTransform bounds,
    // respects masks, and supports raycastTarget toggle & Raycast Padding.
}
