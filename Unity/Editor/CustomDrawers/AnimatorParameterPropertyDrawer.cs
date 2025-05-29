using System;
using System.Linq;
using JetBrains.Annotations;
using Sirenix.OdinInspector.Editor;
using Sirenix.OdinInspector.Editor.ValueResolvers;
using Sirenix.Utilities.Editor;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityJigs.Attributes;
using UnityJigs.Types;

namespace UnityJigs.Editor.CustomDrawers
{
    [UsedImplicitly]
    public class AnimatorParameterPropertyDrawer : OdinValueDrawer<AnimatorParameter>, IDefinesGenericMenuItems
    {
        private const string? NoParameterLabel = "SELECT A PARAMETER";
        private ValueResolver<Animator>? _animatorResolver;

        protected override void Initialize()
        {
            base.Initialize();
            var attr = Property.GetAttribute<AnimatorAttribute>();
            if (attr != null) _animatorResolver = ValueResolver.Get<Animator>(Property, attr.AnimatorPath);
        }

        protected override void DrawPropertyLayout(GUIContent label)
        {
            AnimatorController? animatorController = null;
            var param = ValueEntry.SmartValue;

            var foundAnimator = GetAnimator(Property, out var animator);

            var animProp = Property.Children.Get(nameof(AnimatorParameter.Animator));
            var nameProp = Property.Children.Get(nameof(AnimatorParameter.Name));
            var idProp = Property.Children.Get(nameof(AnimatorParameter.Id));
            var currentName = (string)nameProp.ValueEntry.WeakSmartValue;

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

            if (animatorController == null)
            {
                var newName = SirenixEditorFields.TextField(label, currentName);
                if (newName == currentName) return;
                nameProp.ValueEntry.WeakSmartValue = newName;
                idProp.ValueEntry.WeakSmartValue = Animator.StringToHash(newName);
                return;
            }

            var parameterNames = animatorController.parameters.Where(it => it.type == param.Type).Select(p => p.name)
                .Prepend(NoParameterLabel).ToArray();
            var paramIds = parameterNames.Select(Animator.StringToHash).ToArray();
            paramIds[0] = 0;

            int currentIndex = string.IsNullOrWhiteSpace(currentName) ? 0 : Array.IndexOf(parameterNames, currentName);
            if (currentIndex < 0) currentIndex = Array.IndexOf(paramIds, Animator.StringToHash(currentName));
            if (currentIndex < 0)
                SirenixEditorGUI.ErrorMessageBox($"Animator Parameter Does not Exist: ({currentName})!");

            label.image = EditorIcons.Timer.Active;

            if (label.text.StartsWith("Anim_")) label.text = " " + label.text[5..];
            else if (label.text.StartsWith("Anim")) label.text = " " + label.text[4..];


            var isDampedProp = Property.Children.Get(nameof(AnimatorParameter.Float.IsDamped));

            SirenixEditorGUI.BeginHorizontalPropertyLayout(label);
            var newIndex = SirenixEditorFields.Dropdown(currentIndex, parameterNames);
            if (isDampedProp != null)
            {
                var isDampedValue = isDampedProp.TryGetTypedValueEntry<bool>();
                var pressedDamp = SirenixEditorGUI.IconButton(EditorIcons.LoadingBar, tooltip: "AddDampTime");
                if (pressedDamp) isDampedValue.SmartValue = !isDampedValue.SmartValue;
                if (isDampedValue.SmartValue)
                {
                    var dampTimeProp = Property.Children.Get(nameof(AnimatorParameter.Float.DampTime));
                    var dampValue = dampTimeProp.TryGetTypedValueEntry<float>();
                    dampValue.SmartValue = SirenixEditorFields.FloatField(GUIContent.none, dampValue.SmartValue, GUILayout.Width(40));
                }
            }

            bool canCreate = newIndex <= 0;
            bool pressed = canCreate
                ? SirenixEditorGUI.IconButton(EditorIcons.Plus, tooltip: "Create Parameter")
                : SirenixEditorGUI.IconButton(EditorIcons.ArrowRight, tooltip: "See Animator");
            SirenixEditorGUI.EndHorizontalPropertyLayout();


            if (pressed)
            {
                if (!canCreate)
                {
                    AssetDatabase.OpenAsset(animatorController);
                    Selection.activeObject = animator;
                    return;
                }

                var name = newIndex == 0 ? Property.Name : currentName;
                if (name.StartsWith("Anim_")) name = name[5..];
                else if (name.StartsWith("Anim")) name = name[4..];
                Undo.RecordObject(animatorController, "Add Parameter");
                CreateParameter(animatorController, name, param.Type);
                nameProp.ValueEntry.WeakSmartValue = name;
                idProp.ValueEntry.WeakSmartValue = Animator.StringToHash(name);
                return;
            }

            if (newIndex < 0) return;
            if (newIndex == currentIndex) return;

            nameProp.ValueEntry.WeakSmartValue = newIndex == 0 ? "" : parameterNames[newIndex];
            idProp.ValueEntry.WeakSmartValue = paramIds[newIndex];
        }

        private bool GetAnimator(InspectorProperty prop, out Animator? animator)
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
                var autoAnimProp = prop.Parent.FindChild(
                    it => it.Name.Equals("Animator", StringComparison.InvariantCultureIgnoreCase) &&
                          it.Info.TypeOfValue == typeof(Animator), false);
                if (autoAnimProp != null) foundAnimator = true;
                animator = autoAnimProp?.ValueEntry.WeakSmartValue as Animator;
            }

            return foundAnimator;
        }

        private void CreateParameter(AnimatorController animatorController, string propertyName,
            AnimatorControllerParameterType type)
        {
            var name = animatorController.MakeUniqueParameterName(propertyName);
            animatorController.AddParameter(new AnimatorControllerParameter { name = name, type = type });
        }

        public void PopulateGenericMenu(InspectorProperty property, GenericMenu genericMenu)
        {
            var hasAnimator = GetAnimator(property, out var animator);
            if (!hasAnimator || animator == null) return;
            genericMenu.AddItem(new GUIContent("Reorder"), false, () =>
            {
                var animatorController = animator.runtimeAnimatorController is AnimatorOverrideController oc
                    ? oc.runtimeAnimatorController as AnimatorController
                    : animator.runtimeAnimatorController as AnimatorController;
                if (animatorController == null) return;

                var otherProps = property.Parent.Children
                    .Where(it => it.Info.TypeOfValue.IsSubclassOf(typeof(AnimatorParameter))).ToList();
                var otherParams = otherProps.Select(it => it.TryGetTypedValueEntry<AnimatorParameter>().SmartValue)
                    .ToList();
                var otherNames = otherParams.Select(it => it.Name).ToList();
                var animParams = animatorController.parameters;
                Array.Sort(animParams, (a, b) => otherNames.IndexOf(a.name).CompareTo(otherNames.IndexOf(b.name)));
                animatorController.parameters = animParams;
            });
        }
    }
}
