using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class LabelDrivenLayoutElement : UIBehaviour, ILayoutElement
{
    [SerializeField] private TMP_Text Label = null!;
    [SerializeField] private float PaddingX = 16f; // total left+right padding
    [SerializeField] private float PaddingY = 8f;  // total top+bottom padding
    [SerializeField] private float MinWidth = 0f;
    [SerializeField] private float MinHeight = 0f;
    [SerializeField] private int LayoutPriority = 1;

    private float _minWidth;
    private float _preferredWidth;
    private float _flexibleWidth;
    private float _minHeight;
    private float _preferredHeight;
    private float _flexibleHeight;

    private bool _labelCallbackRegistered;

    public float minWidth => _minWidth;
    public float preferredWidth => _preferredWidth;
    public float flexibleWidth => _flexibleWidth;
    public float minHeight => _minHeight;
    public float preferredHeight => _preferredHeight;
    public float flexibleHeight => _flexibleHeight;
    public int layoutPriority => LayoutPriority;

    // ---- Lifecycle / reactivity ---------------------------------------------

    protected override void OnEnable()
    {
        base.OnEnable();
        RegisterLabelCallback();
        SetDirty();
    }

    protected override void OnDisable()
    {
        UnregisterLabelCallback();
        SetDirty();
        base.OnDisable();
    }

    protected override void OnRectTransformDimensionsChange()
    {
        base.OnRectTransformDimensionsChange();
        SetDirty();
    }

    protected override void OnTransformParentChanged()
    {
        base.OnTransformParentChanged();
        SetDirty();
    }

    protected override void OnDidApplyAnimationProperties()
    {
        base.OnDidApplyAnimationProperties();
        SetDirty();
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        UnregisterLabelCallback();
        RegisterLabelCallback();
        SetDirty();
    }
#endif

    // Called by TMP / Graphic when its layout is dirtied (text, font size, etc.).
    private void OnLabelLayoutDirty()
    {
        SetDirty();
    }

    private void RegisterLabelCallback()
    {
        if (_labelCallbackRegistered)
            return;

        if (!Label)
            return;

        if (Label is Graphic graphic)
        {
            graphic.RegisterDirtyLayoutCallback(OnLabelLayoutDirty);
            _labelCallbackRegistered = true;
        }
    }

    private void UnregisterLabelCallback()
    {
        if (!_labelCallbackRegistered)
            return;

        if (Label && Label is Graphic graphic)
            graphic.UnregisterDirtyLayoutCallback(OnLabelLayoutDirty);

        _labelCallbackRegistered = false;
    }

    // ---- ILayoutElement ------------------------------------------------------

    public void CalculateLayoutInputHorizontal()
    {
        UpdateLayoutValues();
    }

    public void CalculateLayoutInputVertical()
    {
        UpdateLayoutValues();
    }

    private void UpdateLayoutValues()
    {
        if (!isActiveAndEnabled)
        {
            _minWidth = 0f;
            _preferredWidth = 0f;
            _flexibleWidth = 0f;
            _minHeight = 0f;
            _preferredHeight = 0f;
            _flexibleHeight = 0f;
            return;
        }

        if (!Label)
        {
            // Fall back to minimums if label is missing
            var fallbackWidth = Mathf.Max(MinWidth, 0f);
            var fallbackHeight = Mathf.Max(MinHeight, 0f);

            _minWidth = fallbackWidth;
            _preferredWidth = fallbackWidth;
            _flexibleWidth = 0f;

            _minHeight = fallbackHeight;
            _preferredHeight = fallbackHeight;
            _flexibleHeight = 0f;
            return;
        }

        // TMP's preferred size for current content + settings
        var textPreferredWidth = Label.preferredWidth;
        var textPreferredHeight = Label.preferredHeight;

        var width = Mathf.Max(textPreferredWidth + PaddingX, MinWidth);
        var height = Mathf.Max(textPreferredHeight + PaddingY, MinHeight);

        _minWidth = width;
        _preferredWidth = width;
        _flexibleWidth = 0f;

        _minHeight = height;
        _preferredHeight = height;
        _flexibleHeight = 0f;
    }

    private void SetDirty()
    {
        if (!IsActive())
            return;

        if (transform is not RectTransform rt)
            return;

        LayoutRebuilder.MarkLayoutForRebuild(rt);
    }
}
