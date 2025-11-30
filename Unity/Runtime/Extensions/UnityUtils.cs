using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;
using Application = UnityEngine.Application;

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

        public static float NormalizedTimeClamp01(this AnimatorStateInfo state) =>
            Mathf.Clamp01(state.normalizedTime);

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

        public static float Min(this Vector3 v) => Mathf.Min(Mathf.Min(v.x, v.y), v.z);
        public static float Min(this Vector2 v) => Mathf.Min(v.x, v.y);
        public static float Min(this Vector4 v) => Mathf.Min(Mathf.Min(v.x, v.y), Mathf.Min(v.z, v.w));

        public static float Max(this Vector2 v) => Mathf.Min(v.x, v.y);
        public static float Max(this Vector3 v) => Mathf.Max(Mathf.Max(v.x, v.y), v.z);
        public static float Max(this Vector4 v) => Mathf.Max(Mathf.Max(v.x, v.y), Mathf.Max(v.z, v.w));


        public static Vector3 Divide(this Vector3 a, Vector3 b) => new(a.x / b.x, a.y / b.y, a.z / b.z);
        public static Vector3 Multiply(this Vector3 a, Vector3 b) => new(a.x * b.x, a.y * b.y, a.z * b.z);
        public static Color WithAlpha(this Color c, float a) => new(c.r, c.g, c.b, a);

        public static bool MinMaxContains(this Vector2 minMax, float v, bool minInclusive = true,
            bool maxInclusive = true)
        {
            if (minInclusive)
            {
                if (!(v >= minMax.x)) return false;
            }
            else
            {
                if (!(v > minMax.x)) return false;
            }

            if (maxInclusive)
            {
                if (!(v <= minMax.y)) return false;
            }
            else
            {
                if (!(v < minMax.y)) return false;
            }

            return true;
        }

        public static float Clamp01(this float t) => Mathf.Clamp01(t);
        public static float Abs(this float t) => Mathf.Abs(t);

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

        public static void DestroySafe(this Object? obj)
        {
            if (!obj) return;
            if (Application.IsPlaying(obj)) Object.Destroy(obj);
            else Object.DestroyImmediate(obj);
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

        public static T? SafeNull<T>(this T? source) where T : Object => source ? source : null;

        public static float Diff(this float a, float b) => Mathf.Abs(a - b);

        public static float Cross(this Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;


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

        /// <summary>
        /// Draws a wireframe quad (4 edges) defined by its four corners.
        /// </summary>
        public static void DrawWireQuadGizmo(
            in Vector3 c0, in Vector3 c1, in Vector3 c2, in Vector3 c3)
        {
            Gizmos.DrawLine(c0, c1);
            Gizmos.DrawLine(c1, c2);
            Gizmos.DrawLine(c2, c3);
            Gizmos.DrawLine(c3, c0);
        }

        /// <summary>
        /// Draws a full wireframe volume (12 edges) between a near and far quad.
        /// </summary>
        public static void DrawWireVolumeGizmo(
            in Vector3 n0, in Vector3 n1, in Vector3 n2, in Vector3 n3,
            in Vector3 f0, in Vector3 f1, in Vector3 f2, in Vector3 f3)
        {
            // Near + far quads
            DrawWireQuadGizmo(in n0, in n1, in n2, in n3);
            DrawWireQuadGizmo(in f0, in f1, in f2, in f3);

            // Connect edges
            Gizmos.DrawLine(n0, f0);
            Gizmos.DrawLine(n1, f1);
            Gizmos.DrawLine(n2, f2);
            Gizmos.DrawLine(n3, f3);
        }

        public static TransformEnumerable Children(this Transform parent) => new(parent);
        public static TransformEnumerable Children(this GameObject parent) => new(parent.transform);


        public static void SyncTo(this Transform target, Transform source, bool includeLocalScale = false)
        {
            target.position = source.position;
            target.rotation = source.rotation;
            if (includeLocalScale) target.localScale = source.localScale;
        }

        public static void SyncTo(this Rigidbody target, Transform source)
        {
            target.position = source.position;
            target.rotation = source.rotation;
        }

        public static void SyncTo(this Rigidbody target, Rigidbody source)
        {
            target.position = source.position;
            target.rotation = source.rotation;
        }

        public static T GetComponentCached<T>(this Component c, ref T? field) where T : Component =>
            field ? field! : field = c.GetComponent<T>();

#if UNITY_6000_0_OR_NEWER

        public static Task LerpTo(this Transform transform, Transform target, float duration,
            UpdateTimingFlags updateType = UpdateTimingFlags.Update) =>
            LerpTo(transform, target.position, target.rotation, duration, updateType);

        public static async Task LerpTo(this Transform transform, Vector3 position, Quaternion rotation, float duration,
            UpdateTimingFlags updateType = UpdateTimingFlags.Update)
        {
            if (duration <= 0)
            {
                transform.position = position;
                transform.rotation = rotation;
            }

            var start = Time.time;
            var startPos = transform.position;
            var startRot = transform.rotation;

            while (true)
            {
                var t = Mathf.Clamp01((Time.time - start) / duration);
                transform.position = Vector3.Lerp(startPos, position, t);
                transform.rotation = Quaternion.Slerp(startRot, rotation, t);
                if(t >= 1) break;
                switch (updateType)
                {
                    case UpdateTimingFlags.Update:
                        await Awaitable.NextFrameAsync();
                        break;
                    case UpdateTimingFlags.FixedUpdate:
                        await Awaitable.FixedUpdateAsync();
                        break;
                    case UpdateTimingFlags.LateUpdate:
                        await Awaitable.EndOfFrameAsync();
                        break;
                    case UpdateTimingFlags.InEditor:
                    default:
                        throw new ArgumentOutOfRangeException(nameof(updateType), updateType, null);
                }
            }
        }

#endif
    }
}
