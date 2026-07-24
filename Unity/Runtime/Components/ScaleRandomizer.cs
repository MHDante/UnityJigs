using UnityEngine;

namespace UnityJigs.Components
{
    public class ScaleRandomizer : MonoBehaviour
    {
        public float MinScale = 0.8f;
        public float MaxScale = 1.2f;

        private void Awake() => transform.localScale *= Random.Range(MinScale, MaxScale);
    }
}
