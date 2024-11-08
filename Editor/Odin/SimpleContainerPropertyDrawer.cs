using JetBrains.Annotations;
using Sirenix.OdinInspector.Editor;
using UnityEngine;
using UnityUtils.Attributes.Odin;

namespace UnityUtils.Editor.Odin
{
    [UsedImplicitly]
    public class SimpleContainerPropertyDrawer : OdinAttributeDrawer<SimpleContainerAttribute>
    {
        protected override void DrawPropertyLayout(GUIContent label) => Property.Children[0].Draw(label);
    }


}
