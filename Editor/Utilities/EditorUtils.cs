using UnityEditor;

namespace UnityJigs.Editor.Utilities
{
    public static class EditorUtils
    {
        public static string DrawScriptField(UnityEditor.Editor editor)
        {
            var mScript = editor.serializedObject.FindProperty("m_Script");
            using var x = new EditorGUI.DisabledGroupScope(true);
            EditorGUILayout.PropertyField(mScript);
            return "m_Script";
        }
    }
}
