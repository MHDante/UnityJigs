using System.Collections.Generic;
using UnityEngine;

namespace NorthShore.Player
{
    public class GizmoDrawer : MonoBehaviour
    {
        private readonly List<CapsuleData> _capsuleDataList = new();
        private readonly List<SphereData> _sphereDataList = new();
        private readonly List<BoxData> _boxDataList = new();

        private static GizmoDrawer? _Instance;
        private static GizmoDrawer Instance => _Instance ? _Instance : new GameObject().AddComponent<GizmoDrawer>();

        private void Awake()
        {
            if (_Instance == null) _Instance = this;
            else if (_Instance != this) Destroy(gameObject);
        }


        public static void DrawSphereCast(Vector3 origin, float radius, Vector3 direction, float distance,
            Color? color = null) =>
            Instance._capsuleDataList.Add(new CapsuleData
            {
                Origin = origin,
                Direction = direction,
                Radius = radius,
                Distance = distance,
                Color = color ?? Color.green
            });


        public static void DrawCapsule(Vector3 point1, Vector3 point2, float radius, Color? color = null) =>
            DrawSphereCast(point1, radius, (point2 - point1).normalized, Vector3.Distance(point2, point1), color);

        public static void DrawBox(Vector3 centre, Vector3 size, Quaternion orientation, Color? color = null) =>
            Instance._boxDataList.Add(new BoxData
            {
                Centre = centre,
                Size = size,
                Orientation = orientation,
                Color = color ?? Color.green
            });

        public static void DrawSphere(Vector3 centre, float radius, Color? color = null) =>
            Instance._sphereDataList.Add(new()
            {
                Origin = centre,
                Radius = radius,
                Color = color ?? Color.green
            });

        private void OnDrawGizmos()
        {
            foreach (var data in _capsuleDataList)
            {
                var oldColor = Gizmos.color;
                DrawCapsule(data.Origin, data.Direction, data.Radius, data.Distance, data.Color);
                Gizmos.color = oldColor;
            }
            _capsuleDataList.Clear();

            foreach (var data in _boxDataList)
            {
                var oldColor = Gizmos.color;
                var oldMatrix = Gizmos.matrix;
                Gizmos.color = data.Color;
                Gizmos.matrix = Matrix4x4.Rotate(data.Orientation);
                Gizmos.DrawWireCube(data.Centre, data.Size);
                Gizmos.color = oldColor;
                Gizmos.matrix = oldMatrix;
            }
            _boxDataList.Clear();

            foreach (var data in _sphereDataList)
            {
                var oldColor = Gizmos.color;
                Gizmos.color = data.Color;
                Gizmos.DrawWireSphere(data.Origin, data.Radius);
                Gizmos.color = oldColor;
            }
            _sphereDataList.Clear();
        }

        private void DrawCapsule(Vector3 origin, Vector3 direction, float radius, float distance, Color color)
        {
            Gizmos.color = color;

            var endPosition = origin + direction.normalized * distance;

            // Draw main sphere
            Gizmos.DrawWireSphere(origin, radius);

            // Draw end sphere
            Gizmos.DrawWireSphere(endPosition, radius);

            // Draw lines connecting the two spheres
            Gizmos.DrawLine(origin + Vector3.up * radius, endPosition + Vector3.up * radius);
            Gizmos.DrawLine(origin - Vector3.up * radius, endPosition - Vector3.up * radius);
            Gizmos.DrawLine(origin + Vector3.right * radius, endPosition + Vector3.right * radius);
            Gizmos.DrawLine(origin - Vector3.right * radius, endPosition - Vector3.right * radius);
            Gizmos.DrawLine(origin + Vector3.forward * radius, endPosition + Vector3.forward * radius);
            Gizmos.DrawLine(origin - Vector3.forward * radius, endPosition - Vector3.forward * radius);
        }

        private struct CapsuleData
        {
            public Vector3 Origin;
            public Vector3 Direction;
            public float Radius;
            public float Distance;
            public Color Color;
        }

        private struct SphereData
        {
            public Vector3 Origin;
            public float Radius;
            public Color Color;
        }

        private struct BoxData
        {
            public Vector3 Centre;
            public Vector3 Size;
            public Quaternion Orientation;
            public Color Color;
        }
    }
}
