using Sirenix.OdinInspector;
using UnityEngine;
using UnityJigs.Extensions;

namespace UnityJigs.SMBs
{
    // A normalized-time window within a state, plus a membership query. Base for window emitters
    // (WindowEventSMB) and window markers (anything polled via IsInRange). Overlap-safe via AnyState.
    public abstract class WindowSMB : TrackedStateSMB
    {
        [MinMaxSlider(0, 1, true)] public Vector2 Window = new(0f, 1f);

        // True while the active state is inside the window. Overlap-safe — checks every known entry.
        public bool IsInRange() => AnyState(this, static (smb, info) =>
        {
            var t = info.NormalizedTimeClamp01();
            return t >= smb.Window.x && t <= smb.Window.y;
        });
    }
}
