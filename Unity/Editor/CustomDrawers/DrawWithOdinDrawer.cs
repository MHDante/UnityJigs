using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;
using UnityJigs.Attributes.Odin;

namespace UnityJigs.Editor.CustomDrawers
{
    [CustomPropertyDrawer(typeof(DrawWithOdinAttribute))]
    public class DrawWithOdinDrawer : PropertyDrawer
    {
        private PropertyTree _tree = null!;
        private InspectorProperty _odinProp = null!;
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) => 0;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EnsureTree(property);
            _tree.BeginDraw(true);
            _odinProp.Draw(label);
            _tree.EndDraw();
        }

        private void EnsureTree(SerializedProperty property)
        {
            if (_tree != null!) return;
            var target = property.serializedObject.targetObject;
            _tree = PropertyTree.Create(target);
            _odinProp = _tree.GetPropertyAtPath(property.name);
        }
    }
}
