using UnityEngine;
using Sirenix.OdinInspector;

namespace UnityJigs.Fmod
{
    /// <summary>
    /// Optional companion for AudioSMB that exposes common references like Rigidbody or AudioOrigin.
    /// Designed to auto-assign safe defaults.
    /// </summary>
    public class AnimatorAudioHelper : MonoBehaviour
    {
        [Tooltip("Optional Rigidbody for 3D velocity tracking.")]
        public Rigidbody? Rigidbody;

        [Tooltip("Optional transform used for 3D audio positioning. Defaults to this Transform.")]
        [Required]
        public Transform AudioOrigin = null!;

        private void Reset()
        {
            // Automatically assign safe defaults
            Rigidbody = TryGetComponent(out Rigidbody rb) ? rb : null;
            AudioOrigin = transform;
        }

        private void OnValidate()
        {
            if (AudioOrigin == null)
                AudioOrigin = transform;
        }
    }
}
