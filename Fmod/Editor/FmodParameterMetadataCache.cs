using System;
using System.Collections.Generic;
using FMODUnity;

namespace UnityJigs.Fmod.Editor
{
    /// <summary>
    /// Small editor-time cache that fetches parameter names, ranges, and labels.
    /// Uses the Studio API via FmodEditorUtils (works in play mode if banks are loaded).
    /// </summary>
    internal static class FmodParameterMetadataCache
    {
        private static readonly Dictionary<Guid, List<FmodParameterMeta>> Cache = new();

        public static List<FmodParameterMeta> GetParameters(EventReference evtRef)
        {
            if (evtRef.IsNull) return Empty;

            if (Cache.TryGetValue(evtRef.Guid, out var list) && list != null && list.Count > 0)
                return list;

            list = TryFromStudioAPI(evtRef);

            if (list.Count == 0) list = Empty;

            Cache[evtRef.Guid] = list;
            return list;
        }

        private static List<FmodParameterMeta> TryFromStudioAPI(EventReference evtRef)
        {
            var list = new List<FmodParameterMeta>();
            var desc = FmodEditorUtils.GetEditorDescription(evtRef);
            if (!desc.hasHandle()) return list;

            desc.getParameterDescriptionCount(out var count);
            for (var i = 0; i < count; i++)
            {
                desc.getParameterDescriptionByIndex(i, out var pd);

                var labels = new List<string>();
                if ((pd.flags & FMOD.Studio.PARAMETER_FLAGS.LABELED) != 0)
                {
                    var labelCount = pd.maximum + 1;
                    for (var li = 0; li < labelCount; li++)
                    {
                        desc.getParameterLabelByID(pd.id, li, out var label);
                        labels.Add(label);
                    }
                }

                list.Add(new FmodParameterMeta
                {
                    Name      = pd.name,
                    Min       = pd.minimum,
                    Max       = pd.maximum,
                    Default   = pd.defaultvalue,
                    HasRange  = pd.maximum > pd.minimum,
                    IsLabeled = labels.Count > 0,
                    Labels    = labels.ToArray()
                });
            }

            return list;
        }

        private static readonly List<FmodParameterMeta> Empty = new()
        {
            new FmodParameterMeta { Name = "<no parameters>", Min = 0, Max = 1, Default = 0, HasRange = true }
        };
    }
}