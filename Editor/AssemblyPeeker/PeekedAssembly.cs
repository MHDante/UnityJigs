using System;
using UnityEditorInternal;
using UnityEngine;

namespace UnityUtils.Editor.AssemblyPeeker
{
    [Serializable]
    public class PeekedAssembly
    {
        public AssemblyDefinitionAsset? Asmdef;
        public AssemblyDefinitionAsset?[] PeekerAsmdefs = { };
        [Delayed] public string[] PeekerAssemblyNames = { };
    }
}
