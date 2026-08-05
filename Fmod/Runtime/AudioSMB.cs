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

            // Rent-time reset: collections are cleared (keeping their backing storage) so a recycled
            // context behaves exactly like a fresh one — including LastUpdatedFrame = -1, which keeps
            // FindBestEntryForUpdate eligible on the entry's first frame.
            public void Reset(float lastTime)
            {
                LastTime = lastTime;
                LastUpdatedFrame = -1;
                FiredThisLoop.Clear();
                Instances.Clear();
            }
        }

        private static int _NextEntryId;
        private readonly List<EntryContext> _entries = new();
        // Retired contexts for reuse: state re-entry is constant during gameplay, and each context owns
        // a HashSet + List — pooling keeps their storage instead of allocating per OnStateEnter. Static:
        // contexts are fungible (Reset at rent wipes all state, retired instances hold only released
        // handles), so one shared pool beats stranding warm contexts on every per-state SMB instance.
        private static readonly Stack<EntryContext> _EntryPool = new();

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

        public override void OnStateEnter(Animator? animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            base.OnStateEnter(animator, stateInfo, layerIndex);
            EnsureReferences(animator);

            var entry = _EntryPool.Count > 0 ? _EntryPool.Pop() : new EntryContext();
            entry.Reset(stateInfo.normalizedTime);
            _entries.Add(entry);
        }

        public override void OnStateUpdate(Animator? animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            base.OnStateUpdate(animator, stateInfo, layerIndex);
            if (Events.Count == 0 || _entries.Count == 0) return;

            var frame = animator == null ? ++_editorTick : Time.frameCount;
            var time = stateInfo.normalizedTime;
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

        private void EvaluateEntry(Animator? animator, EntryContext entry, float time)
        {
            var prevTime = entry.LastTime;
            var prevFrac = Mathf.Repeat(prevTime, 1f);
            var currFrac = Mathf.Repeat(time, 1f);

            var previewMode = animator == null;
            var looped = previewMode
                ? currFrac + Mathf.Epsilon < prevFrac
                : Mathf.FloorToInt(currFrac) != Mathf.FloorToInt(prevFrac);

            if (looped)
                entry.FiredThisLoop.Clear();

            for (var i = 0; i < Events.Count; i++)
            {
                if (entry.FiredThisLoop.Contains(i))
                    continue;

                var evt = Events[i];
                if (evt.AudioEvent.IsNull)
                    continue;

                var t = Mathf.Clamp01(evt.NormalizedTime);
                var crossed =
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
            EventInstance instance;
            if (animator == null)
            {
                var editorInstance = OnEditorPlay?.Invoke(evt.AudioEvent);
                if(editorInstance == null) return;
                instance = editorInstance.Value;
            }
            else
            {
                instance = evt.AudioEvent.CreateInstance();

                if (_rigidbody != null)
                    instance.AttachTo(_rigidbody);
                else
                {
                    var targetTransform = _helper?.AudioOrigin != null ? _helper.AudioOrigin : animator.transform;
                    instance.AttachTo(targetTransform.gameObject);
                }

                instance.start();
            }

            entry.Instances.Add(instance);
        }

        private EntryContext? FindBestEntryForUpdate(float time, int frame)
        {
            EntryContext? best = null;
            var bestDelta = float.MaxValue;

            foreach (var e in _entries)
            {
                if (e.LastUpdatedFrame == frame)
                    continue;

                var delta = time - e.LastTime;
                if (delta >= 0f && delta < bestDelta)
                {
                    best = e;
                    bestDelta = delta;
                }
            }

            if (best == null && _entries.Count > 0)
            {
                for (var i = _entries.Count - 1; i >= 0; i--)
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
            _EntryPool.Push(entry); // instances released above; Reset() wipes the handles on reuse
        }

        // TryGetComponent: a miss returns false instead of fabricating the editor-only error string
        // that GetComponent allocates on every failed lookup. The ?? fallback structure is preserved
        // exactly (a helper whose Rigidbody field is unassigned still behaves as before).
        private void EnsureReferences(Animator? animator)
        {
            if (_referencesInitialized) return;
            _helper = animator != null && animator.TryGetComponent<AnimatorAudioHelper>(out var helper) ? helper : null;
            _rigidbody = _helper?.Rigidbody ?? (animator != null && animator.TryGetComponent<Rigidbody>(out var rb) ? rb : null);
            _referencesInitialized = true;
        }
    }
}
