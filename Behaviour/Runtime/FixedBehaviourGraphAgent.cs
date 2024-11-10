using System;
using System.Reflection;
using Unity.Behavior;

namespace UnityJigs.Behaviour
{
    public class FixedBehaviourGraphAgent : BehaviorGraphAgent
    {
        private static readonly Action<BehaviorGraphAgent>? BaseUpdate = typeof(BehaviorGraphAgent)
            .GetMethod(nameof(Update), BindingFlags.Instance | BindingFlags.NonPublic)
            ?.CreateDelegate(typeof(Action<BehaviorGraphAgent>)) as Action<BehaviorGraphAgent>;


        private void FixedUpdate()
        {
            if (BaseUpdate == null) throw new Exception("Cannot find base Update method");
            BaseUpdate(this);
        }

        public void Update()
        {
            // No-Op
        }
    }
}
