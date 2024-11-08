using System;
using UnityEditorInternal;
using UnityEngine;

namespace MHDante.UnityUtils.Editor.AssemblyPeeker
{
    [Serializable]
    public class PeekedAssembly
    {
        public AssemblyDefinitionAsset? Asmdef;
        public AssemblyDefinitionAsset?[] PeekerAsmdefs = { };
        [Delayed] public string[] PeekerAssemblyNames = { };
    }
}
