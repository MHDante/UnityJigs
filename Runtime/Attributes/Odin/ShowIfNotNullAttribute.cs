using System;
using Sirenix.OdinInspector;

namespace MHDante.UnityUtils.Attributes.Odin
{
    [IncludeMyAttributes, ShowInInspector, ShowIf("@$value != null")]
    public class ShowIfNotNullAttribute : Attribute { }
}
