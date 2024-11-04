using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities;
using UnityEditor;
using static UnityEditorInternal.InternalEditorUtility;

namespace MHDante.UnityUtils.Editor
{
    [UsedImplicitly(ImplicitUseTargetFlags.Members)]
    public class EnumDrawer : OdinMenuEditorWindow
    {
        private List<EnumType> _types = new();

        protected override void Initialize()
        {
            base.Initialize();
            var types = TypeCache.GetTypesWithAttribute<DesignEnumAttribute>();
            _types = types.Where(it => it.IsEnum).Select(it => new EnumType(it)).ToList();
        }

        protected override OdinMenuTree BuildMenuTree()
        {
            var tree = new OdinMenuTree();
            foreach (var type in _types) tree.Add(type.Name, type);
            return tree;
        }

        [MenuItem("North Shore/Enums")]
        private static void OpenWindow() => GetWindow<EnumDrawer>();
    }

    [Serializable, UsedImplicitly(ImplicitUseTargetFlags.Members)]
    internal class EnumType
    {
        private readonly DesignEnumAttribute _attribute;
        [ReadOnly, HorizontalGroup] public string Name;

        [Button, HorizontalGroup]
        public void Edit() => OpenFileAtLineExternal(_attribute.File, _attribute.Line);

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
