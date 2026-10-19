using System.IO;
using UnityEditor;
using UnityEngine;
using UnityJigs.Lanes;
#if UNITY_6000_3_OR_NEWER
using System.Reflection;
using UnityEditor.Toolbars;
using UnityEngine.UIElements;
#endif

namespace UnityJigs.Editor.Lanes
{
    /// <summary>Tells the editors of several lanes apart (Lanes~/README.md): "[Lane B · branch]" at the
    /// front of the window title (and so the taskbar), and, on Unity 6000.3+, a button in the lane's colour
    /// on the main toolbar. The main checkout shows neither. Click opens the lane's folder; right-click
    /// copies its project path for the Unity CLI (--project-path).</summary>
    /// <remarks>The button leans on Unity internals (6000.3), each with a plain fallback: a custom toolbar
    /// element starts hidden, so it is shown through the internal <c>MainToolbar.SetDisplayedAll</c> (else:
    /// tick it in the toolbar's ⋮ menu); and the toolbar API has no colour, so the button is found in
    /// <c>MainToolbarWindow</c>'s visual tree and filled with the lane colour (else: the icon alone carries
    /// it). Where it sits is the user's: drag it.</remarks>
    [InitializeOnLoad]
    internal static class LaneToolbar
    {
        private const string ElementPath = "UnityJigs/Lane";
        private const string OverlayName = "UnityJigs/Lane"; // the toolbar names the element's overlay so
        private const double PollSeconds = 2;

        // Lanes B to J: red, green, blue, cyan, magenta, yellow, brown, gray, black.
        private static readonly Color[] Colors =
        {
            new(0.90f, 0.20f, 0.20f),
            new(0.20f, 0.75f, 0.30f),
            new(0.25f, 0.45f, 0.95f),
            new(0.15f, 0.80f, 0.85f),
            new(0.85f, 0.30f, 0.80f),
            new(0.95f, 0.85f, 0.20f),
            new(0.55f, 0.35f, 0.18f),
            new(0.55f, 0.55f, 0.55f),
            new(0.05f, 0.05f, 0.05f)
        };

        // A worktree folder whose name has no lane letter.
        private static readonly Color NoLetterColor = new(0.85f, 0.85f, 0.85f);

        private static string? _Branch;
        private static double _NextPoll;

        static LaneToolbar()
        {
            if (Lane.Id == null) return;
            _Branch = ReadBranch();
            EditorApplication.updateMainWindowTitle += OnUpdateTitle;
            EditorApplication.update += Poll;
            EditorApplication.UpdateMainWindowTitle();
        }

        public static Color LaneColor => Lane.Index > 0 ? Colors[Lane.Index - 1] : NoLetterColor;

        private static string Label => Lane.Index > 0 ? "Lane " + Lane.Id : Lane.Id ?? "";

        private static string LabelWithBranch => _Branch != null ? Label + " · " + _Branch : Label;

        private static string ProjectPath => Path.GetDirectoryName(Application.dataPath)!;

        private static void OpenFolder()
        {
            if (Lane.RepoRoot != null) EditorUtility.OpenWithDefaultApp(Lane.RepoRoot);
        }

        private static void OnUpdateTitle(ApplicationTitleDescriptor title) =>
            title.title = $"[{LabelWithBranch}] {title.title}";

        private static void Poll()
        {
            if (EditorApplication.timeSinceStartup < _NextPoll) return;
            _NextPoll = EditorApplication.timeSinceStartup + PollSeconds;
#if UNITY_6000_3_OR_NEWER
            if (!_Shown) _Shown = ShowElement();
#endif
            var branch = ReadBranch();
            if (branch != _Branch)
            {
                _Branch = branch;
#if UNITY_6000_3_OR_NEWER
                MainToolbar.Refresh(ElementPath);
#endif
                EditorApplication.UpdateMainWindowTitle();
            }
#if UNITY_6000_3_OR_NEWER
            // A refresh or a layout change rebuilds the button: style the new one.
            if (_Styled?.panel == null) _Styled = StyleButton();
#endif
        }

        /// <summary>The worktree's checked-out branch: its <c>.git</c> file names its git folder
        /// ("gitdir: …/.git/worktrees/&lt;name&gt;"), whose HEAD is "ref: refs/heads/&lt;branch&gt;".
        /// Null when detached or unreadable.</summary>
        private static string? ReadBranch()
        {
            const string GitDirPrefix = "gitdir:";
            const string RefPrefix = "ref: refs/heads/";
            if (Lane.RepoRoot == null) return null;
            try
            {
                var link = File.ReadAllText(Path.Combine(Lane.RepoRoot, ".git")).Trim();
                if (!link.StartsWith(GitDirPrefix)) return null;
                var gitDir = link[GitDirPrefix.Length..].Trim();
                if (!Path.IsPathRooted(gitDir)) gitDir = Path.Combine(Lane.RepoRoot, gitDir);

                var head = File.ReadAllText(Path.Combine(gitDir, "HEAD")).Trim();
                return head.StartsWith(RefPrefix) ? head[RefPrefix.Length..] : null;
            }
            catch (IOException)
            {
                return null;
            }
        }

#if UNITY_6000_3_OR_NEWER
        private static bool _Shown;
        private static VisualElement? _Styled;
        private static Texture2D? _Icon;

        // Left zone, after Unity's AI dropdown (its index is 12).
        [MainToolbarElement(ElementPath, defaultDockPosition = MainToolbarDockPosition.Left, defaultDockIndex = 13)]
        private static MainToolbarElement CreateButton()
        {
            var lane = Lane.Id != null;
            var tooltip = lane
                ? $"{Label}: {Lane.RepoRoot}\nBranch: {_Branch ?? "(detached)"}\nSaves: {Lane.SaveFolder}\n" +
                  "Click: open the folder. Right-click: copy the --project-path for the Unity CLI."
                : "";
            var content = new MainToolbarContent(lane ? LabelWithBranch : "", lane ? Icon() : null, tooltip);
            return new MainToolbarButton(content, OpenFolder)
            {
                displayed = lane,
                populateContextMenu = menu =>
                    menu.AppendAction("Copy --project-path", _ => EditorGUIUtility.systemCopyBuffer = ProjectPath)
            };
        }

        private static bool ShowElement()
        {
            var setDisplayed = typeof(MainToolbar).GetMethod("SetDisplayedAll",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (setDisplayed == null) return true; // gone in this Unity: tick it in the ⋮ menu
            setDisplayed.Invoke(null, new object[] { ElementPath, true });
            return true;
        }

        private static VisualElement? StyleButton()
        {
            var windowType = typeof(EditorWindow).Assembly.GetType("UnityEditor.MainToolbarWindow");
            if (windowType == null) return null;

            foreach (var window in Resources.FindObjectsOfTypeAll(windowType))
            {
                var button = ((EditorWindow)window).rootVisualElement.Q(OverlayName)
                    ?.Q(className: "unity-toolbar-button");
                if (button == null) continue;

                var fill = LaneColor;
                var light = 0.299f * fill.r + 0.587f * fill.g + 0.114f * fill.b > 0.6f;
                button.style.backgroundColor = fill;
                button.style.color = light ? Color.black : Color.white;
                button.style.unityFontStyleAndWeight = FontStyle.Bold;
                return button;
            }

            return null;
        }

        // A square in the lane colour: invisible on the filled button, the colour itself when the button
        // can't be filled.
        private static Texture2D Icon()
        {
            if (_Icon != null) return _Icon;

            const int Size = 32;
            _Icon = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color[Size * Size];
            for (var i = 0; i < pixels.Length; i++) pixels[i] = LaneColor;
            _Icon.SetPixels(pixels);
            _Icon.Apply();
            return _Icon;
        }
#endif
    }
}
