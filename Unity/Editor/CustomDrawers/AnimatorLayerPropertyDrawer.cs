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
    public class AnimatorLayerPropertyDrawer : OdinValueDrawer<AnimatorLayer>
    {
        private const string? NoLayerLabel = "SELECT A LAYER";
        private AnimatorResolver _animatorResolver = null!;

        protected override void Initialize()
        {
            base.Initialize();
            _animatorResolver = new(Property, nameof(AnimatorLayer.Animator));
        }

        protected override void DrawPropertyLayout(GUIContent label)
        {
            var animatorController = _animatorResolver.UpdateAnimatorProp(out var animator);
            var nameProp = Property.Children.Get(nameof(AnimatorLayer.Name));
            var currentName = (string)nameProp.ValueEntry.WeakSmartValue;

            if (animatorController == null)
            {
                var newName = SirenixEditorFields.TextField(label, currentName);
                if (newName == currentName) return;
                nameProp.ValueEntry.WeakSmartValue = newName;
                return;
            }

            var layerNames = animatorController.layers.Select(p => p.name).Prepend(NoLayerLabel).ToArray();

            var currentIndex = string.IsNullOrWhiteSpace(currentName) ? 0 : Array.IndexOf(layerNames, currentName);
            if (currentIndex < 0) SirenixEditorGUI.ErrorMessageBox($"Animator Layer Does not Exist: ({currentName})!");

            label.image = EditorIcons.List.Active;

            if (label.text.StartsWith("AnimLayer")) label.text = label.text[9..];
            else if (label.text.StartsWith("AnimLayer_")) label.text = label.text[10..];

            SirenixEditorGUI.BeginHorizontalPropertyLayout(label);
            var newIndex = SirenixEditorFields.Dropdown(currentIndex, layerNames);


            bool canCreate = newIndex <= 0;
            bool pressed = canCreate
                ? SirenixEditorGUI.IconButton(EditorIcons.Plus, tooltip: "Create Layer")
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
                if (name.StartsWith("AnimLayer")) name = name[9..];
                else if (name.StartsWith("AnimLayer_")) name = name[10..];
                Undo.RecordObject(animatorController, "Add Layer");
                CreateLayer(animatorController, name);
                nameProp.ValueEntry.WeakSmartValue = name;
                return;
            }

            if (newIndex < 0) return;
            if (newIndex == currentIndex) return;

            nameProp.ValueEntry.WeakSmartValue = newIndex == 0 ? "" : layerNames[newIndex];
        }


        private void CreateLayer(AnimatorController animatorController, string layerName)
        {
            var name = animatorController.MakeUniqueLayerName(layerName);
            animatorController.AddLayer(new AnimatorControllerLayer() { name = name,defaultWeight = 1});
        }

    }
}
