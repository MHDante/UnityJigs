using System.Collections.Generic;
using System.Linq;
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
                Vector3 parentScale = parent.localScale;
                Quaternion parentRotation = parent.localRotation;
                List<Vector3> positions = new();
                foreach (Transform child in parent)
                {
                    Undo.RecordObject(child, "Freeze transforms" );
                    child.localRotation = parentRotation * child.localRotation;
                    child.localScale = Vector3.Scale(child.localScale, parentScale);
                    positions.Add(child.position);
                }

                parent.localScale = Vector3.one;
                parent.localRotation = Quaternion.identity;
                int i = 0;
                foreach (Transform child in parent)
                {
                    child.position = positions[0];
                    i++;
                    EditorUtility.SetDirty(child);
                }
                EditorUtility.SetDirty(parent);

            }
            Undo.CollapseUndoOperations(group);
        }
    }
}
