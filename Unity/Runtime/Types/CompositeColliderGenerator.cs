using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;
using UnityJigs.Geometry;

namespace UnityJigs.Colliders
{
    [ExecuteInEditMode]
    public abstract class CompositeColliderGenerator : MonoBehaviour
    {
        int _lastHash;

        protected abstract (List<Vector2> polygon, float height) GetSource();

        void Update()
        {
            if (Application.isPlaying) return;

            var hash = ComputeHash();
            if (hash != _lastHash)
            {
                _lastHash = hash;
                Rebuild();
            }
        }

        public void Rebuild()
        {
            // clear existing children
            using var _1 = ListPool<GameObject>.Get(out var toDelete);
            foreach (Transform t in transform) toDelete.Add(t.gameObject);
            foreach (var go in toDelete) DestroyImmediate(go);

            var (polygon, height) = GetSource();
            if (polygon.Count < 3) return;

            using var _2 = ListPool<List<Vector2>>.Get(out var convexPolys);
            ConvexDecomposition2D.Decompose(polygon, convexPolys);

            foreach (var poly in convexPolys)
                CreateConvexPrism(poly, height);
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
                tris.Add(0); tris.Add(i); tris.Add(i + 1);
            }

            for (var i = 1; i < n - 1; i++)
            {
                tris.Add(n); tris.Add(n + i + 1); tris.Add(n + i);
            }

            for (var i = 0; i < n; i++)
            {
                var j = (i + 1) % n;
                tris.Add(i); tris.Add(j); tris.Add(n + i);
                tris.Add(j); tris.Add(n + j); tris.Add(n + i);
            }

            var mesh = new Mesh { vertices = verts, triangles = tris.ToArray() };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var child = new GameObject("ConvexPiece");
            child.transform.SetParent(transform, false);
            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            child.AddComponent<MeshRenderer>().enabled = false;
            var mc = child.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
            mc.convex = true;
        }

        int ComputeHash()
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
