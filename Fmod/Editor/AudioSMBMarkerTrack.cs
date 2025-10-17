using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityJigs.Editor;

namespace UnityJigs.Fmod.Editor
{
    /// <summary>
    /// Draws and manages the interactive marker track above the AnimationPreviewDrawerUtil timeline.
    /// </summary>
    public sealed class AudioSMBMarkerTrack
    {
        private readonly AudioSMBEditor _editor;

        public const float DefaultHeight = 20f;
        private const float MarkerSize = 8f;
        private const float ButtonSize = 20f;
        private const float MarkerHitSize = 12f;

        private int? _selectedIndex;
        private bool _isDragging;
        private Vector2 _dragStartPos;
        private float _dragStartTime;

        public AudioSMBMarkerTrack(AudioSMBEditor editor) => _editor = editor;

        // --------------------------------------------------------------------

        public void DrawLayout(AnimationClip? currentClip)
        {
            var rect = EditorGUILayout.GetControlRect(false, DefaultHeight);
            Draw(rect, currentClip);
        }

        public void Draw(Rect controlRect, AnimationClip? currentClip)
        {
            var smb = _editor.AudioSMB;
            var events = smb.Events;

            // Derive timeline rect from util
            var fakePreviewRect = controlRect;
            fakePreviewRect.height = 300f; // arbitrary; only x/width matter
            var timelineRect = AnimationPreviewDrawerUtil.GetTimelineRect(fakePreviewRect);

            // Our track sits directly above the timeline (not floating up)
            var trackRect = controlRect;
            trackRect.xMin = timelineRect.xMin;
            trackRect.xMax = timelineRect.xMax;

            // Draw baseline
            EditorGUI.DrawRect(trackRect, new Color(0, 0, 0, 0.1f));

            // Draw and interact with markers
            HandleMarkerEvents(trackRect, events);
            DrawMarkers(trackRect, events);

            // Button region to the right of the timeline
            var buttonRegion = new Rect(timelineRect.xMax + 4f, trackRect.y, ButtonSize * 2f, trackRect.height);
            DrawButtons(buttonRegion, events);
        }

        // --------------------------------------------------------------------
        // Marker drawing and interaction
        // --------------------------------------------------------------------

        private void DrawMarkers(Rect trackRect, List<AudioSMBEvent> events)
        {
            if (events.Count == 0)
                return;

            Handles.BeginGUI();

            for (int i = 0; i < events.Count; i++)
            {
                var evt = events[i];
                float x = Mathf.Lerp(trackRect.xMin, trackRect.xMax, evt.NormalizedTime);
                var center = new Vector2(x, trackRect.center.y);
                var color = (i == _selectedIndex ? Color.yellow : Color.cyan);
                var size = MarkerSize;

                Handles.color = color;
                Handles.DrawSolidDisc(center, Vector3.forward, size * 0.5f);

                // Optional line to baseline
                Handles.DrawLine(new Vector3(x, trackRect.yMin + 2f), new Vector3(x, trackRect.yMax - 2f));

                var labelRect = new Rect(x + 4f, trackRect.yMin, 80f, trackRect.height);
                GUI.Label(labelRect, $"{evt.NormalizedTime:F2}");
            }

            Handles.EndGUI();
        }

        private void HandleMarkerEvents(Rect trackRect, List<AudioSMBEvent> events)
        {
            var e = Event.current;
            var mousePos = e.mousePosition;

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                // Hit-test existing markers only (no deselection on empty space)
                for (int i = 0; i < events.Count; i++)
                {
                    float x = Mathf.Lerp(trackRect.xMin, trackRect.xMax, events[i].NormalizedTime);
                    if (Mathf.Abs(mousePos.x - x) <= MarkerHitSize && trackRect.Contains(mousePos))
                    {
                        GUI.FocusControl(null);
                        _selectedIndex = i;
                        _isDragging = true;
                        _dragStartPos = mousePos;
                        _dragStartTime = events[i].NormalizedTime;
                        e.Use();
                        return;
                    }
                }
            }

            if (_isDragging && _selectedIndex.HasValue)
            {
                int index = _selectedIndex.Value;
                if (e.type == EventType.MouseDrag)
                {
                    float delta = (mousePos.x - _dragStartPos.x) / trackRect.width;
                    events[index].NormalizedTime = Mathf.Clamp01(_dragStartTime + delta);
                    GUI.changed = true;
                    e.Use();
                }
                else if (e.type == EventType.MouseUp)
                {
                    _isDragging = false;
                    e.Use();
                }
            }
        }

        // --------------------------------------------------------------------

        private void DrawButtons(Rect rect, List<AudioSMBEvent> events)
        {
            var addRect = new Rect(rect.x, rect.y, ButtonSize, rect.height);
            var removeRect = new Rect(rect.x + ButtonSize, rect.y, ButtonSize, rect.height);

            if (GUI.Button(addRect, "+"))
            {
                float time = Mathf.Clamp01(_editor.ClipDrawer.NormalizedTime);

                events.Add(new AudioSMBEvent
                {
                    NormalizedTime = time,
                    AudioEvent = new FMODUnity.EventReference()
                });
                _selectedIndex = events.Count - 1;
                GUI.changed = true;
            }

            GUI.enabled = _selectedIndex.HasValue;
            if (GUI.Button(removeRect, "-") && _selectedIndex.HasValue)
            {
                events.RemoveAt(_selectedIndex.Value);
                _selectedIndex = null;
                GUI.changed = true;
            }
            GUI.enabled = true;
        }
    }
}
