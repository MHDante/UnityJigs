using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using Sirenix.OdinInspector;
using Sirenix.Utilities;
using UnityEditorInternal;
using UnityJigs.Attributes;

namespace UnityJigs.Editor.EnumWindow
{
    [Serializable, UsedImplicitly(ImplicitUseTargetFlags.Members)]
    internal class EnumType
    {
        private readonly DesignEnumAttribute _attribute;
        [ReadOnly, HorizontalGroup] public string Name;

        [Button, HorizontalGroup]
        public void Edit() => InternalEditorUtility.OpenFileAtLineExternal(_attribute.File, _attribute.Line);

        [ReadOnly, TableList(AlwaysExpanded = true)]
        public List<EnumValue> Values;

        public EnumType(Type type)
        {
            _attribute = type.GetAttribute<DesignEnumAttribute>();
            Name = type.Name;
            Values = Enum.GetValues(type)
                .Cast<Enum>()
                .Select(value => new EnumValue(value))
                .ToList();
        }
    }
}
