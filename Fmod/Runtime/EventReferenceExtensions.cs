using UnityEngine;
using FMODUnity;
using FMOD.Studio;

namespace UnityJigs.Fmod
{
    /// <summary>
    /// Forwarders from EventReference to RuntimeManager.
    /// </summary>
    public static class EventReferenceExtensions
    {
        public static EventInstance CreateInstance(this EventReference reference)
            => RuntimeManager.CreateInstance(reference);

        public static void PlayOneShot(this EventReference reference)
            => RuntimeManager.PlayOneShot(reference);

        public static void PlayOneShot(this EventReference reference, Vector3 position)
            => RuntimeManager.PlayOneShot(reference, position);

        public static void PlayOneShotAttached(this EventReference reference, GameObject gameObject)
            => RuntimeManager.PlayOneShotAttached(reference, gameObject);

        public static EventDescription GetDescription(this EventReference reference)
            => RuntimeManager.GetEventDescription(reference);

        public static void PlayInEditor()
        {
        }
    }

    /// <summary>
    /// Forwarders from EventInstance to RuntimeManager.
    /// </summary>
    public static class EventInstanceExtensions
    {
        public static void AttachTo(this EventInstance instance, Rigidbody rb)
            => RuntimeManager.AttachInstanceToGameObject(instance, rb.gameObject, rb);

        public static void AttachTo(this EventInstance instance, GameObject gameObject)
            => RuntimeManager.AttachInstanceToGameObject(instance, gameObject );

        public static void Detach(this EventInstance instance)
            => RuntimeManager.DetachInstanceFromGameObject(instance);
    }
}
