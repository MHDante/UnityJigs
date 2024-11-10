using System;

namespace UnityJigs.Behaviour
{
    [Flags]
    public enum UpdateTypes
    {
        None = 0,
        FixedUpdate = 1 << 0,
        LateUpdate = 1 << 1,
        Update = 1 << 2,
        OnDidApplyAnimationProperties = 1 << 3,
        OnValidate = 1 << 4,
        Awake = 1<<5,

    }

    public static class UpdateTypesExtensions
    {
        public static bool HasFlagFast(this UpdateTypes value, UpdateTypes flag) => (value & flag) != 0;
    }
}
