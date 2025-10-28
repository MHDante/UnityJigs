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
                Func<InspectorProperty, bool> predicate = static it =>
                    it.Name.Equals("Animator", StringComparison.InvariantCultureIgnoreCase) &&
                    it.Info.TypeOfValue == typeof(Animator);

                var autoAnimProp = prop.Parent.Children.FirstOrDefault(predicate) ??
                                   prop.Parent.Parent?.Children.FirstOrDefault(predicate) ??
                                   prop.Parent.Parent?.Parent?.Children.FirstOrDefault(predicate);

                if (autoAnimProp != null) foundAnimator = true;
                animator = autoAnimProp?.ValueEntry.WeakSmartValue as Animator;
            }

            if (!foundAnimator)
            {
                PropertyInfo? animProp = null;
                object? obj = null;
                var p = prop;
                for (int i = 0; i < 3; i++)
                {
                    p = p.Parent;
                    if (p == null) break;
                    obj = p.ValueEntry.WeakSmartValue;
                    var objProps = obj.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic |
                                                               BindingFlags.Instance | BindingFlags.Static);
                    animProp = objProps.FirstOrDefault(it =>
                        it.Name.Equals("Animator", StringComparison.InvariantCultureIgnoreCase) &&
                        it.PropertyType == typeof(Animator) && it.CanRead);
                }

                if (animProp != null) foundAnimator = true;
                animator = (Animator?)animProp?.GetValue(obj);
            }

            if (!foundAnimator)
            {
                object? obj = null;
                FieldInfo? animProp = null;

                var p = prop;
                for (int i = 0; i < 3; i++)
                {
                    p = p.Parent;
                    if (p == null) break;
                    obj = p.ValueEntry.WeakSmartValue;
                    var objProps = obj.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic |
                                                           BindingFlags.Instance | BindingFlags.Static);
                    animProp = objProps.FirstOrDefault(it =>
                        it.Name.Equals("Animator", StringComparison.InvariantCultureIgnoreCase) &&
                        it.FieldType == typeof(Animator));
                }

                if (animProp != null) foundAnimator = true;
                animator = (Animator?)animProp?.GetValue(obj);
            }

            return foundAnimator;
        }
    }
}
