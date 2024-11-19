using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Pool;
using UnityJigs.Extensions;

namespace UnityJigs.Components
{
    public class PrefabPool<T> : MonoBehaviour where T : MonoBehaviour, IPoolable
    {
        [SerializeField, Required] private T Prefab = null!;
        [SerializeField, Required] private Transform CacheParent = null!;

        [Space, Header("Storage")]
        [SerializeField, ReadOnly] protected List<T> LeasedObjects = new();
        [SerializeField, ReadOnly] protected List<T> CachedObjects = new();

        protected virtual void Reset() => CacheParent = CacheParent.SafeNull() ?? transform;
        protected virtual void Awake() => CacheParent = CacheParent.SafeNull() ?? transform;

        public virtual T Make(Transform parent) => Make(parent, null);

        protected T Make(Transform parent, object? activationContext)
        {
            var bubble = CachedObjects.RemoveLastOrDefault();
            if (bubble == null) bubble = Instantiate(Prefab, parent);
            Activate(bubble, activationContext);
            LeasedObjects.Add(bubble);
            return bubble;
        }

        protected virtual void Activate(T obj, object? activationContext)
        {
            obj.transform.localPosition = Vector3.zero;
            obj.Activate();
            obj.gameObject.SetActive(true);
        }

        protected virtual void Update()
        {
            using var __ = ListPool<T>.Get(out var toReturn);
            foreach (var point in LeasedObjects)
                if (point.ShouldReturnToPool || !point)
                    toReturn.Add(point);

            foreach (var expiredPoint in toReturn)
                Return(expiredPoint);
        }

        public virtual void Return(T bubble)
        {
            bubble.Deactivate();
            LeasedObjects.Remove(bubble);

            if (!bubble) return;

            bubble.transform.parent = CacheParent;
            bubble.gameObject.SetActive(false);
            CachedObjects.Add(bubble);
        }
    }

    public interface IPoolable
    {
        bool ShouldReturnToPool { get; }
        void Activate();
        void Deactivate();
    }
}
