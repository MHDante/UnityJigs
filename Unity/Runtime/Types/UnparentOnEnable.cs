using System;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityJigs.Attributes;

namespace UnityJigs
{
    public class UnparentOnEnable : MonoBehaviour
    {
        [ReadOnly] public Transform Parent = null!;
        public bool RevertOnDisable = false;

        [Header("Rigidbodies")]
        public Rigidbody? MyRigidbody;
        [ShowIf(nameof(MyRigidbody))]
        public Rigidbody? ParentRigidbody;


        [ShowIf(nameof(MyRigidbody))]
        public bool ClearExclusions;
        [ShowIf(nameof(MyRigidbody))] [ShowIf(nameof(ClearExclusions))]
        public LayerMask NewExclusionLayer;
        [ShowIf(nameof(MyRigidbody))] [ShowIf(nameof(ClearExclusions))]
        public float ClearExclusionsDelay = 0f;

        [Header("Layer Swap")]
        public bool SwapLayer = false;
        [ShowIf(nameof(SwapLayer))]
        [Layer] public int NewLayer;
        [ShowIf(nameof(SwapLayer)), MinValue(0)]
        public float LayerSwapDelay = 0f;

        private float _unparentTime = 0f;
        private int _oldLayer;
        private LayerMask _oldExclusionLayer;

        private void Reset()
        {
            if (!MyRigidbody) MyRigidbody = GetComponent<Rigidbody>();
            if (!ParentRigidbody) MyRigidbody = GetComponentInParent<Rigidbody>();
        }

        private void OnValidate() => Parent = transform.parent;

        private void OnEnable()
        {
            transform.parent = null;
            _oldLayer = gameObject.layer;
            if (SwapLayer && LayerSwapDelay <= 0) gameObject.layer = NewLayer;
            _unparentTime = Time.time;
            if (MyRigidbody)
            {
                _oldExclusionLayer = MyRigidbody.excludeLayers;
                if (ClearExclusions && ClearExclusionsDelay <= 0) MyRigidbody.excludeLayers = NewExclusionLayer;
                if (ParentRigidbody)
                {
                    MyRigidbody.linearVelocity = ParentRigidbody.linearVelocity;
                    MyRigidbody.angularVelocity = ParentRigidbody.angularVelocity;
                }
            }
        }

        private void Update()
        {
            if (SwapLayer && Time.time > _unparentTime + LayerSwapDelay) gameObject.layer = NewLayer;
            if (MyRigidbody && ClearExclusions && Time.time > _unparentTime + ClearExclusionsDelay)
                MyRigidbody.excludeLayers = NewExclusionLayer;
        }

        private void OnDisable()
        {
            if (!RevertOnDisable) return;
            transform.parent = Parent ? Parent : null;
            gameObject.layer = _oldLayer;
            if (MyRigidbody)
            {
                MyRigidbody.excludeLayers = _oldExclusionLayer;
                MyRigidbody.linearVelocity = Vector3.zero;
                MyRigidbody.angularVelocity = Vector3.zero;
            }
        }
    }
}
