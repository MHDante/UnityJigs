using System;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;

namespace UnityJigs.Attributes
{
    [MeansImplicitUse(ImplicitUseTargetFlags.WithMembers)]
    public class DesignEnumAttribute : Attribute
    {
        public string File { get; }
        public int Line { get; }

        public DesignEnumAttribute([CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        {
            File = file;
            Line = line;
        }
    }

}
