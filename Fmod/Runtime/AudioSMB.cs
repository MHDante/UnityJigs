using System;
using System.Collections.Generic;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;
using STOP_MODE = FMOD.Studio.STOP_MODE;

namespace UnityJigs.Fmod
{
    public class AudioSMB : StateMachineBehaviour
    {
        public static Func<EventReference, EventInstance>? OnEditorPlay;

        public List<AudioSMBEvent> Events = new();

        [Header("Playback")]
        public bool StopOnExit = true;
        public STOP_MODE StopMode = STOP_MODE.ALLOWFADEOUT;

        private sealed class EntryContext
        {
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
        private int _editorTick;

        private void OnValidate()
        {
            if (Events.Count == 0)
                Events.Add(new AudioSMBEvent
                {
                    NormalizedTime = 0f,
                    AudioEvent = new EventReference(),
                    ExcludedClips = new List<AnimationClip>()
                });
        }

        // --------------------------------------------------------------------

        public override void OnStateEnter(Animator? animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            base.OnStateEnter(animator, stateInfo, layerIndex);
            EnsureReferences(animator);

            var entry = new EntryContext
            {
                LastTime = stateInfo.normalizedTime,
            };
            _entries.Add(entry);
        }

        public override void OnStateUpdate(Animator? animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            base.OnStateUpdate(animator, stateInfo, layerIndex);
            if (Events.Count == 0 || _entries.Count == 0) return;

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

        // --------------------------------------------------------------------

        private void EvaluateEntry(Animator? animator, EntryContext entry, float time)
        {
            float prevTime = entry.LastTime;
            float prevFrac = Mathf.Repeat(prevTime, 1f);
            float currFrac = Mathf.Repeat(time, 1f);

            bool previewMode = animator == null;
            bool looped = previewMode
                ? (currFrac + Mathf.Epsilon) < prevFrac
                : Mathf.FloorToInt(currFrac) != Mathf.FloorToInt(prevFrac);

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
                    || (looped && (t >= prevFrac - Mathf.Epsilon || t <= currFrac + Mathf.Epsilon));

                if (!crossed)
                    continue;

                entry.FiredThisLoop.Add(i);
                FireEvent(animator, evt, entry);
            }
        }

        private void FireEvent(Animator? animator, AudioSMBEvent evt, EntryContext entry)
        {
            if (animator == null)
            {
                OnEditorPlay?.Invoke(evt.AudioEvent);
                return;
            }

            var instance = evt.AudioEvent.CreateInstance();

            if (_rigidbody != null)
                instance.AttachTo(_rigidbody);
            else
            {
                var targetTransform = _helper?.AudioOrigin != null ? _helper.AudioOrigin : animator.transform;
                instance.AttachTo(targetTransform.gameObject);
            }

            instance.start();
            entry.Instances.Add(instance);
        }

        private EntryContext? FindBestEntryForUpdate(float time, int frame)
        {
            EntryContext? best = null;
            float bestDelta = float.MaxValue;

            foreach (var e in _entries)
            {
                if (e.LastUpdatedFrame == frame)
                    continue;

                float delta = time - e.LastTime;
                if (delta >= 0f && delta < bestDelta)
                {
                    best = e;
                    bestDelta = delta;
                }
            }

            if (best == null && _entries.Count > 0)
            {
                for (int i = _entries.Count - 1; i >= 0; i--)
                    if (_entries[i].LastUpdatedFrame != frame)
                        return _entries[i];
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

        private void EnsureReferences(Animator? animator)
        {
            if (_referencesInitialized) return;
            _helper = animator?.GetComponent<AnimatorAudioHelper>();
            _rigidbody = _helper?.Rigidbody ?? animator?.GetComponent<Rigidbody>();
            _referencesInitialized = true;
        }
    }
}
