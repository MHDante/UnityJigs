using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Sirenix.OdinInspector;
using UnityEditor;
using UnityEngine;
using UnityJigs.Types;

namespace UnityJigs.Extensions
{
    [CreateAssetMenu(fileName = "KenneyInputPromptParser", menuName = "Jigs/Kenney Input PromptParser")]
    public class InputPromptParser : RuntimeScriptableSingleton<InputPromptParser>
    {
        [InlineProperty]
        public List<InputPromptPlatform> Platforms = new();
    }

    [Serializable]
    public class InputPromptPlatform
    {
        public string PlatformId = String.Empty;

        [TableList]
        public List<ButtonImage> Buttons = new();
        public InputPromptXmlPair XmlFiles;
    }

    [Serializable]
    public class ButtonImage
    {
        public string ImageId = String.Empty;
        [HideInTables] public string PlatformId = String.Empty;
        [PreviewField] public Sprite ImageDefault = null!;
        [PreviewField] public Sprite ImageDouble = null!;
    }

    [Serializable]
    public struct InputPromptXmlPair
    {
        public TextAsset DefaultSpriteXml;
        public TextAsset DoubleSpriteXml;
    }
}
