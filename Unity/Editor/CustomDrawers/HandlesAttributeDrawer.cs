using System;
using JetBrains.Annotations;
using Sirenix.OdinInspector.Editor;
using Sirenix.OdinInspector.Editor.ValueResolvers;
using Sirenix.Utilities.Editor;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using UnityJigs.Attributes.Odin;

namespace UnityJigs.Editor.CustomDrawers
{
    [UsedImplicitly, DrawerPriority(DrawerPriorityLevel.WrapperPriority)]
    public class HandlesAttributeDrawer : OdinSceneGUIAttributeDrawer<HandlesAttribute>
    {
        private ValueResolver<Color> _colorResolver = null!;
        private readonly BoxBoundsHandle _handleDrawer = new();

        protected override void Initialize()
        {
            base.Initialize();
            _colorResolver = ValueResolver.Get(Property, Attribute.Color,Color.black);
            if (Property.Info.TypeOfValue != typeof(Bounds))
                throw new Exception("Handles attribute can only be place on bounds");
        }

        protected override void DrawPropertyLayout(GUIContent label)
        {
            _colorResolver.DrawError();

            GUIHelper.PushColor(_colorResolver.GetValue());
            CallNextDrawer(label);
            GUIHelper.PopColor();
        }

        protected override void OnSceneGUI(SceneView sv)
        {
            var target = Property.SerializationRoot.ValueEntry.WeakSmartValue;
            if (target is not Component c) return;
            if(Attribute.DrawOnlyWhenExpanded && !Property.State.Expanded) return;
            using var _ = new Handles.DrawingScope(_colorResolver.GetValue(), c.gameObject.transform.localToWorldMatrix);
            var bounds = Property.TryGetTypedValueEntry<Bounds>().SmartValue;
            _handleDrawer.size = bounds.size;
            _handleDrawer.center = bounds.center;

            EditorGUI.BeginChangeCheck();
            _handleDrawer.DrawHandle();

            if (!EditorGUI.EndChangeCheck()) return;

            bounds.center = _handleDrawer.center;
            bounds.size = _handleDrawer.size;
            Property.ValueEntry.WeakSmartValue = bounds;
            SceneView.RepaintAll();
        }
    }
}
