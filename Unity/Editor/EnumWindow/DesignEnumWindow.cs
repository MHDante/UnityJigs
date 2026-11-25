using System.Collections.Generic;
using JetBrains.Annotations;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;
using UnityJigs.Attributes;

namespace UnityJigs.Editor.EnumWindow
{
    // Utils > Enums: every design enum in the project, in folders by namespace, with a search over their names, folders
    // and entries. Selecting one shows what a designer may do with it, and does it (EnumType).
    [UsedImplicitly(ImplicitUseTargetFlags.Members)]
    public class DesignEnumWindow : OdinMenuEditorWindow
    {
        // The selection and the search survive the recompile that every edit causes.
        private const string SelectedKey = "UnityJigs.EnumWindow.Selected";
        private const string SearchKey = "UnityJigs.EnumWindow.Search";

        private string _search = "";
        private List<EnumType>? _all;

        protected override void OnEnable()
        {
            base.OnEnable();
            _search = SessionState.GetString(SearchKey, "");
        }

        protected override OdinMenuTree BuildMenuTree()
        {
            if (_all == null)
            {
                _all = new List<EnumType>();
                foreach (var type in TypeCache.GetTypesWithAttribute<DesignEnumAttribute>())
                    if (type.IsEnum) _all.Add(new EnumType(type));
                _all.Sort((a, b) => string.CompareOrdinal(a.Folder + "/" + a.Label, b.Folder + "/" + b.Label));
            }

            var tree = new OdinMenuTree(false);
            tree.Config.DrawSearchToolbar = false;
            // The menu's own arrow and Return keys would act while the name field has the keyboard.
            tree.Config.AutoHandleKeyboardNavigation = false;
            var searching = _search.Trim().Length > 0;
            foreach (var item in _all)
                if (!searching || item.Matches(_search))
                    tree.Add(item.Folder + "/" + item.Label, item);
            if (searching)
                foreach (var menuItem in tree.EnumerateTree())
                    menuItem.Toggled = true;

            tree.Selection.SelectionChanged += _ =>
            {
                if (tree.Selection.SelectedValue is EnumType selected)
                    SessionState.SetString(SelectedKey, selected.Folder + "/" + selected.Label);
            };
            var last = SessionState.GetString(SelectedKey, "");
            var kept = last.Length > 0 ? tree.GetMenuItem(last) : null;
            if (kept != null) kept.Select();
            else if (searching)
            {
                // The list whose own name matches comes before one that matched by an entry.
                OdinMenuItem? first = null;
                foreach (var menuItem in tree.EnumerateTree())
                    if (menuItem.Value is EnumType e)
                    {
                        first ??= menuItem;
                        if (e.Label.IndexOf(_search.Trim(), System.StringComparison.OrdinalIgnoreCase) >= 0) { first = menuItem; break; }
                    }
                first?.Select();
            }
            return tree;
        }

        protected override void DrawMenu()
        {
            GUILayout.Space(4);
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Space(4);
                var search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField);
                if (search != _search)
                {
                    _search = search;
                    SessionState.SetString(SearchKey, _search);
                    ForceMenuTreeRebuild();
                }
                GUILayout.Space(4);
            }
            if (_search.Trim().Length > 0 && MenuTree != null && MenuTree.MenuItems.Count == 0)
                GUILayout.Label("Nothing matches. Try part of a name, or one of its entries.", EditorStyles.wordWrappedMiniLabel);
            GUILayout.Space(2);
            base.DrawMenu();
        }

        [MenuItem("Utils/Enums")]
        private static void OpenWindow()
        {
            var window = GetWindow<DesignEnumWindow>();
            window.titleContent = new GUIContent("Enums");
            window.minSize = new Vector2(640, 420);
            window.MenuWidth = 240;
            var at = window.position;
            if (at.width < 860 || at.height < 560)
                window.position = new Rect(at.x, at.y, Mathf.Max(at.width, 900), Mathf.Max(at.height, 640));
        }
    }
}
