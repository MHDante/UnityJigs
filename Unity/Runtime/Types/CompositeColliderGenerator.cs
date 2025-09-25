using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;
using Sirenix.OdinInspector;
using UnityJigs.Extensions;

namespace UnityJigs.Types
{
    [ExecuteInEditMode]
    public abstract class CompositeColliderGenerator : MonoBehaviour
    {
        [SerializeField] private bool IsTrigger;
        [SerializeField] private PhysicsMaterial? Material;

        private string? _error;

        private int _lastHash;
        private int _lastCount;
        public List<MeshCollider> Colliders = new();
        private readonly Queue<Mesh> _meshPool = new();

        protected abstract (List<Vector2> polygon, float height) GetSource();

        private void Update()
        {
            Colliders.RemoveAll(static it => !it);
            var hash = ComputeHash();
            if (hash != _lastHash || Colliders.Count != _lastCount || Colliders.Count == 0)
            {
                _lastHash = hash;
                TryRebuild();
                _lastCount = Colliders.Count;
            }
        }

        private void OnValidate() => ApplyColliderSettings();

        private void OnEnable() => SetCollidersActive(true);
        private void OnDisable() => SetCollidersActive(false);



        [ShowInInspector, InfoBox("$" + nameof(_error), InfoMessageType.Error, VisibleIf = "@!string.IsNullOrEmpty(" + nameof(_error) + ")")]
        private void TryRebuild()
        {
            try
            {
                Rebuild();
                _error = null;
            }
            catch (System.Exception ex)
            {
                if (_error != ex.Message)
                {
                    _error = ex.Message;
                    Debug.LogError($"[{name}] Collider rebuild failed: {_error}", this);
                }
            }
        }

        private void Rebuild()
        {
            var isPlaying = Application.IsPlaying(this);
            Colliders.RemoveAll(static it => !it);

            foreach (var mc in Colliders)
            {
                if (mc.sharedMesh != null && isPlaying)
                    _meshPool.Enqueue(mc.sharedMesh);

                if (isPlaying)
                    Destroy(mc.gameObject);
                else
                    DestroyImmediate(mc.gameObject);
            }

            Colliders.Clear();

            var (polygon, height) = GetSource();
            if (polygon.Count < 3) return;

            using var _1 = ListPool<List<Vector2>>.Get(out var convexPolys);
            ConvexDecomposition2D.Decompose(polygon, convexPolys);

            foreach (var poly in convexPolys)
                CreateConvexPrism(poly, height);

            ApplyColliderSettings();
        }

        protected virtual void CreateConvexPrism(List<Vector2> poly2D, float height)
        {
            var n = poly2D.Count;
            if (n < 3) return;

            var verts = new Vector3[n * 2];
            for (var i = 0; i < n; i++)
            {
                var v = new Vector3(poly2D[i].x, 0f, poly2D[i].y);
                verts[i] = v;
                verts[i + n] = v + Vector3.up * height;
            }

            using var _1 = ListPool<int>.Get(out var tris);

            for (var i = 1; i < n - 1; i++)
            {
                tris.Add(0);
                tris.Add(i);
                tris.Add(i + 1);
            }

            for (var i = 1; i < n - 1; i++)
            {
                tris.Add(n);
                tris.Add(n + i + 1);
                tris.Add(n + i);
            }

            for (var i = 0; i < n; i++)
            {
                var j = (i + 1) % n;
                tris.Add(i);
                tris.Add(j);
                tris.Add(n + i);
                tris.Add(j);
                tris.Add(n + j);
                tris.Add(n + i);
            }

            var mesh = GetMesh();
            mesh.Clear();
            mesh.vertices = verts;
            mesh.triangles = tris.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var child = new GameObject($"ConvexPiece_{Colliders.Count}");
            child.transform.SetParent(transform, false);
            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            child.AddComponent<MeshRenderer>().enabled = false;

            var mc = child.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
            mc.convex = true;
            Colliders.Add(mc);
        }

        private Mesh GetMesh()
        {
            if (Application.IsPlaying(this))
            {
                var mesh = _meshPool.Count > 0 ? _meshPool.Dequeue() : new Mesh();
                mesh.MarkDynamic();
                return mesh;
            }

            return new Mesh(); // fresh mesh in edit mode
        }

        private void ApplyColliderSettings()
        {
            foreach (var mc in Colliders)
            {
                if (!mc) continue;
                mc.isTrigger = IsTrigger;
                mc.sharedMaterial = Material;
            }
        }

        private void SetCollidersActive(bool active)
        {
            foreach (var mc in Colliders)
                if (mc && mc.gameObject)
                    mc.gameObject.SetActive(active);
        }

        private int ComputeHash()
        {
            var (polygon, height) = GetSource();
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + height.GetHashCode();
                foreach (var p in polygon)
                {
                    hash = hash * 31 + p.x.GetHashCode();
                    hash = hash * 31 + p.y.GetHashCode();
                }

                return hash;
            }
        }
    }
}
