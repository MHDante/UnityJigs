using Sirenix.OdinInspector;
using UnityEngine;

namespace UnityJigs.Settings
{
    [InlineEditor]
    public class Reference<T> : ScriptableObject
    {
        public T? Value;
        public static implicit operator T?(Reference<T?> value) => value.Value;
    }
}
