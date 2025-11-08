using System.Collections.Generic;
using FMOD.Studio;
using FMODUnity;

namespace UnityJigs.Fmod.Editor
{
    public static class FmodEditorUtils
    {
        private static readonly Dictionary<string, float> ParamValues = new();

        public static EventInstance PlayEditorSound(EventReference ev)
        {
            if (!EditorUtils.PreviewBanksLoaded) EditorUtils.LoadPreviewBanks();

            var editorEventRef = EventManager.EventFromPath(ev.Path);
            var eventInstance = EditorUtils.PreviewEvent(editorEventRef, ParamValues);
            return eventInstance;
        }

        public static EventInstance CreatePreviewInstance(EventReference ev)
        {
            if (!EditorUtils.PreviewBanksLoaded) EditorUtils.LoadPreviewBanks();

            var editorEventRef = EventManager.EventFromPath(ev.Path);
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
    }
}
