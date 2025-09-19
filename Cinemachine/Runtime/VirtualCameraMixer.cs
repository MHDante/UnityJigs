using System;
using UnityEngine;
using Cinemachine.Utility;
using System.Collections.Generic;
using Cinemachine;
using UnityJigs.Extensions;

// ReSharper disable InconsistentNaming

namespace UnityJigs.Cinemachine

{
    /// <summary>
    /// CinemachineMixingCamera is a "manager camera" that takes on the state of
    /// the weighted average of the states of its child virtual cameras.
    ///
    /// A fixed number of slots are made available for cameras, rather than a dynamic array.
    /// We do it this way in order to support weight animation from the Timeline.
    /// Timeline cannot animate array elements.
    /// </summary>
    [DisallowMultipleComponent]
    [ExecuteAlways]
    [ExcludeFromPreset]
    [AddComponentMenu("Cinemachine/VirtualCameraMixer")]
    public class VirtualCameraMixer : CinemachineVirtualCameraBase
    {
        [Serializable]
        public struct CamTuple
        {
            public float Weight;
            public CinemachineVirtualCameraBase Camera;
        }
        public List<CamTuple> ChildCams = new();

        private CinemachineVirtualCameraBase[]? m_ChildCameras;
        private Dictionary<CinemachineVirtualCameraBase, int>? m_indexMap;

        /// <summary>Get the weight of the child at an index.</summary>
        /// <param name="index">The child index. Only immediate CinemachineVirtualCameraBase
        /// children are counted.</param>
        /// <returns>The weight of the camera.  Valid only if camera is active and enabled.</returns>
        public float GetWeight(int index) => ChildCams.GetSafe(index).Weight;

        /// <summary>Set the weight of the child at an index.</summary>
        /// <param name="index">The child index. Only immediate CinemachineVirtualCameraBase
        /// children are counted.</param>
        /// <param name="w">The weight to set.  Can be any non-negative number.</param>
        public void SetWeight(int index, float w)
        {
            if (index > ChildCams.Count)
            {
                Debug.LogError("CinemachineMixingCamera: Invalid index: " + index);
                return;
            }

            var tuple = ChildCams[index];
            tuple.Weight = w;
            ChildCams[index] = tuple;
        }

        /// <summary>Get the weight of the child CinemachineVirtualCameraBase.</summary>
        /// <param name="vcam">The child camera.</param>
        /// <returns>The weight of the camera.  Valid only if camera is active and enabled.</returns>
        public float GetWeight(CinemachineVirtualCameraBase vcam)
        {
            ValidateListOfChildren();
            if (m_indexMap.TryGetValue(vcam, out var index))
                return GetWeight(index);
            Debug.LogError("CinemachineMixingCamera: Invalid child: "
                           + ((vcam != null) ? vcam.Name : "(null)"));
            return 0;
        }

        /// <summary>Set the weight of the child CinemachineVirtualCameraBase.</summary>
        /// <param name="vcam">The child camera.</param>
        /// <param name="w">The weight to set.  Can be any non-negative number.</param>
        public void SetWeight(CinemachineVirtualCameraBase vcam, float w)
        {
            ValidateListOfChildren();
            if (m_indexMap.TryGetValue(vcam, out var index))
                SetWeight(index, w);
            else
                Debug.LogError("CinemachineMixingCamera: Invalid child: " + (vcam != null ? vcam.Name : "(null)"));
        }

        /// <summary>Blended camera state</summary>
        private CameraState m_State = CameraState.Default;

        /// <summary>Get the current "best" child virtual camera, which is nominally
        /// the one with the greatest weight.</summary>
        private ICinemachineCamera? LiveChild { get; set; }

        /// <summary>The blended CameraState</summary>
        public override CameraState State => m_State;

        /// <summary>Not used</summary>
        public override Transform? LookAt { get; set; }

        /// <summary>Not used</summary>
        public override Transform? Follow { get; set; }

        /// <summary>This is called to notify the vcam that a target got warped,
        /// so that the vcam can update its internal state to make the camera
        /// also warp seamlessy.</summary>
        /// <param name="target">The object that was warped</param>
        /// <param name="positionDelta">The amount the target's position changed</param>
        public override void OnTargetObjectWarped(Transform target, Vector3 positionDelta)
        {
            ValidateListOfChildren();
            foreach (var vcam in m_ChildCameras)
                vcam.OnTargetObjectWarped(target, positionDelta);
            base.OnTargetObjectWarped(target, positionDelta);
        }

        /// <summary>
        /// Force the virtual camera to assume a given position and orientation
        /// </summary>
        /// <param name="pos">Worldspace pposition to take</param>
        /// <param name="rot">Worldspace orientation to take</param>
        public override void ForceCameraPosition(Vector3 pos, Quaternion rot)
        {
            ValidateListOfChildren();
            foreach (var vcam in m_ChildCameras)
                vcam.ForceCameraPosition(pos, rot);
            base.ForceCameraPosition(pos, rot);
        }

        /// <summary>Makes sure the internal child cache is up to date</summary>
        protected override void OnEnable()
        {
            base.OnEnable();
            InvalidateListOfChildren();
        }

        /// <summary>Makes sure the internal child cache is up to date</summary>
        public void OnTransformChildrenChanged()
        {
            InvalidateListOfChildren();
        }

        /// <summary>Makes sure the weights are non-negative</summary>
        protected override void OnValidate()
        {
            base.OnValidate();
            for (var i = 0; i < MaxCameras; ++i)
                SetWeight(i, Mathf.Max(0, GetWeight(i)));
        }

        /// <summary>Check whether the vcam a live child of this camera.</summary>
        /// <param name="vcam">The Virtual Camera to check</param>
        /// <param name="dominantChildOnly">If truw, will only return true if this vcam is the dominat live child</param>
        /// <returns>True if the vcam is currently actively influencing the state of this vcam</returns>
        public override bool IsLiveChild(ICinemachineCamera vcam, bool dominantChildOnly = false)
        {
            var children = ChildCameras;
            for (var i = 0; i < MaxCameras && i < children.Length; ++i)
                if ((ICinemachineCamera)children[i] == vcam)
                    return GetWeight(i) > UnityVectorExtensions.Epsilon && children[i].isActiveAndEnabled;
            return false;
        }


        /// <summary>Invalidate the cached list of child cameras.</summary>
        protected void InvalidateListOfChildren()
        {
            m_ChildCameras = null;
            m_indexMap = null;
            LiveChild = null;
        }

        /// <summary>Rebuild the cached list of child cameras.</summary>
        protected void ValidateListOfChildren()
        {
            if (m_ChildCameras != null)
                return;

            m_indexMap = new Dictionary<CinemachineVirtualCameraBase, int>();
            var list = new List<CinemachineVirtualCameraBase>();
            var kids = GetComponentsInChildren<CinemachineVirtualCameraBase>(true);
            foreach (var k in kids)
            {
                if (k.transform.parent != transform)
                    continue;

                var index = list.Count;
                list.Add(k);
                if (index < MaxCameras) m_indexMap.Add(k, index);
            }

            m_ChildCameras = list.ToArray();
        }

        /// <summary>Notification that this virtual camera is going live.</summary>
        /// <param name="fromCam">The camera being deactivated.  May be null.</param>
        /// <param name="worldUp">Default world Up, set by the CinemachineBrain</param>
        /// <param name="deltaTime">Delta time for time-based effects (ignore if less than or equal to 0)</param>
        public override void OnTransitionFromCamera(
            ICinemachineCamera fromCam, Vector3 worldUp, float deltaTime)
        {
            base.OnTransitionFromCamera(fromCam, worldUp, deltaTime);
            InvokeOnTransitionInExtensions(fromCam, worldUp, deltaTime);
            var children = ChildCameras;

            for (var i = 0; i < MaxCameras && i < children.Length; ++i)
                children[i].OnTransitionFromCamera(fromCam, worldUp, deltaTime);

            InternalUpdateCameraState(worldUp, deltaTime);
        }

        /// <summary>Internal use only.  Do not call this methid.
        /// Called by CinemachineCore at designated update time
        /// so the vcam can position itself and track its targets.  This implementation
        /// computes and caches the weighted blend of the tracked cameras.</summary>
        /// <param name="worldUp">Default world Up, set by the CinemachineBrain</param>
        /// <param name="deltaTime">Delta time for time-based effects (ignore if less than 0)</param>
        public override void InternalUpdateCameraState(Vector3 worldUp, float deltaTime)
        {
            var children = ChildCameras;
            LiveChild = null;
            float highestWeight = 0;
            float totalWeight = 0;

            for (var i = 0; i < MaxCameras && i < children.Length; ++i)
            {
                var vcam = children[i];
                if (!vcam.isActiveAndEnabled)
                    continue;

                var weight = Mathf.Max(0, GetWeight(i));
                if (!(weight > UnityVectorExtensions.Epsilon))
                    continue;

                totalWeight += weight;
                m_State = totalWeight == weight ? vcam.State :
                    CameraState.Lerp(m_State, vcam.State, weight / totalWeight);

                if (!(weight > highestWeight))
                    continue;

                highestWeight = weight;
                LiveChild = vcam;
            }

            InvokePostPipelineStageCallback(this, CinemachineCore.Stage.Finalize, ref m_State, deltaTime);
        }
    }
}
