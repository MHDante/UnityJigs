using JetBrains.Annotations;
using Sirenix.OdinInspector.Editor;
using UnityEngine;
using UnityJigs.Attributes.Odin;

namespace UnityJigs.Editor.Odin
{
    [UsedImplicitly]
    public class SimpleContainerPropertyDrawer : OdinAttributeDrawer<SimpleContainerAttribute>
    {
        protected override void DrawPropertyLayout(GUIContent label) => Property.Children[0].Draw(label);
    }


}
