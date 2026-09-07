using System;
using FMOD;
using FMOD.Studio;
using FMODUnity;
using UnityEngine;

namespace UnityJigs.Fmod
{
    /// <summary>
    /// Serializable reference to an FMOD mixer bus by path ("bus:/" is the master bus).
    /// Sibling of <see cref="FmodBank"/>; helpers forward to the RUNTIME Studio system, so only call them
    /// in play mode / a player (touching RuntimeManager in edit mode spins up the runtime system).
    /// </summary>
    [Serializable]
    public struct FmodBus
    {
        public const string MasterPath = "bus:/";

        [SerializeField] private string BusPath;

        public FmodBus(string path) => BusPath = path;

        /// <summary> Full bus path, e.g. "bus:/SFX". "bus:/" is the master bus. </summary>
        public string Path => BusPath;

        public bool IsValid => !string.IsNullOrEmpty(BusPath);
        public bool IsMaster => BusPath == MasterPath;

        /// <summary> Last path segment ("SFX" for "bus:/SFX"); "Master" for the master bus. </summary>
        public string Name
        {
            get
            {
                if (!IsValid) return "";
                if (IsMaster) return "Master";
                var i = BusPath.LastIndexOf('/');
                return i < 0 ? BusPath : BusPath.Substring(i + 1);
            }
        }

        /// <summary>
        /// Resolves the bus on the runtime Studio system. False when the path is empty or the master bank
        /// (which owns the bus hierarchy) isn't loaded yet.
        /// </summary>
        public bool TryGetBus(out Bus bus)
        {
            bus = default;
            if (!IsValid) return false;
            return RuntimeManager.StudioSystem.getBus(BusPath, out bus) == RESULT.OK && bus.isValid();
        }

        /// <summary> Throwing variant (FMODUnity.BusNotFoundException). </summary>
        public Bus GetBus() => RuntimeManager.GetBus(BusPath);

        /// <summary> Linear 0..1 fader volume. Reads 0 / ignores writes while the bus is unresolved. </summary>
        public float Volume
        {
            get => TryGetBus(out var bus) && bus.getVolume(out var volume) == RESULT.OK ? volume : 0f;
            set => TrySetVolume(value);
        }

        public bool TrySetVolume(float volume) => TryGetBus(out var bus) && bus.setVolume(volume) == RESULT.OK;

        /// <summary> Pauses every event routed through this bus (unlike RuntimeManager.MuteAllEvents, per bus). </summary>
        public bool TrySetPaused(bool paused) => TryGetBus(out var bus) && bus.setPaused(paused) == RESULT.OK;

        public override string ToString() => BusPath ?? "<null>";

        public static implicit operator string(FmodBus b) => b.BusPath;
        public static implicit operator FmodBus(string s) => new(s);
    }
}
