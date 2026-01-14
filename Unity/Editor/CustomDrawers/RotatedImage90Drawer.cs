using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.UI;
using UnityEngine.UI;
using UnityJigs.Types;

namespace UnityJigs.Editor.CustomDrawers
{
    [CustomEditor(typeof(RotatedImage90), true)]
    [CanEditMultipleObjects]
    public class RotatedImage90Drawer : ImageEditor
    {
        private static readonly Type ImageType = typeof(Image);

        public override void OnInspectorGUI()
        {
            DrawDerivedSerializedFields();
            base.OnInspectorGUI();
        }

        private void DrawDerivedSerializedFields()
        {
            serializedObject.Update();

            var derivedType = serializedObject.targetObject.GetType();
            if (derivedType == ImageType)
            {
                serializedObject.ApplyModifiedProperties();
                return;
            }

            var anyDrawn = false;

            var iterator = serializedObject.GetIterator();
            var enterChildren = true;

            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;

                if (iterator.depth != 0)
                    continue;

                if (iterator.propertyPath == "m_Script")
                    continue;

                if (!IsDeclaredInImageDescendant(derivedType, iterator))
                    continue;

                if (!anyDrawn)
                {
                    EditorGUILayout.Space(6f);
                    EditorGUILayout.LabelField("Extended", EditorStyles.boldLabel);
                    anyDrawn = true;
                }

                EditorGUILayout.PropertyField(iterator, includeChildren: true);
            }

            if (anyDrawn)
                EditorGUILayout.Space(6f);

            serializedObject.ApplyModifiedProperties();
        }

        private static bool IsDeclaredInImageDescendant(Type derivedType, SerializedProperty property)
        {
            var rootFieldName = GetRootFieldName(property.propertyPath);
            if (string.IsNullOrEmpty(rootFieldName))
                return false;

            var field = FindFieldInHierarchy(derivedType, rootFieldName);
            if (field == null)
                return false;

            var declaringType = field.DeclaringType;
            return declaringType != null
                && declaringType != ImageType
                && declaringType.IsSubclassOf(ImageType);
        }

        private static string GetRootFieldName(string propertyPath)
        {
            // Examples:
            // "Rotation" -> "Rotation"
            // "SomeStruct.Value" -> "SomeStruct"
            // "SomeArray.Array.data[0].Foo" -> "SomeArray"
            var dotIndex = propertyPath.IndexOf('.');
            var bracketIndex = propertyPath.IndexOf('[');

            var end = propertyPath.Length;
            if (dotIndex >= 0)
                end = Math.Min(end, dotIndex);
            if (bracketIndex >= 0)
                end = Math.Min(end, bracketIndex);

            return end <= 0 ? string.Empty : propertyPath.Substring(0, end);
        }

        private static FieldInfo? FindFieldInHierarchy(Type type, string fieldName)
        {
            const BindingFlags flags =
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly;

            var t = type;
            while (t != null)
            {
                var field = t.GetField(fieldName, flags);
                if (field != null)
                    return field;

                t = t.BaseType;
            }

            return null;
        }
    }
}
