using System;
using System.Linq;
using System.Reflection;
using Sirenix.OdinInspector.Editor;
using Sirenix.OdinInspector.Editor.ValueResolvers;
using UnityEditor.Animations;
using UnityEngine;
using UnityJigs.Attributes;

namespace UnityJigs.Editor.CustomDrawers
{
    public class AnimatorResolver
    {
        private readonly ValueResolver<Animator>? _animatorResolver;
        private readonly InspectorProperty _property;
        private readonly string _animatorPropName;

        public AnimatorResolver(InspectorProperty property, string animatorPropName)
        {
            _property = property;
            _animatorPropName = animatorPropName;
            var attr = _property.GetAttribute<AnimatorAttribute>();
            if (attr != null) _animatorResolver = ValueResolver.Get<Animator>(_property, attr.AnimatorPath);
        }

        public AnimatorController? UpdateAnimatorProp(out Animator? animator)
        {
            AnimatorController? animatorController = null;

            var foundAnimator = GetAnimator(_property, out animator);

            var animProp = _property.Children.Get(_animatorPropName);

            if (foundAnimator)
            {
                animProp.ValueEntry.WeakSmartValue = animator;
            }
            else
            {
                animProp.Draw();
                animator = animProp.ValueEntry.WeakSmartValue as Animator;
            }

            if (animator != null)
            {
                animatorController = animator.runtimeAnimatorController is AnimatorOverrideController oc
                    ? oc.runtimeAnimatorController as AnimatorController
                    : animator.runtimeAnimatorController as AnimatorController;
            }

            return animatorController;
        }


        public bool GetAnimator(InspectorProperty prop, out Animator? animator)
        {
            var foundAnimator = false;
            if (_animatorResolver != null)
            {
                try
                {
                    animator = _animatorResolver?.GetValue();
                }
                catch
                {
                    animator = null;
                }

                foundAnimator = true;
            }
            else
            {
                var autoAnimProp = prop.Parent.Children.FirstOrDefault(
                    it => it.Name.Equals("Animator", StringComparison.InvariantCultureIgnoreCase) &&
                          it.Info.TypeOfValue == typeof(Animator));
                if (autoAnimProp != null) foundAnimator = true;
                animator = autoAnimProp?.ValueEntry.WeakSmartValue as Animator;
            }

            if (!foundAnimator)
            {
                var obj = prop.Parent.ValueEntry.WeakSmartValue;
                var objProps = obj.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                var animProp = objProps.FirstOrDefault(it =>
                    it.Name.Equals("Animator", StringComparison.InvariantCultureIgnoreCase) &&
                    it.PropertyType== typeof(Animator) && it.CanRead);
                if (animProp != null) foundAnimator = true;
                animator = (Animator?)animProp?.GetValue(obj);
            }

            if (!foundAnimator)
            {
                var obj = prop.Parent.ValueEntry.WeakSmartValue;
                var objProps = obj.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                var animProp = objProps.FirstOrDefault(it =>
                    it.Name.Equals("Animator", StringComparison.InvariantCultureIgnoreCase) &&
                    it.FieldType== typeof(Animator));
                if (animProp != null) foundAnimator = true;
                animator = (Animator?)animProp?.GetValue(obj);
            }

            return foundAnimator;
        }
    }
}
