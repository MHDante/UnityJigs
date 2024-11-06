using JetBrains.Annotations;
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities.Editor;
using UnityEngine;

namespace MHDante.UnityUtils.Editor
{
    [UsedImplicitly]
    public class SerializableDictionaryDrawer<TKey, TValue> : OdinValueDrawer<SerializedDict<TKey, TValue>>
    {
        protected override void DrawPropertyLayout(GUIContent label)
        {
            CallNextDrawer(label);
            if (ValueEntry.SmartValue.GetInvalidKey(out var invalidKey))
                SirenixEditorGUI.ErrorMessageBox($"Dict cannot have repeated or null key: {invalidKey}");
        }
    }
}
