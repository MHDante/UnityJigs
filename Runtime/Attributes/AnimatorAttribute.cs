using System;

namespace UnityUtils.Attributes
{
    public class AnimatorAttribute : Attribute
    {
        public readonly string AnimatorPath;
        public AnimatorAttribute(string animatorPath) => AnimatorPath = animatorPath;
    }
}