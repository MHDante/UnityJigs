using System;

namespace UnityJigs.Fmod
{
    /// <summary>
    /// Attribute to tell the drawer how to find the EventReference on the same component.
    /// Pass the field or property name (default: "EventReference").
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class FmodEventAttribute : Attribute
    {
        public string EventRefMember { get; }

        public FmodEventAttribute(string eventRefMember) => EventRefMember = eventRefMember;
    }
}