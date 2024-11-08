using System;
using Sirenix.OdinInspector;

namespace UnityUtils.Attributes
{
    [IncludeMyAttributes]
    [ListDrawerSettings(OnTitleBarGUI = "@AutoPopulateDrawer.DrawButton($property)")]
    public class AutoPopulateAttribute : Attribute
    {
        public string Folder { get; }
        public AutoPopulateAttribute(string folder) => Folder = folder;

    }
}
