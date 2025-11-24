using System;
using System.Linq;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities.Editor;
using UnityEditor;
using UnityEngine;
using UnityJigs.Editor.Odin;

public class ExtensibleEnumDrawer<TEnum, TKey, TPayload> : OdinValueDrawer<TEnum>
    where TEnum : IExtensibleEnum<TKey, TPayload> where TKey : IEquatable<TKey>
{
    private enum Mode
    {
        None,
        Selector,
        Add
    }

    private Mode _displayMode = Mode.None;


    protected override void DrawPropertyLayout(GUIContent label)
    {
        var model = ValueEntry.SmartValue;
        try
        {
            SirenixEditorGUI.BeginHorizontalPropertyLayout(label, out var rect);
            var serializedProp = Property.ToSerializedProperty();
            if(serializedProp != null) EditorGUI.BeginProperty(rect, label, serializedProp);

            var buttonRect = GUILayoutUtility.GetRect(GUIContent.none, EditorStyles.popup);
            if (EditorGUI.DropdownButton(buttonRect, new GUIContent(model.GetLabel(model.Key)), FocusType.Keyboard))
                DrawSelector(buttonRect);


            if(serializedProp != null) EditorGUI.EndProperty();
            SirenixEditorGUI.EndHorizontalPropertyLayout();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            SirenixEditorGUI.ErrorMessageBox(e.Message);
            CallNextDrawer(label);
        }
    }

    private void DrawSelector(Rect rect)
    {
        _displayMode = Mode.Selector;
        var model = ValueEntry.SmartValue;
        var selector = new GenericSelector<TKey>(
            "Select Value",
            false,
            getMenuItemName: k => model.GetLabel(k),
            collection: model.GetKeys()
        );

        selector.EnableSingleClickToSelect();

        selector.SelectionConfirmed += selection => SetKey(selection.First());
        var window = selector.ShowInPopup(rect, new Vector2(rect.width, 150));
        window.OnEndGUI += () => DrawFooter(window);
    }

    private void SetKey(TKey key)
    {
        var model = ValueEntry.SmartValue;
        model.Key = key;
        ValueEntry.SmartValue = model;
    }

    void DrawFooter(OdinEditorWindow window)
    {
        var model = ValueEntry.SmartValue;
        SirenixEditorGUI.HorizontalLineSeparator();

        using var _ = new GUILayout.HorizontalScope();

        GUILayout.FlexibleSpace();
        switch (_displayMode)
        {
            case Mode.None: break;
            case Mode.Selector:
                if (GUILayout.Button("➕ Add New...", GUILayout.Width(110)))
                    ShowAddWindow(window);
                break;
            case Mode.Add:
                if (GUILayout.Button("☑️ Add", GUILayout.Width(110)))
                    window.Close();
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        if (model.CanEdit)
        {
            if (GUILayout.Button("✏️ Edit", GUILayout.Width(110)))
            {
                window.Close();
                model.SelectItem();
            }
        }


        GUILayout.FlexibleSpace();
    }

    private void ShowAddWindow(OdinEditorWindow window)
    {
        _displayMode = Mode.Add;
        var model = ValueEntry.SmartValue;
        var didGetKey = model.TryGetValidNewKey(out var newKey, out var isEditable);
        if (!didGetKey) return;

        var shower = new PayloadShower(newKey);
        shower.IsKeyEditable = isEditable;
        var addWin = OdinEditorWindow.InspectObject(window, shower);
        addWin.titleContent = new GUIContent("Add New Value");

        addWin.OnClose += () =>
        {
            var didAdd = model.TryAddValue(shower.Key, shower.Value);
            if (didAdd)
            {
                SetKey(shower.Key);
                model.ApplyChanges();
            }

            _displayMode = Mode.None;
            GUIHelper.RequestRepaint();
        };
    }

    [Serializable]
    public class PayloadShower
    {
        [NonSerialized] public bool IsKeyEditable;
        [EnableIf(nameof(IsKeyEditable))] public TKey Key;
        [Space, InlineProperty, LabelText("")] public TPayload? Value;

        public PayloadShower(TKey key) => Key = key;
    }
}
