using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.PlayerLoop;
using UnityEngine.Pool;
using UnityEngine.Rendering;
using UnityJigs.Extensions;
using UnityJigs.Types;

namespace UnityJigs.Components
{
    public class InstancedMeshGroup : LoopSystem
    {
        protected override Type ParentLoopType => typeof(PostLateUpdate);
        private static InstancedMeshGroup? _Instance;
        public static InstancedMeshGroup Instance => _Instance ?? new();

        private readonly Dictionary<InstancedMesh, HashSet<InstancedMeshRenderer>> _renderersByMesh = new();

        private InstancedMeshGroup()
        {
            if (_Instance != null) throw new Exception();
            _Instance = this;
            Initialize();
        }


        protected override void Update()
        {
            foreach (var (mesh, renderers) in _renderersByMesh)
            {
                if(renderers.Count == 0) continue;
                var rp = new RenderParams(mesh.Material)
                {
                    receiveShadows = true,
                    shadowCastingMode = ShadowCastingMode.On,
                };
                using var _ = ListPool<Matrix4x4>.Get(out var matrices);
                //var matrices = new List<Matrix4x4>();
                foreach (var renderer in renderers) matrices.Add(renderer.transform.localToWorldMatrix);
                Graphics.RenderMeshInstanced(in rp, mesh.Mesh, 0, matrices);
            }
        }

        public bool Register(InstancedMeshRenderer renderer) =>
            _renderersByMesh.GetOrAddNew(renderer.Mesh).Add(renderer);

        public bool Unregister(InstancedMeshRenderer renderer) =>
            _renderersByMesh.TryGetValue(renderer.Mesh, out var list) && list.Remove(renderer);
    }
}
