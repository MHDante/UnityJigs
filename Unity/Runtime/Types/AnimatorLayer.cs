using System;
using UnityEngine;

namespace UnityJigs.Types
{
    [Serializable]
    public class AnimatorLayer
    {
        public string Name = "";

        [SerializeField] public Animator? Animator;
        private bool _hasLogged;

        public void SetWeight(float w)
        {
            if(string.IsNullOrEmpty(Name)) return;

            if (Animator == null)
            {
                if (!_hasLogged && Application.isEditor)
                {
                    Debug.LogWarning($"Animator is null: {this}", Animator);
                    _hasLogged = true;
                }
                return;
            }
            var index = Animator.GetLayerIndex(Name);
            if (index == -1)
            {
                if (!_hasLogged && Application.isEditor)
                {
                    Debug.LogWarning($"Animator does not have a layer with the name {Name}.", Animator);
                    _hasLogged = true;
                }
                return;
            }
            Animator.SetLayerWeight(index, w);
        }

    }
}
