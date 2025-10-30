using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FMODUnity;
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities.Editor;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityJigs.Editor;

namespace UnityJigs.Fmod.Editor
{
    [CustomEditor(typeof(AudioSMB), true)]
    public class AudioSMBEditor : OdinEditor, IMarkerTrackSource
    {

        static AudioSMBEditor() => AudioSMB.OnEditorPlay = FmodEditorUtils.PlayEditorSound;

        public AudioSMB AudioSMB => (AudioSMB)target;
        private readonly List<AnimationClip> _clips = new();

        public AnimationPreviewDrawerUtil ClipDrawer = null!;
        private MarkerTrackDrawer _markerTrack = null!;

        public AnimationClip? Clip
        {
            get => ClipDrawer.Clip;
            set => ClipDrawer.Clip = value;
        }

        private int? _selectedMarker;
        private bool _wasPlaying;

        // --------------------------------------------------------------------

        protected override void OnEnable()
        {
            base.OnEnable();
            ClipDrawer = new AnimationPreviewDrawerUtil();
            _markerTrack = new MarkerTrackDrawer(this);
            RefreshContext();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (ClipDrawer != null!) ClipDrawer.Dispose();
            ClipDrawer = null!;
        }

        // --------------------------------------------------------------------

        public override void OnInspectorGUI()
        {
            using var _ = new EditorGUI.IndentLevelScope();

            // Hide the Events list from the main inspector
            var eventsProp = Tree.GetPropertyAtPath(nameof(AudioSMB.Events));
            if (eventsProp != null)
                eventsProp.State.Visible = false;

            base.OnInspectorGUI();

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Animation Context", EditorStyles.boldLabel);

            if (_clips.Count == 0)
            {
                EditorGUILayout.HelpBox("No animation context found.", MessageType.Info);
                if (GUILayout.Button("Refresh")) RefreshContext();
                return;
            }

            ClipDrawer.ShowIKOnFeetButton = true;

            DrawPreviewArea();

            if (GUILayout.Button("Refresh"))
                RefreshContext();

            HandlePreviewPlayback();
        }

        // ---------------------------------------------------------------
        // Context + Preview logic
        // ---------------------------------------------------------------

        private void RefreshContext()
        {
            if(target is not Fmod.AudioSMB) return;
            _clips.Clear();
            ClipDrawer.Clip = null;

            var contexts = AnimatorController.FindStateMachineBehaviourContext(AudioSMB);
            if (contexts == null || contexts.Length == 0)
                return;

            foreach (var ctx in contexts)
            {
                if (ctx.animatorController == null) continue;
                if (ctx.animatorObject is not AnimatorState state) continue;
                if (state.motion == null) continue;

                CollectClipsFromMotion(state.motion);
            }

            if (ClipDrawer.Clip == null && _clips.Count > 0)
                ClipDrawer.Clip = _clips[0];
        }

        private void CollectClipsFromMotion(Motion? motion)
        {
            switch (motion)
            {
                case AnimationClip clip when !_clips.Contains(clip):
                    _clips.Add(clip);
                    break;

                case BlendTree blendTree:
                    foreach (var child in blendTree.children)
                        CollectClipsFromMotion(child.motion);
                    break;
            }
        }


        private void DrawPreviewArea()
        {
            if (ClipDrawer.Clip == null)
                return;

            if (!ClipDrawer.HasPreviewGUI)
                return;

            // Ensure at least one marker and selection
            if (AudioSMB.Events.Count == 0)
                AudioSMB.Events.Add(new AudioSMBEvent { NormalizedTime = 0f });

            if (!_markerTrack.SelectedMarkerIndex.HasValue ||
                _markerTrack.SelectedMarkerIndex.Value < 0 ||
                _markerTrack.SelectedMarkerIndex.Value >= AudioSMB.Events.Count)
            {
                _markerTrack.SelectedMarkerIndex = 0;
            }

            _selectedMarker = _markerTrack.SelectedMarkerIndex!.Value;

            // --- Details panel above timeline ---
            if (_selectedMarker is int idx and >= 0 && idx < AudioSMB.Events.Count)
            {
                var path = $"{nameof(AudioSMB.Events)}.${idx}";
                var prop = Tree.GetPropertyAtPath(path);
                if (prop != null)
                {
                    SirenixEditorGUI.BeginBox("Selected Event");

                    // Draw only the AudioEvent field
                    var audioEventProp = prop.Children["AudioEvent"];
                    audioEventProp?.Draw();


                    SirenixEditorGUI.EndBox();
                }
            }

            // --- Marker track (above preview timeline) ---
            _markerTrack.DrawLayout(ClipDrawer.Clip);

            // --- Preview area ---
            const float previewHeight = 180f;
            var rect = EditorGUILayout.GetControlRect(false, previewHeight);
            ClipDrawer.Draw(rect);

            // Draw the clip inclusion table
            DrawClipInclusionTable();
        }

        private void DrawClipInclusionTable()
        {
            if (_clips.Count == 0)
                return;

            SirenixEditorGUI.BeginBox("Clip Inclusion");
            EditorGUILayout.LabelField("Include Clips:", EditorStyles.miniBoldLabel);

            // Calculate preview highlight color
            var highlightColor = new Color(0.25f, 0.55f, 1f, 0.15f);

            AudioSMBEvent? evt = null;

            // --- Details panel above timeline ---
            if (_selectedMarker is int idx and >= 0 && idx < AudioSMB.Events.Count)
            {
                evt = AudioSMB.Events[idx];
            }

            foreach (var clip in _clips)
            {
                bool excluded = evt?.ExcludedClips != null && evt.ExcludedClips.Contains(clip);
                bool include = !excluded;
                bool isPreview = (ClipDrawer.Clip == clip);

                // Background tint for current preview clip
                var rect = EditorGUILayout.BeginHorizontal();
                if (isPreview)
                {
                    var c = GUI.color;
                    EditorGUI.DrawRect(rect, highlightColor);
                    GUI.color = c;
                }

                // --- Left radio button for preview selection ---
                bool selected = (ClipDrawer.Clip == clip);
                bool newSelected = GUILayout.Toggle(selected, GUIContent.none, EditorStyles.radioButton,
                    GUILayout.Width(18));
                if (newSelected && !selected)
                {
                    ClipDrawer.Clip = clip;
                    GUI.changed = true;
                }

                // --- Clip name (left aligned) ---
                EditorGUILayout.LabelField(clip.name, GUILayout.ExpandWidth(true));

                // --- Clip length (tight to checkbox) ---
                EditorGUILayout.LabelField($"{clip.length:F2}s", GUILayout.Width(50));

                // --- Right-hand inclusion checkbox ---
                bool newInclude = GUILayout.Toggle(include, GUIContent.none, GUILayout.Width(18));

                EditorGUILayout.EndHorizontal();

                // --- Handle inclusion changes ---
                if (evt != null && newInclude != include)
                {
                    Undo.RecordObject(AudioSMB, "Toggle Clip Inclusion");

                    if (newInclude)
                        evt.ExcludedClips.Remove(clip);
                    else if (!evt.ExcludedClips.Contains(clip))
                        evt.ExcludedClips.Add(clip);

                    EditorUtility.SetDirty(AudioSMB);
                }
            }

            SirenixEditorGUI.EndBox();
        }

        // ---------------------------------------------------------------
        // Preview Simulation Logic
        // ---------------------------------------------------------------

        private void HandlePreviewPlayback()
        {
            if (ClipDrawer.Clip == null)
                return;

            var isPlaying = ClipDrawer.Playing;
            float currentTime = ClipDrawer.NormalizedTime;

            if (!_wasPlaying && isPlaying)
            {
                var stateInfo = CreateFakeStateInfo(currentTime);
                AudioSMB.OnStateEnter(null, stateInfo, 0);
            }
            else if (_wasPlaying && isPlaying)
            {
                var stateInfo = CreateFakeStateInfo(currentTime);
                AudioSMB.OnStateUpdate(null, stateInfo, 0);
            }
            else if (_wasPlaying && !isPlaying)
            {
                var stateInfo = CreateFakeStateInfo(currentTime);
                AudioSMB.OnStateExit(null, stateInfo, 0);
            }

            _wasPlaying = isPlaying;
        }

        private static AnimatorStateInfo CreateFakeStateInfo(float normalizedTime, float length = 1f)
        {
            var info = new AnimatorStateInfo();
            var type = typeof(AnimatorStateInfo);
            BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;

            type.GetField("m_Name", flags)?.SetValueDirect(__makeref(info), 0);
            type.GetField("m_Path", flags)?.SetValueDirect(__makeref(info), 0);
            type.GetField("m_FullPath", flags)?.SetValueDirect(__makeref(info), 0);
            type.GetField("m_NormalizedTime", flags)?.SetValueDirect(__makeref(info), normalizedTime);
            type.GetField("m_Length", flags)?.SetValueDirect(__makeref(info), length);
            type.GetField("m_Speed", flags)?.SetValueDirect(__makeref(info), 1f);
            type.GetField("m_SpeedMultiplier", flags)?.SetValueDirect(__makeref(info), 1f);
            type.GetField("m_Tag", flags)?.SetValueDirect(__makeref(info), 0);
            type.GetField("m_Loop", flags)?.SetValueDirect(__makeref(info), 1);
            return info;
        }

        // ---------------------------------------------------------------
        // IMarkerTrackSource Implementation
        // ---------------------------------------------------------------

        public int GetMarkerCount() => AudioSMB.Events.Count;
        public float GetMarkerTime(int index) => AudioSMB.Events[index].NormalizedTime;

        public void UpdateMarkerTime(int index, float newNormalizedTime)
        {
            Undo.RecordObject(AudioSMB, "Move Marker");
            AudioSMB.Events[index].NormalizedTime = newNormalizedTime;
        }

        public void AddMarker(float normalizedTime)
        {
            Undo.RecordObject(AudioSMB, "Add Marker");
            AudioSMB.Events.Add(new AudioSMBEvent
            {
                NormalizedTime = normalizedTime,
                AudioEvent = new EventReference()
            });
        }

        public void RemoveMarker(int index)
        {
            if (index < 0 || index >= AudioSMB.Events.Count) return;
            Undo.RecordObject(AudioSMB, "Remove Marker");
            AudioSMB.Events.RemoveAt(index);
        }

        public string GetMarkerLabel(int index)
        {
            var evt = AudioSMB.Events[index];
            return evt.AudioEvent.IsNull ? $"Missing {index}" : evt.AudioEvent.Path.Split("/").Last();
        }

        public float GetSuggestedNewMarkerTime() => Mathf.Repeat(ClipDrawer.NormalizedTime, 1f);
    }
}
