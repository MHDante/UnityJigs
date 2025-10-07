using System.Collections.Generic;

public interface IExtensibleEnum<TKey, in TPayload>
{
    IEnumerable<TKey> GetKeys();
    TKey Key { get; set; }
    string GetLabel(TKey? key);
    bool TryAddValue(TKey key, TPayload? payload);
    bool TryGetValidNewKey(out TKey key, out bool isEditable);
    bool CanEdit => true;
    void Edit();
    void ApplyChanges();
}
