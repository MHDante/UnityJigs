using System.Collections.Generic;
using FMOD.Studio;
using FMODUnity;
using UnityEditor;

namespace UnityJigs.Fmod.Editor
{
    public static class FmodEditorUtils
    {
        private static readonly Dictionary<string, float> ParamValues = new();

        public static EventInstance PlayEditorSound(EventReference ev)
        {
            if (!EditorUtils.PreviewBanksLoaded) EditorUtils.LoadPreviewBanks();

            var editorEventRef = ResolveEventRef(ev);
            if (editorEventRef == null) return default;
            var eventInstance = EditorUtils.PreviewEvent(editorEventRef, ParamValues);
            return eventInstance;
        }

        // Resolve by GUID (FMOD's canonical key) first; ev.Path is an editor cache that can be
        // empty/stale, which made EventFromPath return null and NRE inside EditorUtils.PreviewEvent.
        private static EditorEventRef? ResolveEventRef(EventReference ev)
        {
            var byGuid = EventManager.EventFromGUID(ev.Guid);
            if (byGuid != null) return byGuid;
            return string.IsNullOrEmpty(ev.Path) ? null : EventManager.EventFromPath(ev.Path);
        }

        public static EventInstance CreatePreviewInstance(EventReference ev)
        {
            if (!EditorUtils.PreviewBanksLoaded) EditorUtils.LoadPreviewBanks();

            var editorEventRef = ResolveEventRef(ev);
            if (editorEventRef == null) return default;
            var eventInstance = CreatePreviewInstance(editorEventRef, ParamValues);
            return eventInstance;
        }

        public static EventDescription GetEditorDescription(EventReference ev)
        {
            if (!EditorUtils.PreviewBanksLoaded) EditorUtils.LoadPreviewBanks();

            System.getEventByID(ev.Guid, out var eventDescription);
            return eventDescription;
        }

        public static EventInstance CreatePreviewInstance(EditorEventRef eventRef, Dictionary<string, float> previewParamValues, float volume = 1, float startTime = 0.0f)
        {
            CheckResult(System.getEventByID(eventRef.Guid, out var eventDescription));
            CheckResult(eventDescription.createInstance(out var eventInstance));

            foreach (var param in eventRef.Parameters)
            {
                CheckResult(param.IsGlobal ? System.getParameterDescriptionByName(param.Name, out var paramDesc) :
                    eventDescription.getParameterDescriptionByName(param.Name, out paramDesc));

                var value = previewParamValues.TryGetValue(param.Name, out var paramValue) ? paramValue : param.Default;
                param.ID = paramDesc.id;

                CheckResult(param.IsGlobal ? System.setParameterByID(param.ID, value) :
                    eventInstance.setParameterByID(param.ID, value));
            }

            CheckResult(eventInstance.setVolume(volume));
            CheckResult(eventInstance.setTimelinePosition((int)(startTime * 1000.0f)));

            return eventInstance;
        }

        public static void CheckResult(FMOD.RESULT result)=> EditorUtils.CheckResult(result);
        public static FMOD.Studio.System System => EditorUtils.System;

        // ---------------------------------------------------------------- buses
        // FMOD's editor cache indexes events / banks / parameters but NOT buses, so the only editor-side source
        // of bus paths is the preview system: load the built banks and walk each bank's bus list.

        private static readonly List<string> BusPathsCache = new();
        private static double _busPathsCacheTime = double.NegativeInfinity;
        private const double BusPathsCacheSeconds = 2.0;

        /// <summary>
        /// Every bus path in the built banks, master ("bus:/") first, then sorted. Empty when nothing could be
        /// enumerated (no banks built, preview system failed) — callers must treat empty as "unknown", not
        /// "no buses". <paramref name="reloadBanks"/> drops and re-loads the preview banks first so a fresh
        /// bank build is reflected (stops any playing previews).
        /// </summary>
        public static List<string> GetBusPaths(bool reloadBanks = false)
        {
            var result = new List<string>();
            try
            {
                if (reloadBanks && EditorUtils.PreviewBanksLoaded)
                {
                    EditorUtils.StopAllPreviews();
                    EditorUtils.UnloadPreviewBanks();
                }
                if (!EditorUtils.PreviewBanksLoaded) EditorUtils.LoadPreviewBanks();

                if (System.getBankList(out var banks) != FMOD.RESULT.OK) return result;
                foreach (var bank in banks)
                {
                    if (bank.getBusList(out var buses) != FMOD.RESULT.OK) continue;
                    foreach (var bus in buses)
                    {
                        if (bus.getPath(out var path) != FMOD.RESULT.OK || string.IsNullOrEmpty(path)) continue;
                        if (!result.Contains(path)) result.Add(path);
                    }
                }
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogWarning($"FmodEditorUtils.GetBusPaths: could not enumerate buses — {e.Message}");
                result.Clear();
                return result;
            }

            result.Sort(string.CompareOrdinal);
            var master = result.IndexOf(FmodBus.MasterPath);
            if (master > 0)
            {
                result.RemoveAt(master);
                result.Insert(0, FmodBus.MasterPath);
            }
            return result;
        }

        /// <summary> GetBusPaths with a short cache, for per-repaint callers (drawers). </summary>
        public static List<string> GetBusPathsCached()
        {
            var now = EditorApplication.timeSinceStartup;
            if (now - _busPathsCacheTime < BusPathsCacheSeconds) return BusPathsCache;
            BusPathsCache.Clear();
            BusPathsCache.AddRange(GetBusPaths());
            _busPathsCacheTime = now;
            return BusPathsCache;
        }
    }
}
