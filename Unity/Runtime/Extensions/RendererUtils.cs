using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace UnityJigs.Extensions
{
    public static class RendererUtils
    {
        public static T? GetRenderFeature<T>() where T : ScriptableRendererFeature
        {
            var pipelineAsset = UniversalRenderPipeline.asset.rendererDataList[0];

            // Search for the render feature of the requested type
            foreach (var feature in pipelineAsset.rendererFeatures)
            {
                if (feature is T tFeature)
                    return tFeature;
            }

            Debug.LogWarning($"Render feature of type {typeof(T).Name} not found.");
            return null;
        }

    }
}
