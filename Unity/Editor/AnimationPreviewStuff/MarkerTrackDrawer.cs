using UnityEditor;
using UnityEngine;

namespace UnityJigs.Editor
{
    /// <summary>
    /// Generic, reusable track UI for editing normalized-time markers.
    /// Does not own or serialize marker data; uses an <see cref="IMarkerTrackSource"/> instead.
    /// </summary>
    public sealed class MarkerTrackDrawer
    {
        private readonly IMarkerTrackSource _source;

        public const float DefaultHeight = 20f;
        private const float MarkerSize = 8f;
        private const float ButtonSize = 20f;
        private const float MarkerHitSize = 12f;

        public int? SelectedMarkerIndex;
        public bool AllowDeselect;

        private int? _draggedIndex;
        private bool _isDragging;
        private Vector2 _dragStartPos;
        private float _dragStartTime;

        public int? DraggedMarkerIndex => _draggedIndex;

        public MarkerTrackDrawer(IMarkerTrackSource source) => _source = source;

        // --------------------------------------------------------------------

        public void DrawLayout(AnimationClip? currentClip)
        {
            var rect = EditorGUILayout.GetControlRect(false, DefaultHeight);
            Draw(rect, currentClip);
        }

        public void Draw(Rect controlRect, AnimationClip? currentClip)
        {
            // Derive timeline rect from AnimationPreviewDrawerUtil
            var fakePreviewRect = controlRect;
            fakePreviewRect.height = 300f; // only x/width matter
            var timelineRect = AnimationPreviewDrawerUtil.GetTimelineRect(fakePreviewRect);

            // Our track sits directly above the timeline (touching)
            var trackRect = controlRect;
            trackRect.xMin = timelineRect.xMin;
            trackRect.xMax = timelineRect.xMax;

            // Draw baseline
            EditorGUI.DrawRect(trackRect, new Color(0, 0, 0, 0.1f));

            // Draw and handle markers
            HandleMarkerEvents(trackRect);
            DrawMarkers(trackRect);

            // Button region to the right of the timeline
            var buttonRegion = new Rect(timelineRect.xMax + 4f, trackRect.y, ButtonSize * 2f, trackRect.height);
            DrawButtons(buttonRegion);
        }

        // --------------------------------------------------------------------

        private void DrawMarkers(Rect trackRect)
        {
            int count = _source.GetMarkerCount();
            if (count == 0) return;

            Handles.BeginGUI();

            for (int i = 0; i < count; i++)
            {
                float t = _source.GetMarkerTime(i);
                float x = Mathf.Lerp(trackRect.xMin, trackRect.xMax, t);
                var center = new Vector2(x, trackRect.center.y);
                var color = (i == SelectedMarkerIndex ? Color.yellow : Color.cyan);

                Handles.color = color;
                Handles.DrawSolidDisc(center, Vector3.forward, MarkerSize * 0.5f);
                Handles.DrawLine(new Vector3(x, trackRect.yMin + 2f), new Vector3(x, trackRect.yMax - 2f));

                var label = _source.GetMarkerLabel(i);
                var labelRect = new Rect(x + 4f, trackRect.yMin, 120f, trackRect.height);
                GUI.Label(labelRect, string.IsNullOrEmpty(label) ? $"{t:F2}" : label);
            }

            Handles.EndGUI();
        }

        private void HandleMarkerEvents(Rect trackRect)
        {
            var e = Event.current;
            var mousePos = e.mousePosition;
            int count = _source.GetMarkerCount();

            if (e.type == EventType.MouseDown && e.button == 0 && trackRect.Contains(mousePos))
            {
                for (int i = 0; i < count; i++)
                {
                    float x = Mathf.Lerp(trackRect.xMin, trackRect.xMax, _source.GetMarkerTime(i));
                    if (Mathf.Abs(mousePos.x - x) <= MarkerHitSize)
                    {
                        GUI.FocusControl(null);
                        SelectedMarkerIndex = i;
                        _draggedIndex = i;
                        _isDragging = true;
                        _dragStartPos = mousePos;
                        _dragStartTime = _source.GetMarkerTime(i);
                        e.Use();
                        return;
                    }
                }

                if (AllowDeselect)
                {
                    // Clicked empty space: deselect
                    SelectedMarkerIndex = null;
                    _draggedIndex = null;
                    GUI.changed = true;
                    e.Use();
                }
            }

            if (_isDragging && _draggedIndex.HasValue)
            {
                int index = _draggedIndex.Value;

                if (e.type == EventType.MouseDrag)
                {
                    float delta = (mousePos.x - _dragStartPos.x) / trackRect.width;
                    float newTime = Mathf.Clamp01(_dragStartTime + delta);

                    _source.UpdateMarkerTime(index, newTime);
                    GUI.changed = true;
                    e.Use();
                }
                else if (e.type == EventType.MouseUp)
                {
                    _isDragging = false;
                    _draggedIndex = null;
                    e.Use();
                }
            }
        }

        private void DrawButtons(Rect rect)
        {
            var addRect = new Rect(rect.x, rect.y, ButtonSize, rect.height);
            var removeRect = new Rect(rect.x + ButtonSize, rect.y, ButtonSize, rect.height);

            if (GUI.Button(addRect, "+"))
            {
                float time = Mathf.Clamp01(_source.GetSuggestedNewMarkerTime());
                _source.AddMarker(time);
                SelectedMarkerIndex = _source.GetMarkerCount() - 1;
                GUI.changed = true;
            }

            GUI.enabled = SelectedMarkerIndex.HasValue;
            if (GUI.Button(removeRect, "-") && SelectedMarkerIndex.HasValue)
            {
                _source.RemoveMarker(SelectedMarkerIndex.Value);
                SelectedMarkerIndex = null;
                GUI.changed = true;
            }
            GUI.enabled = true;
        }
    }
}
