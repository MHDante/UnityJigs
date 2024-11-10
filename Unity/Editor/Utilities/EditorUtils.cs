using System.Collections.Generic;
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


        public static List<T> FindAllAssetsOfType<T>() where T : UnityEngine.Object
        {
            List<T> assets = new List<T>();

            // Get all asset GUIDs for the specified type
            string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");

            // Iterate through each GUID and load the asset
            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                T asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
                if (asset != null)
                {
                    assets.Add(asset);
                }
            }

            return assets;
        }
    }
}
