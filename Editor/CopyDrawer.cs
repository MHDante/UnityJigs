using System;
using JetBrains.Annotations;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace MHDante.UnityUtils.Editor
{
    [UsedImplicitly]
    public class CopyDrawer<T> : OdinValueDrawer<T>, IDefinesGenericMenuItems
    {
        protected override void DrawPropertyLayout(GUIContent label) => CallNextDrawer(label);

        private static void Copy(object a)
        {
            var self = (InspectorProperty)a;
            UnityClipboardModifier.Copy<T>((T)self.ValueEntry.WeakSmartValue);
        }

        private static void Paste(object a)
        {
            var self = (InspectorProperty)a;
            self.ValueEntry.WeakSmartValue = UnityClipboardModifier.Paste<T>();
        }


        public void PopulateGenericMenu(InspectorProperty property, GenericMenu menu)
        {
            var copyEnabled = ValueEntry.ValueCount == 1;
            if (!copyEnabled) menu.AddDisabledItem(CopyDrawer.CopyLabel);
            else menu.AddItem(CopyDrawer.CopyLabel, false, Copy, property);

            var pasteEnabled = UnityClipboardModifier.IsReadyToPaste<T>() && GUI.enabled;
            if (!pasteEnabled) menu.AddDisabledItem(CopyDrawer.PasteLabel);
            else menu.AddItem(CopyDrawer.PasteLabel, false, Paste, property);
        }

        public override bool CanDrawTypeFilter(Type type) => UnityClipboardModifier.CanCopyPaste(type);
    }


    internal static class CopyDrawer
    {
        public static readonly GUIContent CopyLabel = new("Copy Unity Value");
        public static readonly GUIContent PasteLabel = new("Paste Unity Value");
    }
}
