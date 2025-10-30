// FmodParameterValuePickerAttribute.cs
using System;

namespace UnityJigs.Fmod
{
    /// <summary>
    /// Use on a float field to draw just the FMOD parameter value control.
    /// Resolves a nearby FmodParameterRef and (optionally) an EventReference on the same object.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class FmodParameterValuePickerAttribute : Attribute
    {
        public string ParamRefMember { get; }

        /// <param name="paramRefMember">Member path to a FmodParameterRef on the same object.</param>
        public FmodParameterValuePickerAttribute(string paramRefMember)
        {
            ParamRefMember = paramRefMember;
        }
    }
}
