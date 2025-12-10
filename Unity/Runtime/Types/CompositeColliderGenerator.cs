using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Pool;
using Sirenix.OdinInspector;
using UnityJigs.Extensions;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UnityJigs.Types
{
    [ExecuteInEditMode]
    public abstract class CompositeColliderGenerator : MonoBehaviour
    {
        private const float ShapeTolerance = 0.01f;

        [System.Serializable]
        private class BakedPolygon
        {
            public Vector2[] Points = System.Array.Empty<Vector2>();
        }

        [SerializeField] private bool IsTrigger;
        [SerializeField] private PhysicsMaterial? Material;

        [SerializeField, HideInInspector] private float _bakedHeight;
        [SerializeField, HideInInspector] private List<BakedPolygon> _bakedPolygons = new();

        [SerializeField, HideInInspector] private int _lastHash;
        [SerializeField, HideInInspector] private int _lastCount;

        private string? _error;
        private bool _runtimeBuilt;

        private List<MeshCollider> Colliders { get; } = new();

        public bool UpdateDynamically = false;

        protected abstract (List<Vector2> polygon, float height) GetSource();

        private void Awake()
        {
            if (Application.IsPlaying(this))
            {
                if (_bakedPolygons.Count == 0)
                {
                    try
                    {
                        BakeFromSource();
                        _lastHash = ComputeShapeHash();
                        _lastCount = _bakedPolygons.Count;
                    }
                    catch (System.Exception ex)
                    {
                        _error = ex.Message;
                        Debug.LogException(ex, this);
                    }
                }

                BuildRuntimeColliders();
            }
            else
            {
                TryRebuild();
            }
        }

        private void Update()
        {
            if (!UpdateDynamically) return;
            if (Application.IsPlaying(this)) return;

            var hash = ComputeShapeHash();
            if (hash != _lastHash || _bakedPolygons.Count != _lastCount)
                TryRebuild();
        }

        private void OnValidate() => ApplyColliderSettings();

        [ShowInInspector,
         InfoBox("$" + nameof(_error), InfoMessageType.Error,
             VisibleIf = "@!string.IsNullOrEmpty(" + nameof(_error) + ")")]
        private void TryRebuild()
        {
            if (Application.IsPlaying(this))
            {
                BuildRuntimeColliders();
                return;
            }

            try
            {
#if UNITY_EDITOR
                var changed = Editor_BakeIfShapeChanged();
                if (changed)
                    _error = null;

                BuildPreviewColliders();
#else
                BakeFromSource();
                _lastHash = ComputeShapeHash();
                _lastCount = _bakedPolygons.Count;
                _error = null;
#endif
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

        private void BakeFromSource()
        {
            var (polygon, height) = GetSource();

            _bakedPolygons.Clear();
            _bakedHeight = height;

            if (polygon.Count < 3)
                return;

            using var _1 = ListPool<List<Vector2>>.Get(out var convexPolys);
            ConvexDecomposition2D.Decompose(polygon, convexPolys);

            foreach (var poly in convexPolys)
            {
                var baked = new BakedPolygon
                {
                    Points = poly.ToArray()
                };
                _bakedPolygons.Add(baked);
            }
        }

        private void BuildRuntimeColliders()
        {
            if (_runtimeBuilt) return;
            _runtimeBuilt = true;

            if (_bakedPolygons.Count == 0)
            {
                try
                {
                    BakeFromSource();
                }
                catch (System.Exception ex)
                {
                    _error = ex.Message;
                    Debug.LogException(ex, this);
                    return;
                }
            }

            BuildCollidersFromBaked(true);
        }

#if UNITY_EDITOR
        private void BuildPreviewColliders()
        {
            if (Application.IsPlaying(this)) return;
            BuildCollidersFromBaked(false);
        }
#endif

        private void BuildCollidersFromBaked(bool persistent)
        {
            Colliders.RemoveAll(static it => !it);

            var childColliders = transform.Cast<Transform>()
                .Select(t => t.GetComponent<MeshCollider>())
                .WhereNotNull()
                .ToList();

            foreach (var mc in childColliders)
                mc.gameObject.DestroySafe();

            Colliders.Clear();

            if (_bakedPolygons.Count == 0)
                return;

            for (var i = 0; i < _bakedPolygons.Count; i++)
            {
                var mc = MakeCollider(persistent);
                Colliders.Add(mc);

                var polyArray = _bakedPolygons[i].Points;
                using var _1 = ListPool<Vector2>.Get(out var poly);
                for (var j = 0; j < polyArray.Length; j++)
                    poly.Add(polyArray[j]);

                var mesh = mc.sharedMesh!;
                CreateConvexPrism(poly, _bakedHeight, mesh);

                // Force PhysX to recook from the final mesh contents.
                mc.sharedMesh = null;
                mc.sharedMesh = mesh;
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

        private MeshCollider MakeCollider(bool persistent)
        {
            var child = new GameObject($"ConvexPiece_{Colliders.Count}");
            child.transform.SetParent(transform, false);

#if UNITY_EDITOR
            if (!persistent && !Application.IsPlaying(this))
                child.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
#endif

            var mc = child.AddComponent<MeshCollider>();
            mc.convex = true;
            mc.sharedMesh = MakeMesh();
            return mc;
        }

        private Mesh MakeMesh()
        {
            var mesh = new Mesh();
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

        private static float Quantize(float value) =>
            Mathf.Round(value / ShapeTolerance) * ShapeTolerance;

        private int ComputeShapeHash()
        {
            var (polygon, height) = GetSource();
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + Quantize(height).GetHashCode();
                for (var i = 0; i < polygon.Count; i++)
                {
                    var p = polygon[i];
                    hash = hash * 31 + Quantize(p.x).GetHashCode();
                    hash = hash * 31 + Quantize(p.y).GetHashCode();
                }

                return hash;
            }
        }

#if UNITY_EDITOR
        internal bool Editor_BakeIfShapeChanged()
        {
            var hash = ComputeShapeHash();

            if (_bakedPolygons.Count > 0 &&
                hash == _lastHash &&
                _bakedPolygons.Count == _lastCount)
            {
                return false;
            }

            BakeFromSource();
            _lastHash = hash;
            _lastCount = _bakedPolygons.Count;
            return true;
        }

        internal void Editor_DestroyColliderChildrenImmediate()
        {
            var allColliders = GetComponentsInChildren<MeshCollider>(true);

            var toDestroy = new List<GameObject>();
            foreach (var mc in allColliders)
            {
                if (!mc) continue;
                var go = mc.gameObject;
                if (go == gameObject) continue;

                var status = PrefabUtility.GetPrefabInstanceStatus(go);
                if (status != PrefabInstanceStatus.NotAPrefab)
                    continue;

                toDestroy.Add(go);
            }

            foreach (var go in toDestroy.Distinct())
                go.DestroySafe();

            Colliders.Clear();
        }
#endif

        [Button]
        private void DestroyChildren()
        {
            var children = transform.Children().ToList();
            foreach (var child in children) child.gameObject.DestroySafe();
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0f, 0.5f, 1f, 1f);

            var meshColliders = Colliders.Where(mc => mc && mc.sharedMesh).ToList();
            if (meshColliders.Count == 0)
            {
                meshColliders = GetComponentsInChildren<MeshCollider>(true)
                    .Where(mc => mc && mc.sharedMesh)
                    .ToList();
            }

            foreach (var mc in meshColliders)
            {
                var mesh = mc.sharedMesh;
                if (!mesh) continue;

                var verts = mesh.vertices;
                var tris = mesh.triangles;

                var oldMatrix = Gizmos.matrix;
                Gizmos.matrix = mc.transform.localToWorldMatrix;

                for (var i = 0; i < tris.Length; i += 3)
                {
                    var a = verts[tris[i]];
                    var b = verts[tris[i + 1]];
                    var c = verts[tris[i + 2]];

                    Gizmos.DrawLine(a, b);
                    Gizmos.DrawLine(b, c);
                    Gizmos.DrawLine(c, a);
                }

                Gizmos.matrix = oldMatrix;
            }
        }
    }
}
