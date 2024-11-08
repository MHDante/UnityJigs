using System;
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities;
using Sirenix.Utilities.Editor;
using UnityEditor;
using UnityEngine;

namespace MHDante.UnityUtils.Editor.OdinUtilities
{
    public readonly struct OverrideWrapper : IDisposable
    {
        private static readonly Color OverrideMarginColor = new Color(0.003921569f, 0.6f, 0.9215686f, 0.75f);

        private readonly bool _canDrawValueProperty;
        private readonly InspectorProperty _property;
        private readonly bool _draw = false;
        private readonly bool _overriden = false;
        private readonly bool _childOrCollectionOverriden = false;

        public OverrideWrapper(InspectorProperty property)
        {
            _property = property;
            _canDrawValueProperty = property is { IsTreeRoot: false, SupportsPrefabModifications: true } &&
                                    property.State.Enabled &&
                                    property.State.Visible && property.Tree.PrefabModificationHandler.HasPrefabs &&
                                    GlobalConfig<GeneralDrawerConfig>.Instance.ShowPrefabModifiedValueBar;
            if (!_canDrawValueProperty) return;

            _draw = Event.current.type == EventType.Repaint;

            _childOrCollectionOverriden = false;

            if (!_draw) return;

            var valueOverriden = property.ValueEntry is { ValueChangedFromPrefab: true };
            var childValueOverriden = false;
            var collectionChanged = false;

            if (!valueOverriden)
            {
                collectionChanged = property.ValueEntry != null &&
                                    (property.ValueEntry.ListLengthChangedFromPrefab ||
                                     property.ValueEntry.DictionaryChangedFromPrefab);

                if (!collectionChanged)
                    childValueOverriden = ChildValueOverriden(property);
            }

            _overriden = _childOrCollectionOverriden = childValueOverriden || collectionChanged;

            if (_overriden)
                GUIHelper.BeginLayoutMeasuring();

            if (_childOrCollectionOverriden) GUIHelper.PushIsBoldLabel(true);
        }

        public void Dispose()
        {
            if (!_canDrawValueProperty) return;
            if (_draw && _childOrCollectionOverriden)
                GUIHelper.PopIsBoldLabel();

            if (!_draw || !_overriden)
                return;

            var rect = GUIHelper.EndLayoutMeasuring();

            var partOfCollection = _property.Parent is { ChildResolver: ICollectionResolver };

            if (partOfCollection)
                rect = GUIHelper.GetCurrentLayoutRect();

            GUIHelper.IndentRect(ref rect);

            GUIHelper.PushGUIEnabled(true);

            if (!partOfCollection && _childOrCollectionOverriden)
                rect.height = EditorGUIUtility.singleLineHeight;

            rect.width = 2;
            rect.x -= 2;

            SirenixEditorGUI.DrawSolidRect(rect, OverrideMarginColor);

            GUIHelper.PopGUIEnabled();
        }


        private static bool ChildValueOverriden(InspectorProperty property)
        {
            var children = property.Children;
            var count = children.Count;

            for (var index = 0; index < count; index++)
            {
                var child = children[index];
                var valueEntry = child.ValueEntry;

                if (valueEntry != null)
                {
                    if (valueEntry.ValueChangedFromPrefab) return true;
                    if (valueEntry.ListLengthChangedFromPrefab) return true;
                    if (valueEntry.DictionaryChangedFromPrefab) return true;
                }

                if (ChildValueOverriden(child))
                    return true;
            }

            return false;
        }
    }
}
