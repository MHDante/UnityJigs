using UnityEditor;
using UnityEngine;
using UnityJigs.Attributes;

namespace UnityJigs.Editor.CustomDrawers
{
    [CustomPropertyDrawer(typeof(LayerAttribute))]
    class LayerAttributeDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            property.intValue = EditorGUI.LayerField(position, label, property.intValue);
        }
    }
}
