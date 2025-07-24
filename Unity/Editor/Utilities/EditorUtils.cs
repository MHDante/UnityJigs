using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

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


        public static List<T> FindAllAssetsOfType<T>() where T : Object
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


        [MenuItem("Utils/FreezeTransforms")]
        public static void FreezeTransforms()
        {
            if(Selection.objects.Length == 0) return;

            Undo.SetCurrentGroupName("Freeze transforms");
            int group = Undo.GetCurrentGroup();

            foreach (var obj in Selection.gameObjects)
            {
                var parent = obj.transform;
                Undo.RecordObject(parent, "Freeze transforms" );
                var children = new List<Transform>();
                foreach (Transform child in parent) children.Add(child);
                foreach (var child in children) child.SetParent(null, true);
                
                parent.localScale = Vector3.one;
                parent.localRotation = Quaternion.identity;
                foreach (Transform child in children)
                {
                    child.SetParent(parent, true);
                    EditorUtility.SetDirty(child);
                }
                EditorUtility.SetDirty(parent);

            }
            Undo.CollapseUndoOperations(group);
        }
    }
}
