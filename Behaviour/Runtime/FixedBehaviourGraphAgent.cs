using System;
using System.Reflection;
using Unity.Behavior;

namespace UnityJigs.Behaviour
{
    public class FixedBehaviourGraphAgent : BehaviorGraphAgent
    {
        private void FixedUpdate()
        {
            base.Update();
        }

        public new void Update()
        {
            // No-Op
        }
    }
}
