using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace UnityJigs.Components
{
    [ExecuteInEditMode]
    public class LightingController : MonoBehaviour
    {
        [FormerlySerializedAs("ambientLight")]
        [ColorUsage(false, true)]
        public Color AmbientLight;
    
        private void Update()
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = AmbientLight;
        }
    }
}
