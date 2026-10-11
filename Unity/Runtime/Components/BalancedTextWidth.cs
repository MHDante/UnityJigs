using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UnityJigs.Components
{
    /// <summary>
    /// Caps a TMP text's preferred width and, when it has to wrap, balances its lines: reports the
    /// narrowest width that still fits the text in as many lines as the cap needs, so a line just
    /// over the cap wraps as two halves rather than a full line and an orphaned word (CSS
    /// <c>text-wrap: balance</c>). Text that fits under the cap reports its natural width.
    /// <para>Sits on the text's GameObject as a layout element above TMP's own (priority 1), so a
    /// parent layout group / content size fitter that controls the child's size hugs the result.
    /// The text must wrap (Text Wrapping Mode: Normal). The cap is <see cref="MaxWidth"/>, and also
    /// <see cref="MaxWidthFrom"/>'s width minus <see cref="Margin"/> when set (e.g. a safe area, with
    /// the margin covering the container's padding); polled every frame, so a resize re-lays out.</para>
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(RectTransform))]
    public class BalancedTextWidth : UIBehaviour, ILayoutElement
    {
        [Tooltip("The text to measure. Found on this GameObject when empty.")]
        public TMP_Text? Text;

        [Min(0), Tooltip("Widest the text may be. 0: no fixed cap (only Max Width From).")]
        public float MaxWidth = 700;

        [Tooltip("Optional: the text is also never wider than this rect's width minus Margin.")]
        public RectTransform? MaxWidthFrom;

        [Tooltip("Subtracted from Max Width From's width (e.g. the container's horizontal padding).")]
        public float Margin;

        [Tooltip("When the text wraps, even out its lines instead of filling each line to the cap.")]
        public bool Balance = true;

        [Tooltip("Extra width on top of the measured one, so rounding can't push a word onto a new line.")]
        public float Slack = 1;

        // Binary search stops when the bracket is this narrow (px).
        private const float Precision = 1;

        private float _preferredWidth = -1;
        private float _preferredHeight = -1;

        // What the last result was computed from.
        private string? _lastText;
        private float _lastCap = -1;
        private float _lastFontSize = -1;
        private bool _lastBalance;

        private TMP_Text? Target => Text != null ? Text : Text = GetComponent<TMP_Text>();

        public float minWidth => -1;
        public float preferredWidth => _preferredWidth;
        public float flexibleWidth => -1;
        public float minHeight => -1;
        public float preferredHeight => _preferredHeight;
        public float flexibleHeight => -1;
        public int layoutPriority => 1;

        /// <summary>The width the text may not exceed right now (infinity when nothing caps it).</summary>
        public float Cap
        {
            get
            {
                var cap = MaxWidth > 0 ? MaxWidth : float.PositiveInfinity;
                if (MaxWidthFrom != null) cap = Mathf.Min(cap, MaxWidthFrom.rect.width - Margin);
                return Mathf.Max(0, cap);
            }
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            SetDirty();
        }

        protected override void OnDisable()
        {
            SetDirty();
            base.OnDisable();
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            _lastText = null;
            SetDirty();
        }
#endif

        // The cap can move without this rect changing (a safe area resizing with the screen).
        private void Update()
        {
            if (!Mathf.Approximately(Cap, _lastCap)) SetDirty();
        }

        public void CalculateLayoutInputHorizontal() => Measure();
        public void CalculateLayoutInputVertical() => Measure();

        private void Measure()
        {
            var text = Target;
            if (!isActiveAndEnabled || text == null)
            {
                _preferredWidth = _preferredHeight = -1;
                return;
            }

            var cap = Cap;
            var content = text.text ?? "";
            if (content == _lastText && Mathf.Approximately(cap, _lastCap) &&
                Mathf.Approximately(text.fontSize, _lastFontSize) && Balance == _lastBalance)
                return;
            (_lastText, _lastCap, _lastFontSize, _lastBalance) = (content, cap, text.fontSize, Balance);

            var natural = text.GetPreferredValues(content, float.PositiveInfinity, float.PositiveInfinity);
            if (natural.x <= cap)
            {
                (_preferredWidth, _preferredHeight) = (natural.x + Slack, natural.y);
                return;
            }

            // Wrapped at the cap: that many lines is the target. Fewer lines can't fit (greedy
            // wrapping only loses lines as it widens), so the narrowest width keeping the same
            // height spreads the words evenly over them.
            var height = Height(text, content, cap);
            var width = cap;
            if (Balance)
            {
                var lo = 0f;
                while (width - lo > Precision)
                {
                    var mid = (lo + width) * 0.5f;
                    if (Height(text, content, mid) <= height + 0.01f) width = mid;
                    else lo = mid;
                }
            }

            _preferredWidth = Mathf.Min(width + Slack, cap);
            _preferredHeight = height;
        }

        private static float Height(TMP_Text text, string content, float width) =>
            text.GetPreferredValues(content, width, float.PositiveInfinity).y;

        private void SetDirty()
        {
            if (!IsActive()) return;
            LayoutRebuilder.MarkLayoutForRebuild((RectTransform)transform);
        }
    }
}
