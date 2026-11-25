using System;
using System.Collections.Generic;
using System.IO;
using JetBrains.Annotations;
using Sirenix.OdinInspector;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityJigs.Attributes;
using UnityJigs.Extensions;

namespace UnityJigs.Editor.EnumWindow
{
    // One design enum in the Enums window: what a designer may do with it, its values, and the edits it declares.
    // Every edit rewrites the enum's own source file (EnumSource) and lets Unity recompile; the file is under version
    // control, so that is the undo.
    [UsedImplicitly(ImplicitUseTargetFlags.Members)]
    internal sealed class EnumType
    {
        public readonly Type Type;
        public readonly DesignEnumAttribute Attribute;
        public readonly string Label;   // "Colors", or "Owner.States" for one declared inside a class
        public readonly string Folder;  // its namespace, as a menu path
        public readonly string SearchText;

        private readonly string? _assetPath; // project-relative, or null when the window must not write the file
        private readonly string? _pathProblem;

        private EnumSource? _source;
        private DateTime _readTime;
        private string _newName = "";
        private string? _confirmRemove;
        private string? _problem; // the last edit that failed, said once

        // Survives the recompile an edit causes, so the panel can say what was just done.
        private const string DoneKey = "UnityJigs.EnumWindow.Done";
        private int _fieldId;
        private Rect _fieldRect;

        public EnumType(Type type)
        {
            Type = type;
            Attribute = type.GetAttribute<DesignEnumAttribute>() ?? throw new NotSupportedException();
            Label = type.DeclaringType != null ? $"{type.DeclaringType.Name}.{type.Name}" : type.Name;
            Folder = string.IsNullOrEmpty(type.Namespace) ? "(no namespace)" : type.Namespace!.Replace('.', '/');
            SearchText = Label + " " + Folder + " " + string.Join(" ", Enum.GetNames(type));
            (_assetPath, _pathProblem) = ProjectPath(Attribute.File);
        }

        public bool Matches(string search)
        {
            foreach (var word in search.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                if (SearchText.IndexOf(word, StringComparison.OrdinalIgnoreCase) < 0) return false;
            return true;
        }

        private static (string?, string?) ProjectPath(string file)
        {
            if (string.IsNullOrEmpty(file) || !File.Exists(file)) return (null, "Its file can't be found on this computer.");
            var full = Path.GetFullPath(file).Replace('\\', '/');
            var root = Path.GetFullPath(Path.GetDirectoryName(Application.dataPath)!).Replace('\\', '/').TrimEnd('/') + "/";
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return (null, "Its file is outside this project.");
            var relative = full.Substring(root.Length);
            if (relative.StartsWith("Library/", StringComparison.OrdinalIgnoreCase))
                return (null, "Its file is in a package this project only borrows.");
            return (relative, null);
        }

        // ---- reading

        private EnumSource? Source
        {
            get
            {
                if (_assetPath == null) return null;
                var time = File.GetLastWriteTimeUtc(Attribute.File);
                if (_source == null || time != _readTime)
                {
                    _source = EnumSource.Read(File.ReadAllBytes(Attribute.File), Type.Name);
                    _readTime = time;
                }
                return _source;
            }
        }

        // Why the window won't change this enum's file right now, or null when it will.
        private string? Blocked()
        {
            if (_pathProblem != null) return _pathProblem;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return "Unity is updating. One moment.";
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "Stop play mode first.";
            var source = Source!;
            if (source.Refusal != null)
                return $"This window can't change this list safely: {char.ToLowerInvariant(source.Refusal[0])}{source.Refusal.Substring(1)} " +
                       "A programmer can, with Open the code below.";
            // The file and what Unity compiled must agree, or an edit would be made on top of something unseen.
            var compiled = new Dictionary<string, long>();
            foreach (var name in Enum.GetNames(Type)) compiled[name] = Convert.ToInt64(Enum.Parse(Type, name));
            if (compiled.Count != source.Members.Count) return Disagree;
            foreach (var m in source.Members)
                if (!compiled.TryGetValue(m.Name, out var n) || n != m.Number) return Disagree;
            return null;
        }

        private const string Disagree =
            "The file has changed since Unity last compiled it. Wait for Unity to finish, or ask a programmer.";

        // ---- drawing

        [OnInspectorGUI, UsedImplicitly]
        private void Draw()
        {
            Styles.Init();
            GUILayout.Space(4);
            GUILayout.Label(Label, Styles.Title);
            GUILayout.Label(Folder.Replace("/", "  ›  "), Styles.Faint);
            GUILayout.Space(8);

            // What the last edit did, shown while the file is still as that edit left it.
            var done = SessionState.GetString(DoneKey, "").Split('|');
            if (done.Length == 3 && done[0] == Type.FullName && Source != null && done[1] == Fingerprint(Source.Bytes))
                EditorGUILayout.HelpBox(done[2], MessageType.Info);

            DrawAllowed();
            GUILayout.Space(8);

            var blocked = Blocked();
            if (blocked != null) EditorGUILayout.HelpBox(blocked, MessageType.Warning);
            if (_problem != null) EditorGUILayout.HelpBox(_problem, MessageType.Error);

            DrawValues(blocked == null);
            if (Attribute.Allows(DesignEnumTraits.Addable)) DrawAdd(blocked == null);

            GUILayout.FlexibleSpace();
            GUILayout.Space(12);
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Label("For programmers:", Styles.Faint, GUILayout.ExpandWidth(false));
                if (GUILayout.Button(new GUIContent("Open the code", Attribute.File), GUILayout.ExpandWidth(false)))
                    InternalEditorUtility.OpenFileAtLineExternal(Attribute.File, Attribute.Line);
                GUILayout.Label(_assetPath ?? Attribute.File, Styles.Faint);
            }
            GUILayout.Space(4);
        }

        private void DrawAllowed()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label("What you can do with this list", EditorStyles.boldLabel);
                Line(DesignEnumTraits.Addable, "Add new entries", "a new entry would do nothing without new code");
                Line(DesignEnumTraits.Removable, "Remove entries", "the game's code relies on these entries");
                Line(DesignEnumTraits.Reorderable, "Change the order", "the game's code relies on their order");
                GUILayout.Space(2);
                GUILayout.Label(Attribute.Allows(DesignEnumTraits.Readable)
                        ? "Animators use these numbers. An entry keeps its number whatever else changes."
                        : "No animator uses these numbers.",
                    Styles.Wrapped);
                var problem = Attribute.Problem;
                if (problem != null) EditorGUILayout.HelpBox(problem + " A programmer should fix its [DesignEnum].", MessageType.Warning);
            }

            void Line(DesignEnumTraits trait, string what, string whyNot)
            {
                var yes = Attribute.Allows(trait);
                using (new GUILayout.HorizontalScope())
                {
                    GUILayout.Label(yes ? "✓" : "✗", yes ? Styles.Yes : Styles.No, GUILayout.Width(18));
                    GUILayout.Label(what, GUILayout.Width(130));
                    GUILayout.Label(yes ? "Yes" : $"No, ask a programmer: {whyNot}.", yes ? Styles.Yes : Styles.Faint);
                }
            }
        }

        private void DrawValues(bool editable)
        {
            GUILayout.Label("Entries", EditorStyles.boldLabel);
            var members = Source?.Refusal == null ? Source?.Members : null;
            var names = new List<(string name, long number)>();
            if (members != null) foreach (var m in members) names.Add((m.Name, m.Number));
            else foreach (var n in Enum.GetNames(Type)) names.Add((n, Convert.ToInt64(Enum.Parse(Type, n))));

            var reorder = editable && Attribute.Allows(DesignEnumTraits.Reorderable);
            var remove = editable && Attribute.Allows(DesignEnumTraits.Removable);
            // One width for every name, so the numbers line up under a long one.
            var nameWidth = 160f;
            foreach (var (n, _) in names) nameWidth = Mathf.Max(nameWidth, EditorStyles.label.CalcSize(new GUIContent(n)).x + 8);
            for (var i = 0; i < names.Count; i++)
            {
                var (name, number) = names[i];
                using (new GUILayout.HorizontalScope())
                {
                    GUILayout.Label(name, GUILayout.Width(nameWidth));
                    GUILayout.Label(number.ToString(), Styles.Number, GUILayout.Width(40));
                    if (Attribute.Allows(DesignEnumTraits.Reorderable))
                    {
                        using (new EditorGUI.DisabledScope(!reorder || i == 0))
                            if (GUILayout.Button(new GUIContent("▲", $"Move {name} up"), GUILayout.Width(26))) Do(s => s.Move(name, -1), $"Moved {name} up.");
                        using (new EditorGUI.DisabledScope(!reorder || i == names.Count - 1))
                            if (GUILayout.Button(new GUIContent("▼", $"Move {name} down"), GUILayout.Width(26))) Do(s => s.Move(name, 1), $"Moved {name} down.");
                    }
                    if (Attribute.Allows(DesignEnumTraits.Removable))
                    {
                        using (new EditorGUI.DisabledScope(!remove || names.Count == 1))
                        {
                            if (_confirmRemove == name)
                            {
                                if (GUILayout.Button($"Yes, remove {name}", GUILayout.ExpandWidth(false)))
                                {
                                    _confirmRemove = null;
                                    Do(s => s.Remove(name), $"Removed {name}. Anything that used its number ({number}) now shows a bare number.");
                                }
                                if (GUILayout.Button("Keep it", GUILayout.ExpandWidth(false))) _confirmRemove = null;
                            }
                            else if (GUILayout.Button(new GUIContent("Remove", $"Remove {name}"), GUILayout.Width(64)))
                                _confirmRemove = name;
                        }
                    }
                    GUILayout.FlexibleSpace();
                }
            }
        }

        private void DrawAdd(bool editable)
        {
            GUILayout.Space(10);
            GUILayout.Label("Add an entry", EditorStyles.boldLabel);
            var source = Source;
            var why = !editable || source == null ? null : source.NameProblem(_newName.Trim());
            using (new GUILayout.HorizontalScope())
            {
                // Return in the field does what the Add button does.
                var e = Event.current;
                var enter = e.type == EventType.KeyDown && e.keyCode is KeyCode.Return or KeyCode.KeypadEnter &&
                            _fieldId != 0 && GUIUtility.keyboardControl == _fieldId;
                if (enter) e.Use();
                // Read before the field draws, against where it was last drawn: the field uses up the click, and
                // during a click the layout has no rectangle to give.
                var clicked = e.type == EventType.MouseDown && _fieldRect.Contains(e.mousePosition);
                using (new EditorGUI.DisabledScope(!editable))
                {
                    _newName = EditorGUILayout.TextField(_newName, GUILayout.MinWidth(200));
                    if (e.type == EventType.Repaint) _fieldRect = GUILayoutUtility.GetLastRect();
                    // The field's control is the one that took the keyboard when it was clicked.
                    if (clicked) _fieldId = GUIUtility.keyboardControl;
                }
                using (new EditorGUI.DisabledScope(!editable || why != null))
                {
                    if (GUILayout.Button("Add", GUILayout.Width(60)) || (enter && editable && why == null))
                    {
                        var name = _newName.Trim();
                        Do(s => s.Add(name, out _), $"Added {name}. Once Unity has updated, it can be picked wherever this list is used.",
                            () => _newName = "");
                    }
                }
                GUILayout.FlexibleSpace();
            }
            if (editable && source != null)
            {
                var next = 0L;
                foreach (var m in source.Members) next = Math.Max(next, m.Number + 1);
                GUILayout.Label(why != null && _newName.Length > 0 ? why : $"A new entry gets the number {next}.",
                    why != null && _newName.Length > 0 ? Styles.Warn : Styles.Faint);
            }
        }

        // ---- writing

        // Writes the edit, then imports the file and asks for the recompile at once (an editor in the background barely
        // ticks, so nothing deferred would run), and ends this GUI pass: the window is redrawn after the reload.
        private void Do(Func<EnumSource, byte[]> edit, string done, Action? written = null)
        {
            _problem = null;
            try
            {
                var blocked = Blocked();
                if (blocked != null) throw new InvalidOperationException(blocked);
                var source = Source!;
                // Never write over a change made since the file was read.
                var now = File.ReadAllBytes(Attribute.File);
                if (!Same(now, source.Bytes)) throw new InvalidOperationException(Disagree);
                var bytes = edit(source);
                File.WriteAllBytes(Attribute.File, bytes);
                _source = null;
                // Keyed by the file's content, not its time: macOS can report a write's time differently a moment later.
                SessionState.SetString(DoneKey, $"{Type.FullName}|{Fingerprint(bytes)}|{done}");
                GUIUtility.keyboardControl = 0;
                written?.Invoke();
            }
            catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                _problem = "Nothing was changed: " + e.Message;
                return;
            }
            AssetDatabase.ImportAsset(_assetPath, ImportAssetOptions.ForceUpdate);
            UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
            GUIUtility.ExitGUI();
        }

        private static string Fingerprint(byte[] bytes) => Hash128.Compute(bytes).ToString();

        private static bool Same(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (var i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private static class Styles
        {
            public static GUIStyle Title = null!, Faint = null!, Yes = null!, No = null!, Number = null!, Warn = null!, Wrapped = null!;
            private static bool _ready;

            public static void Init()
            {
                if (_ready) return;
                _ready = true;
                Title = new GUIStyle(EditorStyles.boldLabel) { fontSize = 16 };
                Faint = new GUIStyle(EditorStyles.label) { normal = { textColor = new Color(0.55f, 0.57f, 0.6f) } };
                Yes = new GUIStyle(EditorStyles.label) { normal = { textColor = new Color(0.45f, 0.78f, 0.5f) } };
                No = new GUIStyle(EditorStyles.label) { normal = { textColor = new Color(0.78f, 0.45f, 0.45f) } };
                Number = new GUIStyle(Faint) { alignment = TextAnchor.MiddleRight };
                Warn = new GUIStyle(EditorStyles.label) { normal = { textColor = new Color(0.95f, 0.75f, 0.3f) }, wordWrap = true };
                Wrapped = new GUIStyle(EditorStyles.label) { wordWrap = true };
            }
        }
    }
}
