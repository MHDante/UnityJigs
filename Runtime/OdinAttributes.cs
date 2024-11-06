using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Sirenix.OdinInspector;

namespace MHDante.UnityUtils
{
    [IncludeMyAttributes, ShowInInspector, ShowIf("@$value != null")]
    public class ShowIfNotNullAttribute : Attribute { }

    [IncludeMyAttributes, ShowInInspector, ShowIf("@!ShowIfNotEmptyAttribute.IsEmpty($value)")]
    public class ShowIfNotEmptyAttribute : Attribute
    {
        public bool IsEmpty(object o)
        {
            switch (o)
            {
                case Array a:
                    return a.Length != 0;
                case ICollection c:
                    return c.Count != 0;
                case ICollection<object> co:
                    return co.Count != 0;
                case IEnumerable e:
                    return e.Cast<object?>().Any();
                default:
                    return false;
            }
        }
    }

    public class SimpleContainerAttribute : Attribute { }

    public class StackedButtonAttribute : Attribute
    {
        public readonly string Action;
        public readonly string? Label;

        public StackedButtonAttribute(string action, string? label = null)
        {
            Action = action;
            Label = label;
        }
    }


    [IncludeMyAttributes]
    [ListDrawerSettings(DraggableItems = true, ShowPaging = false, ShowItemCount = true)]
    public class SyncPaletteAttribute : Attribute
    {
        public SyncPaletteAttribute(string? paletteName = null) => PaletteName = paletteName;
        public string? PaletteName { get; }
    }
}
