using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityJigs.Components;

namespace UnityJigs.Editor.CustomDrawers
{
    // One row of an AnimatorParameterSetter: the parameter, chosen from the ones its Animator's controller has (any
    // type), then its value in the control that suits the type. The value is the component's own Value field of the
    // row's index (a plain float an animation can key), drawn through its SerializedProperty, so the Animation window
    // records it like any other field.
    [CustomPropertyDrawer(typeof(AnimatorParameterSetter.Entry))]
    public class AnimatorParameterSetterEntryDrawer : PropertyDrawer
    {
        private const string None = "Select a parameter";
        private const float Gap = 4f;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) =>
            EditorGUIUtility.singleLineHeight;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var name = property.FindPropertyRelative(nameof(AnimatorParameterSetter.Entry.Name));
            var id = property.FindPropertyRelative(nameof(AnimatorParameterSetter.Entry.Id));
            var type = property.FindPropertyRelative(nameof(AnimatorParameterSetter.Entry.Type));
            var value = property.serializedObject.FindProperty("Value" + RowOf(property));

            var left = position;
            left.width = Mathf.Floor(position.width * 0.6f) - Gap;
            var right = position;
            right.xMin = left.xMax + Gap;

            var setter = property.serializedObject.targetObject as AnimatorParameterSetter;
            var controller = ControllerOf(setter ? setter!.Animator : null);
            if (controller == null) DrawByHand(left, name, id, type);
            else DrawChoice(left, controller, name, id, type);
            if (value != null) DrawValue(right, (AnimatorControllerParameterType)type.intValue, value);
            else EditorGUI.LabelField(right, $"{AnimatorParameterSetter.MaxRows} rows at most", EditorStyles.miniLabel);
        }

        // The row's index, from its path ("Parameters.Array.data[3]").
        private static int RowOf(SerializedProperty property)
        {
            var path = property.propertyPath;
            var open = path.LastIndexOf('[');
            var close = path.LastIndexOf(']');
            return open >= 0 && close > open && int.TryParse(path.Substring(open + 1, close - open - 1), out var row) ? row : -1;
        }

        // With no controller to read (no Animator yet, or one with no controller): the name typed, and its type.
        private static void DrawByHand(Rect rect, SerializedProperty name, SerializedProperty id, SerializedProperty type)
        {
            var nameRect = rect;
            nameRect.width = Mathf.Floor(rect.width * 0.6f) - Gap;
            var typeRect = rect;
            typeRect.xMin = nameRect.xMax + Gap;

            EditorGUI.BeginChangeCheck();
            var typed = EditorGUI.DelayedTextField(nameRect, name.stringValue);
            if (EditorGUI.EndChangeCheck())
            {
                name.stringValue = typed;
                id.intValue = string.IsNullOrWhiteSpace(typed) ? 0 : Animator.StringToHash(typed);
            }

            EditorGUI.BeginChangeCheck();
            var current = type.intValue == 0 ? AnimatorControllerParameterType.Float : (AnimatorControllerParameterType)type.intValue;
            var picked = (AnimatorControllerParameterType)EditorGUI.EnumPopup(typeRect, current);
            if (EditorGUI.EndChangeCheck() || type.intValue == 0) type.intValue = (int)picked;
        }

        private static void DrawChoice(Rect rect, AnimatorController controller, SerializedProperty name,
            SerializedProperty id, SerializedProperty type)
        {
            var parameters = controller.parameters;
            var current = -1;
            for (var i = 0; i < parameters.Length; i++)
                if (parameters[i].nameHash == id.intValue) current = i;
            var missing = current < 0 && id.intValue != 0;

            // Row 0 is "none", or the name the row holds when the controller no longer has it.
            var labels = new string[parameters.Length + 1];
            labels[0] = missing ? $"{name.stringValue} (not in the controller)" : None;
            for (var i = 0; i < parameters.Length; i++) labels[i + 1] = $"{parameters[i].name}  ({parameters[i].type})";

            var before = GUI.color;
            if (missing) GUI.color = new Color(1f, 0.6f, 0.5f);
            EditorGUI.BeginChangeCheck();
            var picked = EditorGUI.Popup(rect, current + 1, labels);
            var changed = EditorGUI.EndChangeCheck();
            GUI.color = before;

            if (!changed)
            {
                // The controller's parameter changed type since the row was made: the row follows it.
                if (current >= 0 && type.intValue != (int)parameters[current].type) type.intValue = (int)parameters[current].type;
                return;
            }
            if (picked == 0)
            {
                name.stringValue = "";
                id.intValue = 0;
                return;
            }
            var chosen = parameters[picked - 1];
            name.stringValue = chosen.name;
            id.intValue = chosen.nameHash;
            type.intValue = (int)chosen.type;
        }

        private static void DrawValue(Rect rect, AnimatorControllerParameterType type, SerializedProperty value)
        {
            var label = new GUIContent("", "The number an animation keys. Float: as it is. Int: the nearest whole " +
                                           "number. Bool: on above 0.5. Trigger: fires each time it rises past 0.5.");
            EditorGUI.BeginProperty(rect, label, value);
            switch (type)
            {
                case AnimatorControllerParameterType.Int:
                {
                    EditorGUI.BeginChangeCheck();
                    var whole = EditorGUI.IntField(rect, Mathf.RoundToInt(value.floatValue));
                    if (EditorGUI.EndChangeCheck()) value.floatValue = whole;
                    break;
                }
                case AnimatorControllerParameterType.Bool:
                case AnimatorControllerParameterType.Trigger:
                {
                    EditorGUI.BeginChangeCheck();
                    var text = type == AnimatorControllerParameterType.Trigger ? "fires when ticked" : "";
                    var on = EditorGUI.ToggleLeft(rect, text, value.floatValue > 0.5f);
                    if (EditorGUI.EndChangeCheck()) value.floatValue = on ? 1f : 0f;
                    break;
                }
                default:
                    EditorGUI.PropertyField(rect, value, GUIContent.none);
                    break;
            }
            EditorGUI.EndProperty();
        }

        private static AnimatorController? ControllerOf(Animator? animator)
        {
            if (animator == null) return null;
            return animator.runtimeAnimatorController is AnimatorOverrideController over
                ? over.runtimeAnimatorController as AnimatorController
                : animator.runtimeAnimatorController as AnimatorController;
        }
    }
}
