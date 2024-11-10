using System;
using UnityEditorInternal;
using UnityEngine;

namespace UnityJigs.Editor.AssemblyPeeker
{
    [Serializable]
    public class PeekedAssembly
    {
        public AssemblyDefinitionAsset? Asmdef;
        public AssemblyDefinitionAsset?[] PeekerAsmdefs = { };
        [Delayed] public string[] PeekerAssemblyNames = { };
    }
}
