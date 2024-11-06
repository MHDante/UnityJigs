// ReSharper disable once CheckNamespace

namespace System.Diagnostics.CodeAnalysis
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property, AllowMultiple = true, Inherited = false)]
    public sealed class MemberNotNullAttribute : Attribute
    {
        public MemberNotNullAttribute(string member) => Members = new[]{member};
        public MemberNotNullAttribute(string[] members) => Members = members;
        public string[] Members { get; set; }
    }

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property, AllowMultiple = true, Inherited = false)]
    public sealed class MemberNotNullWhenAttribute : Attribute
    {
        public MemberNotNullWhenAttribute(bool returnValue, string member) =>
            (Members, ReturnValue) = (new[]{member}, returnValue);

        public MemberNotNullWhenAttribute(bool returnValue, string[] members) =>
            (Members, ReturnValue) = (members, returnValue);

        public string[] Members { get; }
        public bool ReturnValue { get; }
    }
}
