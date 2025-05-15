using System;
using JetBrains.Annotations;
using Sirenix.OdinInspector.Editor;
using Sirenix.OdinInspector.Editor.ValueResolvers;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using UnityJigs.Attributes.Odin;

namespace UnityJigs.Editor.CustomDrawers
{
    [UsedImplicitly, DrawerPriority(DrawerPriorityLevel.WrapperPriority)]
    public class HandlesAttributeDrawer : OdinSceneGUIAttributeDrawer<HandlesAttribute>
    {
        private ValueResolver<Color> colorResolver = null!;
        private readonly BoxBoundsHandle handleDrawer = new();

        protected override void Initialize()
        {
            base.Initialize();
            colorResolver = ValueResolver.Get(Property, Attribute.Color,Color.black);
            if (Property.Info.TypeOfValue != typeof(Bounds))
                throw new Exception("Handles attribute can only be place on bounds");
        }

        protected override void DrawPropertyLayout(GUIContent label)
        {
            colorResolver.DrawError();

            var oldC = GUI.color;
            GUI.color = colorResolver.GetValue();
            var oldExp = Property.State.Expanded;
            Property.State.Expanded = EditorGUILayout.Foldout(oldExp, label);
            if(oldExp != Property.State.Expanded) SceneView.RepaintAll();
            GUI.color = oldC;
            if(Property.State.Expanded) CallNextDrawer(null);
        }

        protected override void OnSceneGUI(SceneView sv)
        {
            var target = Property.SerializationRoot.ValueEntry.WeakSmartValue;
            if (target is not Component c) return;
            if(Attribute.DrawOnlyWhenExpanded && !Property.State.Expanded) return;
            using var _ = new Handles.DrawingScope(colorResolver.GetValue(), c.gameObject.transform.worldToLocalMatrix);
            var bounds = Property.TryGetTypedValueEntry<Bounds>().SmartValue;
            handleDrawer.size = bounds.size;
            handleDrawer.center = bounds.center;

            EditorGUI.BeginChangeCheck();
            handleDrawer.DrawHandle();

            if (!EditorGUI.EndChangeCheck()) return;

            bounds.center = handleDrawer.center;
            bounds.size = handleDrawer.size;
            Property.ValueEntry.WeakSmartValue = bounds;
        }
    }
}
