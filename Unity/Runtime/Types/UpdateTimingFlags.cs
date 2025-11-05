using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityJigs.Types
{
    [Flags]
    public enum UpdateTimingFlags
    {
        None = 0,
        Update = 1 << 0,
        FixedUpdate = 1 << 1,
        LateUpdate = 1 << 2,
        InEditor = 1 << 3
    }

    public static class UpdateTimingFlagsExtensions
    {
        public static bool Applies(this UpdateTimingFlags flags, Object obj, UpdateTimingFlags flag)
        {
            var isEditor = Application.IsPlaying(obj);
            var hasEditorFlag = flags.HasFlag(UpdateTimingFlags.InEditor);
            if (isEditor && !hasEditorFlag) return false;
            if (flag == UpdateTimingFlags.Update && hasEditorFlag) return true;
            if (flags.HasFlag(flag)) return true;
            return false;
        }
    }
}
