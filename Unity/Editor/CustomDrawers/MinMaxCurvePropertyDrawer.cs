using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityJigs.Editor.Odin;

namespace UnityJigs.Editor.CustomDrawers
{
    public class MinMaxCurveOdinPropertyDrawer : OdinValueDrawer<ParticleSystem.MinMaxCurve>
    {
        public MinMaxCurvePropertyDrawer Drawer { get; set; } = null!;

        protected override void Initialize()
        {
            base.Initialize();
            Drawer = new MinMaxCurvePropertyDrawer();
        }

        protected override void DrawPropertyLayout(GUIContent label)
        {
            var serializedProp = Property.ToSerializedProperty();
            var height = Drawer.GetPropertyHeight(serializedProp, label);
            var rect = EditorGUILayout.GetControlRect(true, height);
            Drawer.OnGUI(rect, serializedProp, label);
        }
    }
}
