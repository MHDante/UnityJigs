using System.Collections.Generic;
using System.Linq;
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
#if UNITY_6000_0_OR_NEWER
        [SerializeField] private PhysicsMaterial? Material;
#else
        [SerializeField] private PhysicMaterial? Material;
#endif
        private string? _error;
        private int _lastHash;
        private int _lastCount;
        private List<MeshCollider> Colliders { get; } = new();
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


        [ShowInInspector,
         InfoBox("$" + nameof(_error), InfoMessageType.Error,
             VisibleIf = "@!string.IsNullOrEmpty(" + nameof(_error) + ")")]
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
                    Debug.LogException(ex, this);
                }
            }
        }

        private void Rebuild()
        {
            Colliders.RemoveAll(static it => !it);
            Colliders.Clear();
            var childColliders = transform.Cast<Transform>().Select(it => it.GetComponent<MeshCollider>())
                .WhereNotNull();
            Colliders.AddRange(childColliders);

            var (polygon, height) = GetSource();
            if (polygon.Count < 3) return;

            using var _1 = ListPool<List<Vector2>>.Get(out var convexPolys);
            ConvexDecomposition2D.Decompose(polygon, convexPolys);

            for (var i = convexPolys.Count; i < Colliders.Count; i++)
            {
                var excessCollider = Colliders[i];
                excessCollider.gameObject.DestroySafe();
                Colliders.RemoveAt(i);
                i--;
            }

            for (int i = 0; i < convexPolys.Count; i++)
            {
                var mc = Colliders.GetSafe(i) ?? Colliders.AddAndGet(MakeCollider());
                if(mc.sharedMesh == null) mc.sharedMesh = MakeMesh();
                var poly = convexPolys[i];
                CreateConvexPrism(poly, height, mc.sharedMesh);
            }

            ApplyColliderSettings();
        }

        protected virtual void CreateConvexPrism(List<Vector2> poly2D, float height, Mesh mesh)
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

            mesh.Clear();
            mesh.vertices = verts;
            mesh.triangles = tris.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        private MeshCollider MakeCollider()
        {
            var child = new GameObject($"ConvexPiece_{Colliders.Count}");
            child.transform.SetParent(transform, false);
            var mc = child.AddComponent<MeshCollider>();
            mc.convex = true;
            mc.sharedMesh = MakeMesh();
            return mc;
        }

        private Mesh MakeMesh()
        {
            var mesh = new Mesh(); // fresh mesh in edit mode
            if (Application.IsPlaying(this)) mesh.MarkDynamic();
            return mesh;
        }

        private void ApplyColliderSettings()
        {
            foreach (var mc in Colliders)
            {
                if (!mc) continue;
                mc.isTrigger = IsTrigger;
                mc.sharedMaterial = Material;
                mc.gameObject.layer = gameObject.layer;
            }
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

#if UNITY_EDITOR
        [UnityEditor.MenuItem("Utils/CompositeCollider/Rebuild All")]
#endif
        public static void RebuildAll()
        {
            var generators =
                FindObjectsByType<CompositeColliderGenerator>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Debug.Log(generators.Length + " CompositeColliderGenerators found");
            foreach (var generator in generators) generator.Rebuild();
        }


#if UNITY_EDITOR
        [UnityEditor.MenuItem("Utils/CompositeCollider/Destroy All Children")]
#endif
        public static void DestroyAllChildren()
        {
            var generators =
                FindObjectsByType<CompositeColliderGenerator>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Debug.Log(generators.Length + " CompositeColliderGenerators found");
            foreach (var generator in generators)
            {
                generator.DestroyChildren();
            }
        }

        [Button]
        private void DestroyChildren()
        {
            var children = transform.Children().ToList();
            foreach (var child in children) child.gameObject.DestroySafe();
        }
    }
}
