using JetBrains.Annotations;
using Sirenix.OdinInspector.Editor;
using Sirenix.OdinInspector.Editor.ActionResolvers;
using UnityEngine;
using UnityUtils.Attributes.Odin;

namespace UnityUtils.Editor.CustomDrawers
{
    [UsedImplicitly, DrawerPriority(DrawerPriorityLevel.WrapperPriority)]
    public class StackedButtonDrawer : OdinAttributeDrawer<StackedButtonAttribute>
    {
        private ActionResolver _resolver = null!;
        protected override void Initialize() => _resolver = ActionResolver.Get(Property, Attribute.Action);

        protected override void DrawPropertyLayout(GUIContent label)
        {
            _resolver.DrawError();
            if (GUILayout.Button(Attribute.Label ?? _resolver.Context.ResolvedString)) InvokeButton();
            CallNextDrawer(label);
        }

        private void InvokeButton()
        {
            Property.RecordForUndo("Click " + Attribute.Label);
            _resolver.DoActionForAllSelectionIndices();
        }
    }
}
