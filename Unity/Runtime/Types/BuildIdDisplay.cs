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
        public TextMeshProUGUI Text => this.GetComponentCached(ref _text);


        private void Start()
        {
            UpdateBuildId();
        }

        /// <summary>
        /// Updates the text display with the current build ID.
        /// Can be called manually if you need to refresh the display.
        /// </summary>
        [Button]
        public void UpdateBuildId()
        {
            string buildId = GitVersionInfo.Instance.GetBuildId();
            Text.text = $"{Prefix}{buildId}{Suffix}";
        }

#if UNITY_EDITOR
        // Update in editor when values change
        private void OnValidate() => UpdateBuildId();
#endif
    }
}