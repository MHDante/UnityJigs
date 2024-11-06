using System;
using Sirenix.OdinInspector;

namespace MHDante.UnityUtils
{
    [IncludeMyAttributes]
    [ListDrawerSettings(OnTitleBarGUI = "@AutoPopulateDrawer.DrawButton($property)")]
    public class AutoPopulateAttribute : Attribute
    {
        public string Folder { get; }
        public AutoPopulateAttribute(string folder) => Folder = folder;

    }
}
