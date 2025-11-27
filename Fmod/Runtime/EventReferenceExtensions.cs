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

        public static EventInstance? CreateInstanceInRange(this EventReference reference, Vector3 position)
        {
            if (!Settings.Instance.StopEventsOutsideMaxDistance)
                return RuntimeManager.CreateInstance(reference);

            var desc = reference.GetDescription();
            desc.getMinMaxDistance(out _, out var max);
            if (StudioListener.DistanceSquaredToNearestListener(position) > max * max)
                return null;

            return RuntimeManager.CreateInstance(reference);
        }


        public static EventInstance Play(this EventReference reference) => reference.CreateInstance().Start();
        public static EventInstance? Play(this EventReference reference, Vector3 position) =>
            reference.CreateInstanceInRange(position)?.AttachTo(position).Start();

        public static void PlayOneShot(this EventReference reference, Vector3 position) =>
            Play(reference, position)?.release();

        public static EventInstance? Play(this EventReference reference, Rigidbody attached) =>
            reference.CreateInstanceInRange(attached.position)?.AttachTo(attached).Start();

        public static void PlayOneShot(this EventReference reference, Rigidbody attached) =>
            Play(reference, attached)?.release();

        public static EventInstance? Play(this EventReference reference, GameObject attached) =>
            reference.CreateInstanceInRange(attached.transform.position)?.AttachTo(attached).Start();

        public static void PlayOneShot(this EventReference reference, GameObject attached) =>
            Play(reference, attached)?.release();

        private static EventInstance Start(this EventInstance instance)
        {
            instance.start();
            return instance;
        }

        public static EventDescription GetDescription(this EventReference reference)
            => RuntimeManager.GetEventDescription(reference);

        public static void PlayInEditor() { }
    }

    /// <summary>
    /// Forwarders from EventInstance to RuntimeManager.
    /// </summary>
    public static class EventInstanceExtensions
    {
        public static EventInstance AttachTo(this EventInstance instance, Rigidbody rb)
        {
            RuntimeManager.AttachInstanceToGameObject(instance, rb.gameObject, rb);
            return instance;
        }

        public static EventInstance AttachTo(this EventInstance instance, GameObject gameObject)
        {
            RuntimeManager.AttachInstanceToGameObject(instance, gameObject);
            return instance;
        }

        public static EventInstance AttachTo(this EventInstance instance, Vector3 position)
        {
            instance.set3DAttributes(position.To3DAttributes());
            return instance;
        }

        public static EventInstance Detach(this EventInstance instance)
        {
            RuntimeManager.DetachInstanceFromGameObject(instance);
            return instance;
        }
    }
}
