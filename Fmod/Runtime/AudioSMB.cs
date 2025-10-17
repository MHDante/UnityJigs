using System;
using System.Collections.Generic;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;
using STOP_MODE = FMOD.Studio.STOP_MODE;

namespace UnityJigs.Fmod
{
    /// <summary>
    /// StateMachineBehaviour that plays FMOD events tied to normalized animation time markers.
    /// Handles overlapping re-entries, loops, and editor-preview playback (animator == null).
    /// </summary>
    public class AudioSMB : StateMachineBehaviour
    {
        public static Func<EventReference, EventInstance>? OnEditorPlay;

        [SerializeField]
        public List<AudioSMBEvent> Events = new();

        [Header("Playback")]
        public bool StopOnExit = true;
        public STOP_MODE StopMode = STOP_MODE.ALLOWFADEOUT;

        // -------------------------------------------------------------
        // Internal per-entry tracking
        // -------------------------------------------------------------

        private sealed class EntryContext
        {
            public int Id;
            public float LastTime;
            public readonly HashSet<int> FiredThisLoop = new();
            public readonly List<EventInstance> Instances = new();
            public int LastUpdatedFrame = -1;
        }

        private static int _NextEntryId;
        private readonly List<EntryContext> _entries = new();

        private AnimatorAudioHelper? _helper;
        private Rigidbody? _rigidbody;
        private bool _referencesInitialized;
        private int _editorTick; // increments in editor-driven updates (animator == null)

        // -------------------------------------------------------------
        // Unity Callbacks
        // -------------------------------------------------------------

        public override void OnStateEnter(Animator? animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            base.OnStateEnter(animator, stateInfo, layerIndex);

            EnsureReferences(animator);

            // Register new logical entry
            var entry = new EntryContext
            {
                Id = _NextEntryId++,
                LastTime = stateInfo.normalizedTime,
            };

            _entries.Add(entry);
        }

        public override void OnStateUpdate(Animator? animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            base.OnStateUpdate(animator, stateInfo, layerIndex);
            if (Events.Count == 0 || _entries.Count == 0) return;

            // Use Time.frameCount in play mode; synthetic tick in editor preview
            int frame = animator == null ? ++_editorTick : Time.frameCount;

            float time = stateInfo.normalizedTime;

            var entry = FindBestEntryForUpdate(time, frame);
            if (entry == null) return;

            EvaluateEntry(animator, entry, time);

            entry.LastTime = time;
            entry.LastUpdatedFrame = frame;
        }


        public override void OnStateExit(Animator? animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            base.OnStateExit(animator, stateInfo, layerIndex);

            if (StopOnExit)
                StopOldestEntry();
        }

        // -------------------------------------------------------------
        // Core Evaluation
        // -------------------------------------------------------------

        private void EvaluateEntry(Animator? animator, EntryContext entry, float time)
        {
            float prevTime = entry.LastTime;

            // Fractions (0..1) for crossing checks
            float prevFrac = Mathf.Repeat(prevTime, 1f);
            float currFrac = Mathf.Repeat(time, 1f);

            // Loop detection
            bool previewMode = (animator == null);
            bool looped;

            if (previewMode)
            {
                // Editor preview usually wraps within [0,1]; detect 1->0 wrap
                looped = (currFrac + Mathf.Epsilon) < prevFrac;
            }
            else
            {
                // Runtime often increases beyond 1; detect loop by floor change
                int prevLoop = Mathf.FloorToInt(prevTime);
                int currLoop = Mathf.FloorToInt(time);
                looped = currLoop != prevLoop;
            }

            if (looped)
                entry.FiredThisLoop.Clear();

            for (int i = 0; i < Events.Count; i++)
            {
                if (entry.FiredThisLoop.Contains(i))
                    continue;

                var evt = Events[i];
                if (evt.AudioEvent.IsNull)
                    continue;

                float t = Mathf.Clamp01(evt.NormalizedTime);

                bool crossed =
                    (!looped && prevFrac - Mathf.Epsilon <= t && t <= currFrac + Mathf.Epsilon)
                    ||
                    (looped && (t >= prevFrac - Mathf.Epsilon || t <= currFrac + Mathf.Epsilon));

                if (!crossed)
                    continue;

                entry.FiredThisLoop.Add(i);
                FireEvent(animator, evt, entry);
            }

            // Note: entry.LastTime is updated by caller after EvaluateEntry
        }

        private void FireEvent(Animator? animator, AudioSMBEvent evt, EntryContext entry)
        {

            Debug.Log("FIRE" + evt.AudioEvent.Path);
            if (animator == null)
            {
                // Editor preview
                OnEditorPlay?.Invoke(evt.AudioEvent);

                return;
            }

            var instance = evt.AudioEvent.CreateInstance();

            if (_rigidbody != null)
                instance.AttachTo(_rigidbody);
            else
            {
                var targetTransform = _helper?.AudioOrigin != null
                    ? _helper.AudioOrigin
                    : animator.transform;

                instance.AttachTo(targetTransform.gameObject);
            }

            instance.start();
            entry.Instances.Add(instance);
        }

        // -------------------------------------------------------------
        // Entry management helpers
        // -------------------------------------------------------------

        private EntryContext? FindBestEntryForUpdate(float time, int frame)
        {
            EntryContext? best = null;
            float bestDelta = float.MaxValue;

            foreach (var e in _entries)
            {
                if (e.LastUpdatedFrame == frame)
                    continue; // already updated this frame

                float delta = time - e.LastTime;
                if (delta >= 0f && delta < bestDelta)
                {
                    best = e;
                    bestDelta = delta;
                }
            }

            if (best == null && _entries.Count > 0)
            {
                // fallback: newest non-updated entry
                for (int i = _entries.Count - 1; i >= 0; i--)
                {
                    var e = _entries[i];
                    if (e.LastUpdatedFrame != frame)
                        return e;
                }
            }

            return best;
        }

        private void StopOldestEntry()
        {
            if (_entries.Count == 0)
                return;

            var entry = _entries[0];
            foreach (var inst in entry.Instances)
            {
                if (!inst.isValid()) continue;
                inst.stop(StopMode);
                inst.release();
            }

            _entries.RemoveAt(0);
        }

        // -------------------------------------------------------------
        // Shared instance helpers
        // -------------------------------------------------------------

        private void EnsureReferences(Animator? animator)
        {
            if (_referencesInitialized)
                return;

            _helper = animator?.GetComponent<AnimatorAudioHelper>();
            _rigidbody = _helper?.Rigidbody ?? animator?.GetComponent<Rigidbody>();
            _referencesInitialized = true;
        }
    }
}
