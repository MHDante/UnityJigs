using System;
using System.Linq;
using JetBrains.Annotations;
using Sirenix.OdinInspector.Editor;
using Sirenix.OdinInspector.Editor.ValueResolvers;
using Sirenix.Utilities.Editor;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MHDante.UnityUtils.Editor
{
    [UsedImplicitly]
    public class AnimatorParameterPropertyDrawer : OdinValueDrawer<AnimatorParameter>
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

            Animator? animator;
            bool foundAnimator = false;
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
                var autoAnimProp = Property.Parent.FindChild(
                    it => it.Name.Equals("Animator", StringComparison.InvariantCultureIgnoreCase) &&
                          it.Info.TypeOfValue == typeof(Animator), false);
                if(autoAnimProp != null) foundAnimator = true;
                animator = autoAnimProp?.ValueEntry.WeakSmartValue as Animator;
            }

            var animProp = Property.FindChild(it => it.Name == nameof(AnimatorParameter.Animator), false);
            var nameProp = Property.FindChild(it => it.Name == nameof(AnimatorParameter.Name), false);
            var idProp = Property.FindChild(it => it.Name == nameof(AnimatorParameter.Id), false);
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


            SirenixEditorGUI.BeginHorizontalPropertyLayout(label);
            var newIndex = SirenixEditorFields.Dropdown(currentIndex, parameterNames);
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
    }
}
