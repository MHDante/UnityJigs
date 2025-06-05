using System.Collections.Generic;
using UnityEngine;

namespace UnityJigs.Extensions
{
    public static class LayerUtils
    {
        private static readonly Dictionary<string, int> NameLayerDict = new();
        private static readonly Dictionary<int, string> LayerNameDict = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        public static void UpdateLayers()
        {
            for (int i = 0; i < 32; i++)
            {
                var name = LayerMask.LayerToName(i);
                LayerNameDict[i] = name;
                if(string.IsNullOrWhiteSpace(name)) continue;
                NameLayerDict[name] = i;
            }
        }

        public static string LayerToName(int layer) => LayerNameDict.TryGetValue(layer, out var n) ? n : string.Empty;
        public static int? NameToLayer(string name) => NameLayerDict.TryGetValue(name, out var l) ? l : null;
    }
}
