using System;
using Sirenix.OdinInspector;

namespace UnityUtils.Attributes.Odin
{
    [IncludeMyAttributes, ShowInInspector, ShowIf("@$value != null")]
    public class ShowIfNotNullAttribute : Attribute { }
}
