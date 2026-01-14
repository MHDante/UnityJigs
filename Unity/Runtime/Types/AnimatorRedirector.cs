using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityJigs.Extensions;

namespace UnityJigs.Types
{
    [RequireComponent(typeof(Animator))]
    public class AnimatorEventRedirector : MonoBehaviour
    {
        public List<UnityEvent> IntEvents = new();
        public SerializedDict<string, UnityEvent> StringEvents = new();

        public void FireIntEvent(int index)
        {
            var ev = IntEvents.GetSafe(index);
            ev?.Invoke();
            if(ev == null) Debug.LogWarning($"No event found for index: {index}");
        }

        public void FireStringEvent(string key)
        {
            var ev = StringEvents.GetOrDefault(key);
            ev?.Invoke();
            if(ev == null) Debug.LogWarning($"No event found for key: {key}");
        }
    }
}
