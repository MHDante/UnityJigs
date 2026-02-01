using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityJigs.Extensions;

namespace UnityJigs.Types
{
    /// <summary>
    /// Displays the git build version on a TextMeshProUGUI component.
    /// Automatically updates the text on Start.
    /// </summary>
    [RequireComponent(typeof(TextMeshProUGUI))]
    public class BuildIdDisplay : MonoBehaviour
    {
        [FormerlySerializedAs("prefix")] [SerializeField]
        private string Prefix = "Build: ";

        [FormerlySerializedAs("suffix")] [SerializeField]
        private string Suffix = "";

        private TextMeshProUGUI? _text;
        private TextMeshProUGUI Text => this.GetComponentCached(ref _text);

        private void Start() => UpdateBuildId();

        [Button]
        public void UpdateBuildId() => Text.text = $"{Prefix}{GitVersionInfo.Instance.UpdateBuildId()}{Suffix}";
    }
}