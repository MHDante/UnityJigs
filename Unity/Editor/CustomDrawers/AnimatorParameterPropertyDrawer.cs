using System;
using System.Linq;
using JetBrains.Annotations;
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities.Editor;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityJigs.Types;

namespace UnityJigs.Editor.CustomDrawers
{
    [UsedImplicitly]
    public class AnimatorParameterPropertyDrawer : OdinValueDrawer<AnimatorParameter>, IDefinesGenericMenuItems
    {
        private const string? NoParameterLabel = "SELECT A PARAMETER";
        private AnimatorResolver _animatorResolver = null!;

        protected override void Initialize()
        {
            base.Initialize();
            _animatorResolver = new(Property, nameof(AnimatorParameter.Animator));
        }

        protected override void DrawPropertyLayout(GUIContent? label)
        {

            var animatorController = _animatorResolver.UpdateAnimatorProp(out var animator);

            var param = ValueEntry.SmartValue;
            var nameProp = Property.Children.Get(nameof(AnimatorParameter.Name));
            var idProp = Property.Children.Get(nameof(AnimatorParameter.Id));
            var currentName = (string)nameProp.ValueEntry.WeakSmartValue;
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

            if (label != null)
            {
                label.image = EditorIcons.Timer.Active;
                if (label.text.StartsWith("Anim_")) label.text = " " + label.text[5..];
                else if (label.text.StartsWith("Anim")) label.text = " " + label.text[4..];
            }


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


        private void CreateParameter(AnimatorController animatorController, string propertyName,
            AnimatorControllerParameterType type)
        {
            var name = animatorController.MakeUniqueParameterName(propertyName);
            animatorController.AddParameter(new AnimatorControllerParameter { name = name, type = type });
        }

        public void PopulateGenericMenu(InspectorProperty property, GenericMenu genericMenu)
        {
            var hasAnimator = _animatorResolver.GetAnimator(property, out var animator);
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
