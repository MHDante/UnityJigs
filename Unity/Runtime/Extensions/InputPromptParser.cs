using System;
using System.Collections.Generic;
using System.Linq;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Serialization;
using UnityJigs.Types;

namespace UnityJigs.Extensions
{
    [CreateAssetMenu(fileName = "KenneyInputPrompts", menuName = "Jigs/Kenney Input Prompts")]
    public class KenneyInputPrompts : RuntimeScriptableSingleton<KenneyInputPrompts>
    {
        [InlineProperty]
        public List<InputPromptPlatform> Platforms = new();
        public IEnumerable<string> PlatformIds => Platforms.Select(it => it.PlatformId);

        public Sprite? GetSprite(string? platformId, string? buttonId)
        {
            if (platformId == null || buttonId == null) return null;
            foreach (var platform in Platforms)
            {
                if (platform.PlatformId != platformId) continue;
                foreach (var button in platform.Buttons)
                    if (button.ButtonId == buttonId)
                        return button.ImageDouble;
            }

            return null;
        }

        public InputPromptPlatform? GetPlatform(string? platformId)
        {
            if (platformId == null) return null;
            foreach (var inputPromptPlatform in Platforms)
                if (inputPromptPlatform.PlatformId == platformId)
                    return inputPromptPlatform;
            return null;
        }
    }

    [Serializable]
    public class InputPromptPlatform
    {
        public string PlatformId = String.Empty;

        [TableList]
        public List<ButtonImage> Buttons = new();
        public IEnumerable<string> ButtonIds => Buttons.Select(it => it.ButtonId);
    }

    [Serializable]
    public class ButtonImage
    {
        [FormerlySerializedAs("ImageId")] public string ButtonId = String.Empty;
        [HideInTables] public string PlatformId = String.Empty;
        [PreviewField] public Sprite ImageDefault = null!;
        [PreviewField] public Sprite ImageDouble = null!;
    }

    [Serializable, InlineProperty]
    public struct InputPrompt
    {
        private static KenneyInputPrompts Prompts => KenneyInputPrompts.Instance;
        private static IEnumerable<string> PlatformIds => Prompts.PlatformIds;
        private InputPromptPlatform? Platform => Prompts.GetPlatform(PlatformId);
        private IEnumerable<string> ButtonIds => Platform?.ButtonIds ?? Enumerable.Empty<string>();
        private bool HasPlatform => Platform != null;

        [LabelText(""), HorizontalGroup("A"), VerticalGroup("A/B"), ValueDropdown(nameof(PlatformIds)),
         OnValueChanged(nameof(OnValidate))]
        public string? PlatformId ;
        [LabelText(""), HorizontalGroup("A"), VerticalGroup("A/B"), EnableIf(nameof(HasPlatform)),
         ValueDropdown(nameof(ButtonIds))]
        public string? ButtonId ;

        [LabelText(""), HorizontalGroup("A", Width = 70), PreviewField, ShowInInspector]
        public Sprite? Sprite => Prompts.GetSprite(PlatformId, ButtonId);


        private void OnValidate()
        {
            if (!ButtonIds.Contains(ButtonId)) ButtonId = string.Empty;
        }
    }
}
