using System.Collections.Generic;
using UnityEngine;

namespace UnityJigs
{
    public class AudioSourcePool : MonoBehaviour
    {
        [SerializeField] private AudioSource AudioSourcePrefab = null!;

        private readonly List<AudioSlot> _slots = new List<AudioSlot>();

        internal class AudioSlot
        {
            public AudioSource Source = null!;
            public bool InUse;
            public float FadeSpeed;
            public float TargetVolume;
        }

        public readonly struct AudioToken
        {
            private readonly AudioSourcePool _pool;
            private readonly AudioSlot _audioSlot;

            internal AudioToken(AudioSourcePool pool, AudioSlot audioSlot)
            {
                _pool = pool;
                _audioSlot = audioSlot;
            }

            public bool IsValid => _pool != null && _audioSlot is { InUse: true };

            public void Stop()
            {
                if (!IsValid) return;
                _audioSlot.Source.Stop();
                _audioSlot.InUse = false;
            }

            public void FadeOut(float duration)
            {
                if (!IsValid) return;
                _audioSlot.FadeSpeed = -_audioSlot.Source.volume / Mathf.Max(duration, 0.0001f);
                _audioSlot.TargetVolume = 0f;
            }

            public void FadeIn(float duration, float target = 1f)
            {
                if (!IsValid) return;
                _audioSlot.Source.volume = 0f;
                _audioSlot.FadeSpeed = target / Mathf.Max(duration, 0.0001f);
                _audioSlot.TargetVolume = target;
            }
        }

        private void Awake()
        {
            if (!AudioSourcePrefab)
            {
                // fallback prefab if not assigned
                var go = new GameObject("AudioSourcePrefab");
                go.hideFlags = HideFlags.HideAndDontSave;
                AudioSourcePrefab = go.AddComponent<AudioSource>();
            }
        }

        private AudioSlot CreateSlot()
        {
            var src = Instantiate(AudioSourcePrefab, transform);
            src.playOnAwake = false;
            var slot = new AudioSlot { Source = src, InUse = false };
            _slots.Add(slot);
            return slot;
        }

        private AudioSlot GetFreeSlot()
        {
            foreach (var s in _slots)
                if (!s.InUse)
                    return s;

            return CreateSlot();
        }

        public AudioToken PlayOneShot(AudioClip clip, float volume = 1f, float pitch = 1f)
        {
            if (!clip) return default;

            var slot = GetFreeSlot();
            slot.InUse = true;
            slot.Source.clip = clip;
            slot.Source.volume = volume;
            slot.Source.pitch = pitch;
            slot.Source.loop = false;
            slot.Source.Play();
            slot.FadeSpeed = 0f;
            slot.TargetVolume = volume;

            return new AudioToken(this, slot);
        }

        public AudioToken Sustain(AudioClip clip, float volume = 1f, float pitch = 1f)
        {
            if (!clip) return default;

            var slot = GetFreeSlot();
            slot.InUse = true;
            slot.Source.clip = clip;
            slot.Source.volume = volume;
            slot.Source.pitch = pitch;
            slot.Source.loop = true;
            slot.Source.Play();
            slot.FadeSpeed = 0f;
            slot.TargetVolume = volume;

            return new AudioToken(this, slot);
        }

        private void Update()
        {
            foreach (var s in _slots)
            {
                if (!s.InUse) continue;

                // handle fades
                if (s.FadeSpeed != 0f)
                {
                    s.Source.volume += s.FadeSpeed * Time.deltaTime;

                    if ((s.FadeSpeed < 0f && s.Source.volume <= s.TargetVolume) ||
                        (s.FadeSpeed > 0f && s.Source.volume >= s.TargetVolume))
                    {
                        s.Source.volume = s.TargetVolume;
                        s.FadeSpeed = 0f;

                        if (s.TargetVolume <= 0f)
                        {
                            s.Source.Stop();
                            s.InUse = false;
                        }
                    }
                }
                else if (!s.Source.isPlaying)
                {
                    // release one-shots automatically
                    s.InUse = false;
                }
            }
        }
    }
}
