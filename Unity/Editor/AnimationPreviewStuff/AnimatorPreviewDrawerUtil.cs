using System;
using System.Linq.Expressions;
using System.Reflection;
using UnityEngine;

namespace UnityJigs.Editor
{
    /// <summary>
    /// High-performance reflective wrapper for Unity's internal <c>AvatarPreview</c>
    /// (managed by <c>AnimationClipEditor</c>), allowing custom inspectors and editor
    /// windows to embed Unity's native animation preview safely and efficiently.
    /// </summary>
    public sealed class AnimationPreviewDrawerUtil : IDisposable
    {
        // ========================================================================
        // Static cached reflection metadata & delegates
        // ========================================================================

        private static readonly Type AnimationClipEditorType;

        private static readonly Func<object, object?> GetAvatarPreview;
        private static readonly Func<object, object?> GetTimeControl;

        private static readonly Func<object, Vector2> GetPreviewDir;
        private static readonly Action<object, Vector2> SetPreviewDir;

        private static readonly Func<object, float> GetZoomFactor;
        private static readonly Action<object, float> SetZoomFactor;

        private static readonly Func<object, Vector3> GetPivotOffset;
        private static readonly Action<object, Vector3> SetPivotOffset;

        private static readonly Func<object, bool> GetIs2D;
        private static readonly Action<object, bool> SetIs2D;

        private static readonly Func<object, bool> GetIKOnFeet;
        private static readonly Action<object, bool> SetIKOnFeet;

        private static readonly Func<object, bool> GetShowIKButton;
        private static readonly Action<object, bool> SetShowIKButton;

        private static readonly Func<object, float> GetPlaybackSpeed;
        private static readonly Action<object, float> SetPlaybackSpeed;

        private static readonly Func<object, bool> GetLoop;
        private static readonly Action<object, bool> SetLoop;

        private static readonly Func<object, float> GetCurrentTime;
        private static readonly Action<object, float> SetCurrentTime;

        private static readonly Func<object, float> GetNormalizedTime;
        private static readonly Action<object, float> SetNormalizedTime;

        private static readonly Func<object, bool> GetPlaying;
        private static readonly Action<object, bool> SetPlaying;

        private static readonly Func<object, Camera?> GetCamera;

        // --- TimeControl constants ---
        private static readonly float KScrubberHeight;
        private static readonly float KPlayButtonWidth;
        // --- AvatarPreview layout constant (cannot be reflected; method-local const) ---
        private const float KSliderWidth = 150f;

        // ========================================================================
        // Static constructor — builds all access delegates once
        // ========================================================================
        static AnimationPreviewDrawerUtil()
        {
            AnimationClipEditorType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.AnimationClipEditor")
                                      ?? throw new InvalidOperationException("Cannot find UnityEditor.AnimationClipEditor type.");

            var avatarPreviewField = AnimationClipEditorType.GetField("m_AvatarPreview", BindingFlags.NonPublic | BindingFlags.Instance)
                                     ?? throw new InvalidOperationException("Cannot find m_AvatarPreview field.");

            GetAvatarPreview = BuildFieldGetter<object, object?>(avatarPreviewField);
            var avatarPreviewType = avatarPreviewField.FieldType;

            // AvatarPreview core fields
            var previewDirField = avatarPreviewType.GetField("m_PreviewDir", BindingFlags.NonPublic | BindingFlags.Instance)!;
            GetPreviewDir = BuildFieldGetter<object, Vector2>(previewDirField);
            SetPreviewDir = BuildFieldSetter<object, Vector2>(previewDirField);

            var zoomField = avatarPreviewType.GetField("m_ZoomFactor", BindingFlags.NonPublic | BindingFlags.Instance)!;
            GetZoomFactor = BuildFieldGetter<object, float>(zoomField);
            SetZoomFactor = BuildFieldSetter<object, float>(zoomField);

            var pivotField = avatarPreviewType.GetField("m_PivotPositionOffset", BindingFlags.NonPublic | BindingFlags.Instance)!;
            GetPivotOffset = BuildFieldGetter<object, Vector3>(pivotField);
            SetPivotOffset = BuildFieldSetter<object, Vector3>(pivotField);

            var is2DField = avatarPreviewType.GetField("m_2D", BindingFlags.NonPublic | BindingFlags.Instance)!;
            GetIs2D = BuildFieldGetter<object, bool>(is2DField);
            SetIs2D = BuildFieldSetter<object, bool>(is2DField);

            var ikField = avatarPreviewType.GetField("m_IKOnFeet", BindingFlags.NonPublic | BindingFlags.Instance)!;
            GetIKOnFeet = BuildFieldGetter<object, bool>(ikField);
            SetIKOnFeet = BuildFieldSetter<object, bool>(ikField);

            var showIKField = avatarPreviewType.GetField("m_ShowIKOnFeetButton", BindingFlags.NonPublic | BindingFlags.Instance)!;
            GetShowIKButton = BuildFieldGetter<object, bool>(showIKField);
            SetShowIKButton = BuildFieldSetter<object, bool>(showIKField);

            // Camera access
            var previewUtilityField = avatarPreviewType.GetField("m_PreviewUtility", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var cameraProp = previewUtilityField.FieldType.GetProperty("camera", BindingFlags.Public | BindingFlags.Instance)!;
            GetCamera = BuildNestedPropertyGetter<object, Camera?>(previewUtilityField, cameraProp);

            // TimeControl fields & props
            var timeControlField = avatarPreviewType.GetField("timeControl", BindingFlags.Public | BindingFlags.Instance)!;
            GetTimeControl = BuildFieldGetter<object, object?>(timeControlField);
            var timeControlType = timeControlField.FieldType;

            GetPlaybackSpeed = BuildFieldGetter<object, float>(timeControlType.GetField("playbackSpeed")!);
            SetPlaybackSpeed = BuildFieldSetter<object, float>(timeControlType.GetField("playbackSpeed")!);

            GetLoop = BuildFieldGetter<object, bool>(timeControlType.GetField("loop")!);
            SetLoop = BuildFieldSetter<object, bool>(timeControlType.GetField("loop")!);

            GetCurrentTime = BuildFieldGetter<object, float>(timeControlType.GetField("currentTime")!);
            SetCurrentTime = BuildFieldSetter<object, float>(timeControlType.GetField("currentTime")!);

            var normalizedTimeProp = timeControlType.GetProperty("normalizedTime")!;
            GetNormalizedTime = BuildPropertyGetter<object, float>(normalizedTimeProp);
            SetNormalizedTime = BuildPropertySetter<object, float>(normalizedTimeProp);

            var playingProp = timeControlType.GetProperty("playing")!;
            GetPlaying = BuildPropertyGetter<object, bool>(playingProp);
            SetPlaying = BuildPropertySetter<object, bool>(playingProp);

            // --- Reflect TimeControl constants ---
            KScrubberHeight = (float)(timeControlType.GetField("kScrubberHeight", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) ?? 21f);
            KPlayButtonWidth = (float)(timeControlType.GetField("kPlayButtonWidth", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) ?? 33f);

        }

        // ========================================================================
        // Construction / lifecycle
        // ========================================================================

        private UnityEditor.Editor? _clipEditor;
        private AnimationClip? _clip;

        /// <summary>
        /// The current <see cref="AnimationClip"/> being previewed.
        /// Setting this reuses the cached internal <c>AnimationClipEditor</c> instance.
        /// </summary>
        public AnimationClip? Clip
        {
            get => _clip;
            set
            {
                if (_clip == value) return;
                _clip = value;
                UnityEditor.Editor.CreateCachedEditor(_clip, AnimationClipEditorType, ref _clipEditor);
            }
        }

        /// <summary>
        /// Gets the current internal AvatarPreview instance managed by the active <c>AnimationClipEditor</c>.
        /// </summary>
        private object? AvatarPreview => _clipEditor != null ? GetAvatarPreview(_clipEditor) : null;

        /// <summary>
        /// Creates a new preview drawer and optionally binds an initial <see cref="AnimationClip"/>.
        /// </summary>
        public AnimationPreviewDrawerUtil(AnimationClip? clip = null)
        {
            if (clip != null)
                Clip = clip;
        }

        /// <summary>
        /// Releases the cached editor and cleans up Unity resources.
        /// </summary>
        public void Dispose()
        {
            if (_clipEditor != null)
                UnityEngine.Object.DestroyImmediate(_clipEditor);
        }

        // ========================================================================
        // Drawing
        // ========================================================================

        /// <summary>
        /// Whether the preview GUI can be drawn for the current clip.
        /// </summary>
        public bool HasPreviewGUI => _clipEditor?.HasPreviewGUI() ?? false;

        /// <summary>
        /// Draws the animated preview into the specified <paramref name="rect"/>.
        /// Prevents AvatarPreview from hijacking scroll wheel input when the cursor
        /// is outside the actual 3D viewport (so the inspector scrolls normally).
        /// </summary>
        public void Draw(Rect rect)
        {
            if (_clipEditor == null)
                return;

            var evt = Event.current;

            // Unity's TimeControl header occupies the top portion of the preview rect.
            var timelineRect = GetTimelineRect(rect);
            var insidePreview = rect.Contains(evt.mousePosition) && !timelineRect.Contains(evt.mousePosition);

            // If user scrolls outside the 3D camera viewport, skip preview input this frame.
            if (evt.type == EventType.ScrollWheel && !insidePreview)
                return;
            if(HasPreviewGUI) _clipEditor.OnInteractivePreviewGUI(rect, GUIStyle.none);
        }

        /// <summary>
        /// Calculates the GUI-space rect of the scrub timeline area within the preview header,
        /// excluding the play button, speed slider, and IK toggles.
        /// Useful for compositing overlay controls synchronized with the built-in TimeControl.
        /// </summary>
        public static Rect GetTimelineRect(Rect previewRect)
        {
            // Start at the top of the preview area with the TimeControl's scrubber height
            var rect = new Rect(previewRect.x, previewRect.y, previewRect.width, KScrubberHeight);

            // Skip the play button
            rect.xMin += KPlayButtonWidth;

            // Remove the slider region on the right (AvatarPreview subtracts a fixed 150f)
            rect.xMax -= KSliderWidth;

            // Match Unity's slight vertical inset used around header elements
            rect.yMin += 1f;
            rect.yMax -= 1f;

            return rect;
        }

        // ========================================================================
        // AvatarPreview Toggles
        // ========================================================================

        public bool IKOnFeet
        {
            get => AvatarPreview != null && GetIKOnFeet(AvatarPreview);
            set { if (AvatarPreview != null) SetIKOnFeet(AvatarPreview, value); }
        }

        public bool ShowIKOnFeetButton
        {
            get => AvatarPreview != null && GetShowIKButton(AvatarPreview);
            set { if (AvatarPreview != null) SetShowIKButton(AvatarPreview, value); }
        }

        public bool Is2D
        {
            get => AvatarPreview != null && GetIs2D(AvatarPreview);
            set { if (AvatarPreview != null) SetIs2D(AvatarPreview, value); }
        }

        // ========================================================================
        // Playback / TimeControl
        // ========================================================================

        private object? TimeControl => AvatarPreview != null ? GetTimeControl(AvatarPreview) : null;

        public bool Playing
        {
            get => TimeControl != null && GetPlaying(TimeControl);
            set { if (TimeControl != null) SetPlaying(TimeControl, value); }
        }

        public bool Loop
        {
            get => TimeControl != null && GetLoop(TimeControl);
            set { if (TimeControl != null) SetLoop(TimeControl, value); }
        }

        public float PlaybackSpeed
        {
            get => TimeControl != null ? GetPlaybackSpeed(TimeControl) : 1f;
            set { if (TimeControl != null) SetPlaybackSpeed(TimeControl, value); }
        }

        public float CurrentTime
        {
            get => TimeControl != null ? GetCurrentTime(TimeControl) : 0f;
            set { if (TimeControl != null) SetCurrentTime(TimeControl, value); }
        }

        public float NormalizedTime
        {
            get => TimeControl != null ? GetNormalizedTime(TimeControl) : 0f;
            set { if (TimeControl != null) SetNormalizedTime(TimeControl, value); }
        }

        // ========================================================================
        // Camera Controls
        // ========================================================================

        public Vector2 PreviewAngles
        {
            get => AvatarPreview != null ? GetPreviewDir(AvatarPreview) : Vector2.zero;
            set { if (AvatarPreview != null) SetPreviewDir(AvatarPreview, value); }
        }

        public float ZoomFactor
        {
            get => AvatarPreview != null ? GetZoomFactor(AvatarPreview) : 1f;
            set { if (AvatarPreview != null) SetZoomFactor(AvatarPreview, value); }
        }

        public Vector3 PivotOffset
        {
            get => AvatarPreview != null ? GetPivotOffset(AvatarPreview) : Vector3.zero;
            set { if (AvatarPreview != null) SetPivotOffset(AvatarPreview, value); }
        }

        public Camera? Camera => AvatarPreview != null ? GetCamera(AvatarPreview) : null;

        // ========================================================================
        // Delegate builder helpers
        // ========================================================================

        private static Func<TTarget, TField> BuildFieldGetter<TTarget, TField>(FieldInfo field)
        {
            var target = Expression.Parameter(typeof(TTarget), "t");
            var body = Expression.Convert(Expression.Field(Expression.Convert(target, field.DeclaringType!), field), typeof(TField));
            return Expression.Lambda<Func<TTarget, TField>>(body, target).Compile();
        }

        private static Action<TTarget, TField> BuildFieldSetter<TTarget, TField>(FieldInfo field)
        {
            var target = Expression.Parameter(typeof(TTarget), "t");
            var value = Expression.Parameter(typeof(TField), "v");
            var body = Expression.Assign(
                Expression.Field(Expression.Convert(target, field.DeclaringType!), field),
                Expression.Convert(value, field.FieldType)
            );
            return Expression.Lambda<Action<TTarget, TField>>(body, target, value).Compile();
        }

        private static Func<TTarget, TProp> BuildPropertyGetter<TTarget, TProp>(PropertyInfo prop)
        {
            var target = Expression.Parameter(typeof(TTarget), "t");
            var body = Expression.Convert(Expression.Property(Expression.Convert(target, prop.DeclaringType!), prop), typeof(TProp));
            return Expression.Lambda<Func<TTarget, TProp>>(body, target).Compile();
        }

        private static Action<TTarget, TProp> BuildPropertySetter<TTarget, TProp>(PropertyInfo prop)
        {
            var target = Expression.Parameter(typeof(TTarget), "t");
            var value = Expression.Parameter(typeof(TProp), "v");
            var body = Expression.Assign(
                Expression.Property(Expression.Convert(target, prop.DeclaringType!), prop),
                Expression.Convert(value, prop.PropertyType)
            );
            return Expression.Lambda<Action<TTarget, TProp>>(body, target, value).Compile();
        }

        private static Func<TTarget, TResult> BuildNestedPropertyGetter<TTarget, TResult>(FieldInfo nestedField, PropertyInfo nestedProp)
        {
            var target = Expression.Parameter(typeof(TTarget), "t");
            var nested = Expression.Field(Expression.Convert(target, nestedField.DeclaringType!), nestedField);
            var propAccess = Expression.Property(Expression.Convert(nested, nestedProp.DeclaringType!), nestedProp);
            var body = Expression.Convert(propAccess, typeof(TResult));
            return Expression.Lambda<Func<TTarget, TResult>>(body, target).Compile();
        }
    }
}
