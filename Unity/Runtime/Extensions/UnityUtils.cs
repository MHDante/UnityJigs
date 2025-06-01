using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace UnityJigs.Extensions
{
    public static class UnityUtils
    {
        public static void DrawGizmoCircle(Vector3 circleCenter, Vector3 circleNormal, float circleRadius,
            int segments = 32)
        {
            var radiusVector = Mathf.Abs(Vector3.Dot(circleNormal, Vector3.right)) - 1f <= Mathf.Epsilon
                ? Vector3.Cross(circleNormal, Vector3.forward).normalized
                : Vector3.Cross(circleNormal, Vector3.right).normalized;
            radiusVector *= circleRadius;
            var angleBetweenSegments = 360f / segments;
            var previousCircumferencePoint = circleCenter + radiusVector;
            for (var i = 0; i < segments; ++i)
            {
                radiusVector = Quaternion.AngleAxis(angleBetweenSegments, circleNormal) * radiusVector;
                var newCircumferencePoint = circleCenter + radiusVector;
                Gizmos.DrawLine(previousCircumferencePoint, newCircumferencePoint);
                previousCircumferencePoint = newCircumferencePoint;
            }
        }

        public static Vector2 WithX(this Vector2 v, float x) => new(x, v.y);
        public static Vector2 WithY(this Vector2 v, float y) => new(v.x, y);
        public static Vector3 WithZ(this Vector2 v, float z) => new(v.x, v.y, z);
        public static Vector3 WithX(this Vector3 v, float x) => new(x, v.y, v.z);
        public static Vector3 WithY(this Vector3 v, float y) => new(v.x, y, v.z);
        public static Vector3 WithZ(this Vector3 v, float z) => new(v.x, v.y, z);
        public static Vector3 WithXY(this Vector3 v, Vector2 xy) => new(xy.x, xy.y, v.z);
        public static Vector2 XY(this Vector3 v) => v;
        public static Vector2 YX(this Vector2 v) => new(v.y, v.x);
        public static Vector2 XZ(this Vector3 v) => new(v.x, v.z);
        public static Vector3 X_Z(this Vector2 v, float y) => new(v.x, y, v.y);
        public static Vector3 X0Z(this Vector2 v) => new(v.x, 0, v.y);

        public static Vector3 Divide(this Vector3 a, Vector3 b) => new(a.x / b.x, a.y / b.y, a.z / b.z);
        public static Vector3 Multiply(this Vector3 a, Vector3 b) => new(a.x * b.x, a.y * b.y, a.z * b.z);
        public static Color WithAlpha(this Color c, float a) => new(c.r, c.g, c.b, a);

        public static float Clamp01(this float t) => Mathf.Clamp01(t);

        public static T GetSaveData<T>(ref T? singletonField, string fileName) where T : class, new()
        {
            if (singletonField != null) return singletonField;
            var path = Path.Combine(Application.persistentDataPath, fileName + ".json");
            if (!File.Exists(path)) return singletonField = new T();
            var json = File.ReadAllText(path);
            return singletonField = JsonUtility.FromJson<T>(json);
        }


        public static void SaveData<T>(T data, string fileName)
        {
            var path = Path.Combine(Application.persistentDataPath, fileName + ".json");
            var json = JsonUtility.ToJson(data);
            File.WriteAllText(path, json);
        }

        public static T GetPreloadedSingleton<T>(ref T singletonField) where T : Object
        {
#if UNITY_EDITOR
            if (singletonField) return singletonField;
            var candidate = UnityEditor.PlayerSettings.GetPreloadedAssets().OfType<T>().First();
            if (!candidate) throw new FileNotFoundException("Could Not Find object of type T in Preloaded Assets");
            singletonField = candidate;
#endif
            return singletonField;
        }

        public static ReadOnlySpan<char> TryRemoveSuffix(this ReadOnlySpan<char> str, ReadOnlySpan<char> suffix)
        {
            if (str.Length < suffix.Length) return str;
            var target = str[^suffix.Length..];
            if (!target.SequenceEqual(suffix)) return str;
            return str[..^suffix.Length];
        }

        [ThreadStatic] private static GradientColorKey[]? _ColorKeyCache;
        [ThreadStatic] private static GradientAlphaKey[]? _AlphaKeyCache;

        public static Gradient CreateGradient(Color from, Color to, GradientMode mode = GradientMode.Blend)
        {
            _ColorKeyCache ??= new GradientColorKey[2];
            _ColorKeyCache[0] = new GradientColorKey(from, 0);
            _ColorKeyCache[1] = new GradientColorKey(to, 1);

            _AlphaKeyCache ??= new GradientAlphaKey[2];
            _AlphaKeyCache[0] = new GradientAlphaKey(from.a, 0);
            _AlphaKeyCache[1] = new GradientAlphaKey(to.a, 1);
            var gradient = new Gradient();
            gradient.SetKeys(_ColorKeyCache, _AlphaKeyCache);
            gradient.mode = mode;
            return gradient;
        }

        public static int GetAlternatingSign(int index) => index % 2 == 0 ? 1 : -1;

        public static bool HasParameter(this Animator animator, int id)
        {
            var found = false;
            for (int i = 0; i < animator.parameterCount; i++)
            {
                var param = animator.GetParameter(i);
                if (param.nameHash != id) continue;
                found = true;
                break;
            }

            return found;
        }


        public static float NearClipSize(this Camera camera) => NearClipSize(camera.fieldOfView, camera.nearClipPlane);
        public static float NearClipSize(float fov, float nearPlane) => Mathf.Tan(fov * .5f) * nearPlane;
        public static float PixelsAtNearPlane(this Canvas canvas) => ((RectTransform)canvas.transform).rect.height;
        public static float PixelsPerUnitAtNearPlane(this Canvas canvas) => 1 / canvas.transform.localScale.y;

        public static float WorldHeight(this Canvas canvas) =>
            PixelsAtNearPlane(canvas) / PixelsPerUnitAtNearPlane(canvas);

        public static float PixelsPerUnitAtDistance(this Canvas canvas, float distanceFromCam) =>
            canvas.renderMode == RenderMode.WorldSpace ? canvas.PixelsAtNearPlane() :
                canvas.planeDistance / distanceFromCam * canvas.PixelsPerUnitAtNearPlane();

        public static float UnitScaleAtDistance(this Canvas canvas, float distanceFromCam) =>
            distanceFromCam / canvas.planeDistance;

        public static bool AboutOnceEvery(float timeInterval) =>
            Random.Range(0f, timeInterval) < Time.deltaTime;


        public static IEnumerable<T> WhereNotNull<T>(this IEnumerable<T?> enumerable) =>
            enumerable.Where(it => it != null)!;

        public static void Return<T>(this ArrayPool<T> pool, ref T[] field, bool clearArray = false)
        {
            pool.Return(field, clearArray);
            field = Array.Empty<T>();
        }

        public static Vector3 WorldCentre(this CapsuleCollider capsuleCollider) =>
            capsuleCollider.transform.TransformPoint(capsuleCollider.center);

        public static bool Includes(this LayerMask mask, int layerIndex) =>
            (mask.value & 1 << layerIndex) != 0;

        public static bool Includes(this LayerMask mask, GameObject obj) =>
            (mask.value & 1 << obj.layer) != 0;

        public static bool Includes(this LayerMask mask, Component comp) =>
            (mask.value & 1 << comp.gameObject.layer) != 0;

        public static Vector3? ViewportPointToWorldPointOnPlane(this Camera camera, Plane plane, Vector3 viewportPoint)
        {
            var ray = camera.ViewportPointToRay(viewportPoint);
            if (!plane.Raycast(ray, out var dist)) return null;
            Debug.DrawRay(ray.origin, ray.direction * dist, Color.magenta);
            return ray.GetPoint(dist);
        }

        public static T? SafeNull<T>(this T? source) where T : Object =>
            // ReSharper disable once MergeConditionalExpression
            ReferenceEquals(source, null) ? null : source;


        public static void ClampMin(this ref float source, float min)
        {
            if (source < min) source = min;
        }

        public static void ClampMax(this ref float source, float max)
        {
            if (source > max) source = max;
        }

        public static void Clamp(this ref float source, float min, float max)
        {
            source = Mathf.Clamp(source, min, max);
        }

        public static float Sqr(this float f) => f * f;


        public static void ExitGameOrPlaymode()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.ExitPlaymode();
#else
            Application.Quit();
#endif
        }
    }
}
