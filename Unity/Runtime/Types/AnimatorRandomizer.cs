using UnityEngine;

namespace NorthShore
{
    [RequireComponent(typeof(Animator))]
    public class AnimatorRandomizer : MonoBehaviour
    {
        public float Range = 1000;
        private Animator _animator = null!;
        private static readonly int AnimRandom = Animator.StringToHash("Random");
        private void Awake() => _animator = GetComponent<Animator>();

        private void Update()
        {
            if (!_animator) return;
            var t = Random.Range(0, 1f) * Range;
            _animator.SetFloat(AnimRandom, t);
        }

    }
}
