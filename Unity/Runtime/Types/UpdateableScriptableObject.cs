using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityJigs.Types
{
    /// <summary>
    /// Base class for ScriptableObjects that can receive runtime update and lifecycle events.
    /// Ensures only one global updater is created at runtime.
    /// </summary>
    public abstract class UpdatableScriptableObject : ScriptableObject
    {
        private static readonly List<UpdatableScriptableObject> Instances = new();
        private static Updater? _Updater;

        /// <summary>
        /// Whether this ScriptableObject should currently receive update callbacks.
        /// </summary>
        protected abstract bool IsActive { get; }

        #region Lifecycle Hooks
        protected virtual void OnUpdate() { }
        protected virtual void OnFixedUpdate() { }
        protected virtual void OnLateUpdate() { }
        protected virtual void OnApplicationPause(bool isPaused) { }
        protected virtual void OnApplicationQuit() { }
        protected virtual void OnSceneLoaded(Scene scene, LoadSceneMode mode) { }
        #endregion

        protected virtual void OnEnable()
        {
            if (!Instances.Contains(this))
                Instances.Add(this);

        }

        protected virtual void OnDisable()
        {
            if (!Application.isPlaying)
                return;

            Instances.Remove(this);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EnsureUpdaterExists()
        {
            if (_Updater != null)
                return;

            var go = new GameObject("[UpdatableScriptableObjects]");
            go.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(go);
            _Updater = go.AddComponent<Updater>();
        }

        private sealed class Updater : MonoBehaviour
        {
            private void Awake()
            {
                SceneManager.sceneLoaded += HandleSceneLoaded;
            }

            private void OnDestroy()
            {
                SceneManager.sceneLoaded -= HandleSceneLoaded;
                _Updater = null;
                Instances.Clear();
            }

            private void Update() => Dispatch(static o => o.OnUpdate());
            private void FixedUpdate() => Dispatch(static o => o.OnFixedUpdate());
            private void LateUpdate() => Dispatch(static o => o.OnLateUpdate());
            private void OnApplicationPause(bool paused) => Dispatch(static (o, p) => o.OnApplicationPause(p), paused);
            private void OnApplicationQuit() => Dispatch(static o => o.OnApplicationQuit());
            private static void HandleSceneLoaded(Scene s, LoadSceneMode m)
                => Dispatch(static (o, data) => o.OnSceneLoaded(data.s, data.m), (s, m));

            // --- Core dispatch ---
            private static void Dispatch(Action<UpdatableScriptableObject> call)
            {
                var list = Instances;
                for (int i = 0; i < list.Count; i++)
                {
                    var obj = list[i];
                    if (obj == null || !obj.IsActive)
                        continue;

                    try { call(obj); }
                    catch (Exception e) { Debug.LogException(e, obj); }
                }
            }

            private static void Dispatch<T>(Action<UpdatableScriptableObject, T> call, T arg)
            {
                var list = Instances;
                for (int i = 0; i < list.Count; i++)
                {
                    var obj = list[i];
                    if (obj == null || !obj.IsActive)
                        continue;

                    try { call(obj, arg); }
                    catch (Exception e) { Debug.LogException(e, obj); }
                }
            }
        }
    }
}
