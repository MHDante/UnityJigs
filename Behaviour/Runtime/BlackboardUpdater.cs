using Sirenix.OdinInspector;
using Unity.Behavior;
using UnityEngine;
using UnityJigs.Attributes;


namespace UnityJigs.Behaviour
{
    [DefaultExecutionOrder(-1)]
    public class BlackboardUpdater : MonoBehaviour
    {
        public BlackboardTypes BlackboardSource;

        [ShowIf(nameof(BlackboardSource), BlackboardTypes.Asset)]
        public RuntimeBlackboardAsset? SourceAsset = null!;
        [ShowIf(nameof(BlackboardSource), BlackboardTypes.Agent)]
        public BehaviorGraphAgent? SourceAgent = null!;

        public Blackboard? Blackboard => BlackboardSource switch
        {
            BlackboardTypes.Agent when Application.isPlaying => SourceAgent?.BlackboardReference.Blackboard,
            BlackboardTypes.Agent => null,//SourceAgent?.BlackboardReference.SourceBlackboardAsset.Blackboard,
            BlackboardTypes.Asset => SourceAsset?.Blackboard,
            _ => null,
        };

        public UpdateTypes WriteOn = UpdateTypes.Update | UpdateTypes.OnValidate | UpdateTypes.OnDidApplyAnimationProperties;
        public virtual void Awake() => UpdateIf(UpdateTypes.Awake);
        public void OnValidate() => UpdateIf(UpdateTypes.OnValidate);
        private void FixedUpdate() => UpdateIf(UpdateTypes.FixedUpdate);
        private void LateUpdate() => UpdateIf(UpdateTypes.LateUpdate);
        private void Update() => UpdateIf(UpdateTypes.Update);
        private void OnDidApplyAnimationProperties() => UpdateIf(UpdateTypes.OnDidApplyAnimationProperties);

        private void UpdateIf(UpdateTypes flagToCheck)
        {
            if (!WriteOn.HasFlagFast(flagToCheck)) return;
            WriteOverrides();
        }

        public virtual void WriteOverrides() { }
    }

    public enum BlackboardTypes
    {
        Asset,
        Agent
    }
}
