using System;
using System.Collections.Generic;
using System.Linq;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using Sirenix.Utilities;
using Sirenix.Utilities.Editor;
using UnityEditor;
#endif

namespace UnityJigs.Types
{
    [Serializable]
    [InlineProperty]
    [HideReferenceObjectPicker]
    public class SceneReference : ISerializationCallbackReceiver
    {
        [HideInInspector, SerializeField] private string ScenePath = string.Empty;
        [HideInInspector, SerializeField] private string SceneGuid = string.Empty;
        public string Path => ScenePath;

        public void OnBeforeSerialize() => UpdatePath();
        public void OnAfterDeserialize() => DeferredUpdatePath();

#if !UNITY_EDITOR
        private void UpdatePath() { }
        private void DeferredUpdatePath() { }
    }
}
#else

        private static readonly Queue<SceneReference> PendingReferences = new();

        static SceneReference()
        {
            EditorApplication.update += () =>
            {
                while (PendingReferences.Count != 0)
                {
                    var sceneRef = PendingReferences.Dequeue();
                    sceneRef.UpdatePath();
                }
            };
        }
        public void DeferredUpdatePath() => PendingReferences.Enqueue(this);

        private void UpdatePath() => ScenePath = AssetDatabase.GUIDToAssetPath(SceneGuid);

        [ShowInInspector ,ValueDropdown(nameof(GetSceneAssets))][ CustomValueDrawer(nameof(DrawSceneReference)), OnValueChanged(nameof(UpdateAsset))]
        private SceneAsset? _scene;

        private bool IsInBuild => SceneUtility.GetBuildIndexByScenePath(ScenePath) >= 0;

        private IEnumerable<ValueDropdownItem> GetSceneAssets()
        {
            return AssetDatabase.FindAssets("t:SceneAsset")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<SceneAsset>)
                .Select(scene => new ValueDropdownItem(scene.name, scene));
        }

        private void UpdateAsset()
        {
            ScenePath = AssetDatabase.GetAssetPath(_scene);
            SceneGuid = AssetDatabase.GUIDFromAssetPath(ScenePath).ToString();
            _scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
        }

        private SceneAsset? DrawSceneReference(Func<GUIContent, bool> callNextDrawer)
        {
            if(_scene == null) _scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);

            EditorGUILayout.BeginHorizontal();

            GUILayout.Space(6f);

            callNextDrawer(GUIContent.none);

            DrawTooltip(GUILayoutUtility.GetLastRect(), ScenePath);

            var (iconContent, iconTooltip) = !ScenePath.IsNullOrWhitespace() ? IsInBuild ?
                    ("greenLight", "Included In Build") :
                    ("orangeLight", "Not Included In Build") :
                ("redLight", "Invalid Scene Asset");

            GUILayout.Label(EditorGUIUtility.IconContent(iconContent), GUILayout.Width(18f), GUILayout.Height(18f));
            DrawTooltip(GUILayoutUtility.GetLastRect(), iconTooltip);

            EditorGUILayout.EndHorizontal();

            return _scene;
        }

        private void DrawTooltip(Rect rect, string tooltip)
        {
            if (!rect.Contains(Event.current.mousePosition)) return;
            var tooltipContent = GUIHelper.TempContent(string.Empty, tooltip);
            var tooltipSize = GUI.skin.label.CalcSize(tooltipContent);
            var tooltipRect = new Rect(Event.current.mousePosition, tooltipSize);
            EditorGUI.LabelField(tooltipRect, tooltipContent);
        }
    }
}

#endif
