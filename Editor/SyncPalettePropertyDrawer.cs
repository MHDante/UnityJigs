using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using Sirenix.OdinInspector.Editor;
using UnityEngine;

namespace MHDante.UnityUtils.Editor
{
    [UsedImplicitly]
    public class SyncPalettePropertyDrawer : OdinAttributeDrawer<SyncPaletteAttribute, List<Color>>
    {
        protected override void DrawPropertyLayout(GUIContent label)
    {
        CallNextDrawer(label);
        var paletteName = Attribute.PaletteName ?? Property.Name;
        paletteName = paletteName.AsSpan().TryRemoveSuffix("Palette").ToString();
        var paletteManager = ColorPaletteManager.Instance;
        var palettes = paletteManager.ColorPalettes;
        var currentColors = ValueEntry.SmartValue;
        var palette = palettes.Find(it => it.Name == paletteName);
        if (palette == null)
        {
            palettes.Add(new ColorPalette { Colors = currentColors.ToList(), Name = paletteName });
            return;
        }

        if (palette.Colors.SequenceEqual(currentColors)) return;
        palette.Colors.Clear();
        palette.Colors.AddRange(currentColors);
        UnityEditor.EditorUtility.SetDirty(paletteManager);
    }
    }
}
