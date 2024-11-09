using System;
using Sirenix.OdinInspector;

namespace UnityJigs.Attributes.Odin
{
    [IncludeMyAttributes, ShowInInspector, ShowIf("@$value != null")]
    public class ShowIfNotNullAttribute : Attribute { }
}
