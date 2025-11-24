using System;
using System.Collections.Generic;
using Object = UnityEngine.Object;

public interface IExtensibleEnum<TKey, TPayload> where TKey : IEquatable<TKey>
{
    IEnumerable<TKey> GetKeys();
    TKey Key { get; set; }
    TPayload Value { get; }
    string GetLabel(TKey? key);
    bool TryAddValue(TKey key, TPayload? payload);
    bool TryGetValidNewKey(out TKey key, out bool isEditable);
    bool CanEdit => true;
    Object RecordableObject { get; }

}




public static class IExtensibleEnumExtensions
{
    public static bool GetNewIntKey<TValue>(this IExtensibleEnum<int, TValue> e, out int key, out bool isEditable)
    {
        var max = 0;
        foreach (var k in e.GetKeys())
            max = Math.Max(max, k);
        key = max + 1;
        isEditable = false;
        return true;
    }

    public static void SelectItem<TKey, TValue>(this IExtensibleEnum<TKey, TValue> e) where TKey : IEquatable<TKey>
    {
#if UNITY_EDITOR
        UnityEditor.Selection.objects = new[] { e.RecordableObject };
#endif
    }


    public static void ApplyChanges<TKey, TValue>(this IExtensibleEnum<TKey, TValue> e) where TKey : IEquatable<TKey>
    {
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(e.RecordableObject);
#endif
    }
}
