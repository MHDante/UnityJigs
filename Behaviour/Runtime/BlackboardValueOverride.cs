using UnityJigs.Attributes;
using UnityJigs.Extensions;



using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using Unity.Behavior;
using Unity.Behavior.GraphFramework;
using UnityEngine;
using UnityEngine.Serialization;


namespace UnityJigs.Behaviour
{
    [Serializable, InlineProperty, HideLabel]
    public struct BlackboardValueOverride<T>
    {
        [HideInInspector] public BlackboardUpdater BlackboardUpdater;
        public Blackboard? Blackboard => BlackboardUpdater.Blackboard;

        public BlackboardVariable<T>? Variable =>
            null;//Blackboard?.GetVariable(Guid, out var v) == true ? v as BlackboardVariable<T> : null;

        public string Name => Variable?.Name.NullIfWhitespace() ?? "MISSING";
        private string Label => IsOverriden ? Name + " (Overriden)" : Name;

        [NonSerialized] public SerializableGUID Guid;

        [FormerlySerializedAs("Override"), HorizontalGroup(15), HideLabel, Tooltip("Override"),
         OnValueChanged(nameof(ClearOverride))]
        public bool IsOverriden;
        [SerializeField, HorizontalGroup, ShowIf(nameof(IsOverriden)), LabelText("@" + nameof(Label))]
        private T? OverrideValue;

        [ShowInInspector, AssetsOnly, HorizontalGroup, ShowIf("@!"+nameof(IsOverriden)), LabelText("@" + nameof(Label))]
        public T? Value
        {
            get => IsOverriden ? OverrideValue : Variable == null ? default : Variable.Value;
            set
            {
                if (IsOverriden)
                {
                    OverrideValue = value;
                    WriteOverride();
                    return;
                }

                if (Variable == null) return;
                Variable.Value = value!;
            }
        }

        private void ClearOverride() => OverrideValue = default;

        public void WriteOverride()
        {
            if (!Application.isPlaying) return;
            if (IsOverriden == false) return;
            if (Variable == null) throw new KeyNotFoundException("Missing Variable with GUID: " + Guid);
            Variable.Value = OverrideValue!;
        }

        public BlackboardValueOverride(BlackboardUpdater updater, string guid)
        {

            Guid = new SerializableGUID(guid);
            BlackboardUpdater = updater;
            OverrideValue = default;
            IsOverriden = false;
        }
    }
}
