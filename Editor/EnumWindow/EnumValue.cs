using System;
using JetBrains.Annotations;

namespace UnityUtils.Editor.EnumWindow
{
    [Serializable, UsedImplicitly(ImplicitUseTargetFlags.Members)]
    internal struct EnumValue
    {
        public string Name;
        public int Value;

        public EnumValue(Enum e)
        {
            Name = Enum.GetName(e.GetType(), e) ?? e.ToString();
            Value = Convert.ToInt32(e);
        }
    }
}
