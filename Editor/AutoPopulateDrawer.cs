using System;
using System.Collections;
using System.Collections.Generic;
using JetBrains.Annotations;
using Sirenix.OdinInspector.Editor;
using Sirenix.OdinInspector.Editor.ValueResolvers;
using Sirenix.Utilities.Editor;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MHDante.UnityUtils.Editor
{
    [UsedImplicitly]
    public class AutoPopulateDrawer
    {
        [UsedImplicitly]
        public static void DrawButton(object obj)
        {
            if (obj is not InspectorProperty prop)
                throw new Exception("What are you doing?");
            var content = EditorIcons.Refresh.ActiveGUIContent;
            content.tooltip = "Re-populate List";
            if (SirenixEditorGUI.ToolbarButton(content)) Execute(prop);
        }

        private static void AutoPopulate(IList? list, Type listType, string? folder)
        {
            if (listType.GetGenericTypeDefinition() != typeof(List<>))
                throw new ArgumentException("Can Only use List<T> with AutoPopulate");

            if (list == null) list = (IList)Activator.CreateInstance(listType);
            else if (list.GetType() != listType) throw new ArrayTypeMismatchException();

            var type = listType.GenericTypeArguments[0];

            string searchTerm;
            if (type.IsSubclassOf(typeof(MonoBehaviour)))
            {
                var tempObj = new GameObject();
                var t = tempObj.AddComponent(type);
                var monoScript = MonoScript.FromMonoBehaviour(t as MonoBehaviour);
                Object.DestroyImmediate(tempObj);

                var scriptPath = AssetDatabase.GetAssetPath(monoScript);
                searchTerm = $"t:Prefab ref:{scriptPath}";
            }
            else if (type.IsSubclassOf(typeof(ScriptableObject)))
            {
                searchTerm = $"t:{type.Name}";
            }
            else
            {
                throw new NotSupportedException("Can Only auto-populate MonoBehaviours and scriptable objects");
            }


            var guids = string.IsNullOrEmpty(folder)
                ? AssetDatabase.FindAssets(searchTerm)
                : AssetDatabase.FindAssets(searchTerm, new[] { folder });

            list.Clear();
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath(path, type);
                if (asset) list.Add(asset);
            }
        }

        private static void Execute(InspectorProperty prop)
        {
            var attr = prop.GetAttribute<AutoPopulateAttribute>();
            var val = prop.ValueEntry.WeakSmartValue as IList;
            var folderExpr = attr.Folder;
            string? folderVal = null;

            if (!string.IsNullOrEmpty(folderExpr))
            {
                var resolver = ValueResolver.GetForString(prop, folderExpr);
                folderVal = resolver.GetValue();
            }

            AutoPopulate(val, prop.Info.TypeOfValue, folderVal);
        }
    }
}
