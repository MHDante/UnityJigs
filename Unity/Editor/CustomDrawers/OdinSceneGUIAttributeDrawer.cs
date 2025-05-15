using System;
using Sirenix.OdinInspector.Editor;
using UnityEditor;

namespace UnityJigs.Editor.CustomDrawers
{
    public abstract class OdinSceneGUIAttributeDrawer<TAttribute> : OdinAttributeDrawer<TAttribute>, IDisposable
        where TAttribute : Attribute
    {
        protected override void Initialize()
        {
            base.Initialize();
            SceneView.duringSceneGui += OnSceneGUI;
        }

        public void Dispose() => SceneView.duringSceneGui -= OnSceneGUI;

        protected abstract void OnSceneGUI(SceneView sv);
    }
}
