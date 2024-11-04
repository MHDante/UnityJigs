using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MHDante.UnityUtils.Editor
{
    public static class EditorUtils
    {
        [MenuItem("North Shore/Transfer Rock Scale")]
        public static void TransferRockScale()
        {
            var obj = Selection.activeGameObject;
            if (obj == null) return;
            Undo.RecordObjects(new Object[] { obj.transform, obj.transform.parent }, "Transfer Rock Scale");

            var pos = obj.transform.position;
            obj.transform.localPosition = Vector3.zero;
            obj.transform.parent.position = pos;

            var scale = obj.transform.lossyScale;
            obj.transform.parent.rotation = obj.transform.rotation;
            obj.transform.localRotation = Quaternion.identity;
            obj.transform.localScale = Vector3.one;
            obj.transform.parent.localScale = scale;

        }

        [MenuItem("North Shore/TransferDownScale")]
        public static void TransferDownScale()
        {
            var obj = Selection.activeGameObject;
            if (obj == null) return;
            var children = obj.transform.OfType<Transform>().ToArray();
            Undo.RecordObjects(children.Append(obj.transform).Cast<Object>().ToArray(), "TransferDownScale");
            var childPositions = children.Select(it => it.position).ToArray();
            var scale = obj.transform.localScale.x;
            obj.transform.localScale /= scale;
            for (int i = 0; i < children.Length; i++)
            {
                var child = children[i];
                child.position = childPositions[i];
                child.localScale *= scale;
            }
        }


        public static string DrawScriptField(UnityEditor.Editor editor)
        {
            var mScript = editor.serializedObject.FindProperty("m_Script");
            using var x = new EditorGUI.DisabledGroupScope(true);
            EditorGUILayout.PropertyField(mScript);
            return "m_Script";
        }
    }
}
