using System;

namespace UnityJigs.Attributes
{
    [AttributeUsage(AttributeTargets.Assembly)]
    public class PeekAssemblyAttribute : Attribute
    {
        public readonly string AssemblyName;
        public PeekAssemblyAttribute(string assemblyName) => AssemblyName = assemblyName;
    }
}
