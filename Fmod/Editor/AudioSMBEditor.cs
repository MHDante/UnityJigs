using System.Collections.Generic;
using System.Reflection;
using FMOD.Studio;
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
        private static bool LoadedBanks = false;
        private static readonly Dictionary<string, float> ParamValues = new();
        static AudioSMBEditor() => AudioSMB.OnEditorPlay =
            PlayEditorSound;

        private static EventInstance PlayEditorSound(EventReference ev)
        {

            if (!LoadedBanks)
            {
                EditorUtils.LoadPreviewBanks();
                LoadedBanks = true;
            }
            var editorEventRef = EventManager.EventFromPath(ev.Path);
            var eventInstance = EditorUtils.PreviewEvent(editorEventRef, ParamValues);
            return eventInstance;
        }

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

        // Playback tracking
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
            ClipDrawer.Dispose();
            ClipDrawer = null!;
        }

        // --------------------------------------------------------------------

        public override void OnInspectorGUI()
        {
            using var _ = new EditorGUI.IndentLevelScope();
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

            DrawAnimationSelection();
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

        private void DrawAnimationSelection()
        {
            EditorGUILayout.BeginHorizontal();
            var clipNames = _clips.ConvertAll(c => c.name).ToArray();
            var selectedClipIndex = _clips.FindIndex(c => c == ClipDrawer.Clip);
            if (selectedClipIndex < 0) selectedClipIndex = 0;
            var newIndex = EditorGUILayout.Popup("Preview Clip", selectedClipIndex, clipNames);
            newIndex = Mathf.Clamp(newIndex, 0, _clips.Count - 1);

            if (_clips.Count == 0)
                ClipDrawer.Clip = null;
            else if (newIndex != selectedClipIndex)
                ClipDrawer.Clip = _clips[newIndex];

            if (selectedClipIndex < _clips.Count)
            {
                var clip = _clips[selectedClipIndex];
                EditorGUILayout.LabelField($"{clip.length:F2}s", GUILayout.Width(60));
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawPreviewArea()
        {
            if (ClipDrawer.Clip == null)
                return;

            if (!ClipDrawer.HasPreviewGUI)
                return;

            // --- Details panel above timeline ---
            _selectedMarker = _markerTrack.SelectedMarkerIndex;
            if (_selectedMarker is { } idx && idx >= 0 && idx < AudioSMB.Events.Count)
            {
                var path = $"{nameof(AudioSMB.Events)}.${idx}";
                var prop = Tree.GetPropertyAtPath(path);
                if (prop != null)
                {
                    SirenixEditorGUI.BeginBox("Selected Event");
                    prop.Draw();
                    SirenixEditorGUI.EndBox();
                }
            }

            // --- Marker track (above preview timeline) ---
            _markerTrack.DrawLayout(ClipDrawer.Clip);

            // --- Preview area ---
            const float previewHeight = 180f;
            var rect = EditorGUILayout.GetControlRect(false, previewHeight);
            ClipDrawer.Draw(rect);
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
                // Enter preview
                var stateInfo = CreateFakeStateInfo(currentTime);
                AudioSMB.OnStateEnter(null, stateInfo, 0);
            }
            else if (_wasPlaying && isPlaying)
            {
                // Update preview
                var stateInfo = CreateFakeStateInfo(currentTime);
                AudioSMB.OnStateUpdate(null, stateInfo, 0);
            }
            else if (_wasPlaying && !isPlaying)
            {
                // Exit preview
                var stateInfo = CreateFakeStateInfo(currentTime);
                AudioSMB.OnStateExit(null, stateInfo, 0);
            }

            _wasPlaying = isPlaying;
        }

        private static AnimatorStateInfo CreateFakeStateInfo(float normalizedTime, float length = 1f)
        {
            var info = new AnimatorStateInfo();

            // Use reflection to set internal fields
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
            return evt.AudioEvent.IsNull ? $"Evt {index}" : evt.AudioEvent.Path;
        }

        public float GetSuggestedNewMarkerTime() => Mathf.Repeat(ClipDrawer.NormalizedTime, 1f);
    }
}
