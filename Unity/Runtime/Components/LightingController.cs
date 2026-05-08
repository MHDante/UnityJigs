using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace UnityJigs.Components
{
    [ExecuteAlways]
    public class LightingController : MonoBehaviour
    {
        public AmbientMode Mode = AmbientMode.Flat;

        [ShowIf(nameof(Mode), AmbientMode.Skybox)]
        public Material? Skybox;

        [ShowIf(nameof(Mode), AmbientMode.Trilight)]
        [ColorUsage(false, true)] public Color SkyColor = new Color(0.212f, 0.227f, 0.259f);
        [ShowIf(nameof(Mode), AmbientMode.Trilight)]
        [ColorUsage(false, true)] public Color EquatorColor = new Color(0.114f, 0.125f, 0.133f);
        [ShowIf(nameof(Mode), AmbientMode.Trilight)]
        [ColorUsage(false, true)] public Color GroundColor = new Color(0.047f, 0.043f, 0.035f);

        [ShowIf(nameof(Mode), AmbientMode.Flat)]
        [FormerlySerializedAs("ambientLight")]
        [ColorUsage(false, true)] public Color AmbientLight = new Color(0.212f, 0.227f, 0.259f);

        [Range(0f, 8f)] public float Intensity = 1f;

        private void Update()
        {
            RenderSettings.ambientMode = Mode;
            RenderSettings.ambientIntensity = Intensity;

            switch (Mode)
            {
                case AmbientMode.Skybox:
                    if (Skybox != null) RenderSettings.skybox = Skybox;
                    break;
                case AmbientMode.Trilight:
                    RenderSettings.ambientSkyColor = SkyColor;
                    RenderSettings.ambientEquatorColor = EquatorColor;
                    RenderSettings.ambientGroundColor = GroundColor;
                    break;
                case AmbientMode.Flat:
                    RenderSettings.ambientLight = AmbientLight;
                    break;
            }
        }
    }
}
