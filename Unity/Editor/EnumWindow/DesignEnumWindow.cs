using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityJigs.Attributes;

namespace UnityJigs.Editor.EnumWindow
{
    [UsedImplicitly(ImplicitUseTargetFlags.Members)]
    public class DesignEnumWindow : OdinMenuEditorWindow
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

        [MenuItem("Pleasure Circuit/Enums")]
        private static void OpenWindow() => GetWindow<DesignEnumWindow>();
    }
}
