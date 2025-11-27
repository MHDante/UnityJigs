using FMODUnity;
using UnityEngine;
using UnityJigs.Extensions;

namespace UnityJigs.Fmod
{
    public class AudioCollisionEmitter : MonoBehaviour
    {
        public LayerMask CollisionMask ;
        public EventReference EventRef;
        [FmodEvent(nameof(EventRef))]
        public FmodParam VelocityParam;

        private void OnCollisionEnter(Collision other)
        {
            if (!CollisionMask.Includes(other.collider)) return;
            var maybeInstance = EventRef.Play(gameObject);
            if(maybeInstance == null) return;
            var instance = maybeInstance.Value;
            VelocityParam.SetOn(instance,other.relativeVelocity.magnitude );
            instance.start();
        }
    }
}
