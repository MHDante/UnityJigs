using Sirenix.OdinInspector;
using UnityEngine;
using UnityJigs.Attributes;

namespace UnityJigs
{
    public class UnparentOnEnable : MonoBehaviour
    {
        [ReadOnly]public Transform Parent = null!;
        public bool RevertOnDisable = false;

        [Header("Layer Swap")]
        public bool SwapLayer = false;
        [ShowIf(nameof(SwapLayer))]
        [Layer]public int NewLayer;
        [ShowIf(nameof(SwapLayer)), MinValue(0)]
        public float LayerSwapDelay = 0f;

        private float _unparentTime = 0f;
        private int _oldLayer;


        private void OnValidate() => Parent =  transform.parent;
        private void OnEnable()
        {
            _oldLayer = gameObject.layer;
            transform.parent = null;
            _oldLayer = gameObject.layer;
            if (SwapLayer && LayerSwapDelay <= 0) gameObject.layer = NewLayer;
            _unparentTime = Time.time;
        }


        private void Update()
        {
            if (SwapLayer && Time.time > _unparentTime + LayerSwapDelay) gameObject.layer = NewLayer;
        }

        private void OnDisable()
        {
            if(!RevertOnDisable) return;
            transform.parent = Parent ? Parent : null;
            gameObject.layer = _oldLayer;
        }
    }
}
