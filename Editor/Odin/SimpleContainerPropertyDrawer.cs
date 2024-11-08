using JetBrains.Annotations;
using MHDante.UnityUtils.Attributes.Odin;
using Sirenix.OdinInspector.Editor;
using UnityEngine;

namespace MHDante.UnityUtils.Editor.Odin
{
    [UsedImplicitly]
    public class SimpleContainerPropertyDrawer : OdinAttributeDrawer<SimpleContainerAttribute>
    {
        protected override void DrawPropertyLayout(GUIContent label) => Property.Children[0].Draw(label);
    }


}
