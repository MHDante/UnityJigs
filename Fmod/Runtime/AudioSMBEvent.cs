using System;
using System.Collections.Generic;
using FMODUnity;
using UnityEngine;

namespace UnityJigs.Fmod
{
    /// <summary>
    /// Authoring data for an audio trigger within an animator state timeline.
    /// </summary>
    [Serializable]
    public sealed class AudioSMBEvent
    {
        [Range(0f, 1f)]
        [Tooltip("Normalized time (0–1) at which this event should trigger.")]
        public float NormalizedTime = 0.0f;

        [Tooltip("FMOD event to trigger at this point in the animation.")]
        public EventReference AudioEvent;

        [Tooltip("If not empty, restricts this event to specific AnimationClips in a blend tree.")]
        public List<AnimationClip> ExcludedClips = new();
    }
}
