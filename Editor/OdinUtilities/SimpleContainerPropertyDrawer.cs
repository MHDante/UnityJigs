using JetBrains.Annotations;
using Sirenix.OdinInspector.Editor;
using UnityEngine;

namespace MHDante.UnityUtils.Editor.OdinUtilities
{
    [UsedImplicitly]
    public class SimpleContainerPropertyDrawer : OdinAttributeDrawer<SimpleContainerAttribute>
    {
        protected override void DrawPropertyLayout(GUIContent label) => Property.Children[0].Draw(label);
    }


}
