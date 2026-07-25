using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Unity.AI.MCP.Editor.Helpers;
using Unity.AI.MCP.Editor.ToolRegistry;
using UnityEditor;
using UnityEngine;

namespace UnityJigs.Assistant.Editor
{
    /// Console log types to include.
    public enum LogSeverities
    {
        Info,
        Warning,
        Error,
        All
    }

    /// How much of the log history to return.
    public enum SinceModes
    {
        /// Every entry currently in the console.
        All,

        /// Only entries added since the previous Unity.Logs call this editor session.
        LastRead
    }

    /// Which entries carry their stack trace.
    public enum StackModes
    {
        /// Errors only. Stacks are ~90% of console payload, and only errors need them.
        Errors,

        None,
        All
    }

    /// Parameters for the Unity.Logs tool.
    public record JigsLogsParams
    {
        [McpDescription("Log types to include", Required = false)]
        public LogSeverities[] Types { get; set; } = { LogSeverities.All };

        [McpDescription("Return everything, or only entries added since the last Unity.Logs call",
            Required = false, Default = SinceModes.All)]
        public SinceModes Since { get; set; } = SinceModes.All;

        [McpDescription("Which entries include their stack trace", Required = false, Default = StackModes.Errors)]
        public StackModes Stacks { get; set; } = StackModes.Errors;

        [McpDescription("Case-insensitive substring filter applied to messages", Required = false)]
        public string Filter { get; set; }

        [McpDescription("Max grouped rows to return. Errors and warnings are never dropped.",
            Required = false, Default = 200)]
        public int Max { get; set; } = 200;

        [McpDescription("Run AssetDatabase.Refresh() first so edits made outside Unity are compiled and their " +
                        "errors visible. Leave on unless you know nothing changed.",
            Required = false, Default = true)]
        public bool Refresh { get; set; } = true;
    }

    /// Which matching entry Unity.LogDetail should render.
    public enum Occurrences
    {
        /// Most recent match — usually the one you just caused.
        Last,

        First,
        All
    }

    /// Parameters for the Unity.LogDetail tool.
    public record JigsLogDetailParams
    {
        [McpDescription("File name or path fragment, e.g. 'SkaterFail.cs'", Required = false)]
        public string File { get; set; }

        // Nullable on purpose: a non-nullable int with no Default is emitted as a REQUIRED property in the
        // generated MCP schema, which would force a line number on every call.
        [McpDescription("Line number, to disambiguate several call sites in one file", Required = false)]
        public int? Line { get; set; }

        [McpDescription("Case-insensitive substring of the message, as an alternative to File/Line",
            Required = false)]
        public string Match { get; set; }

        [McpDescription("Which entry to render when several match", Required = false,
            Default = Occurrences.Last)]
        public Occurrences Occurrence { get; set; } = Occurrences.Last;

        [McpDescription("Cap on entries rendered when Occurrence is All", Required = false, Default = 5)]
        public int Max { get; set; } = 5;
    }

    /// Token-efficient Unity console reader.
    ///
    /// Replaces the stock Unity.GetConsoleLogs / Unity.ReadConsole, both of which return the stack trace
    /// glued into the message field regardless of their own IncludeStacktrace flag — so a read costs ~10x
    /// what the information is worth. This one:
    ///   * splits message from callstack at LogEntry.callstackTextStartUTF16 (an exact index Unity already
    ///     computed — no heuristic parsing),
    ///   * takes the call site from LogEntry.file/line rather than reconstructing it from the trace,
    ///   * run-length collapses CONSECUTIVE entries sharing a call site. Only consecutive: interleaved
    ///     entries carry ordering signal, and flattening them hides repeating cycles.
    public static class ConsoleLogsMcpTool
    {
        const string Title = "Read Unity console (compact)";

        const string Description =
            "Reads the Unity Editor console, compactly. Prefer this over Unity.GetConsoleLogs and " +
            "Unity.ReadConsole: those inline the full stack trace into every message (~90% waste) and " +
            "ignore their own stack-trace flag.\n\n" +
            "Returns a header line (counts by severity, time span, how many entries are new since your last " +
            "call) followed by one row per run of consecutive entries sharing a call site, each row " +
            "'SEV file:line xN time'. Runs of the same call site with differing text show the shared " +
            "template plus the varying parts.\n\n" +
            "Args:\n" +
            "    Types: log types to include (Info, Warning, Error, All).\n" +
            "    Since: All, or LastRead for only what is new since the previous call.\n" +
            "    Stacks: Errors (default), None, or All.\n" +
            "    Filter: case-insensitive substring match on messages.\n" +
            "    Max: max rows; errors and warnings are never dropped.\n" +
            "    Refresh: AssetDatabase.Refresh() first so external edits compile (default true).";

        // ---- LogEntries/LogEntry reflection -------------------------------------------------------

        static MethodInfo _start, _end, _getCount, _getEntry, _clear, _getTimestamp;
        static FieldInfo _fMessage, _fFile, _fLine, _fMode, _fCallstackStart, _fColumn, _fInstanceId;
        static Type _entryType, _modeType;
        static string _reflectionError;

        static ConsoleLogsMcpTool()
        {
            try
            {
                var asm = typeof(EditorApplication).Assembly;
                var entriesType = asm.GetType("UnityEditor.LogEntries");
                _entryType = asm.GetType("UnityEditor.LogEntry");
                if (entriesType == null || _entryType == null)
                    throw new Exception("UnityEditor.LogEntries / LogEntry not found");

                const BindingFlags stat = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                _start = Require(entriesType.GetMethod("StartGettingEntries", stat), "StartGettingEntries");
                _end = Require(entriesType.GetMethod("EndGettingEntries", stat), "EndGettingEntries");
                _getCount = Require(entriesType.GetMethod("GetCount", stat), "GetCount");
                _getEntry = Require(entriesType.GetMethod("GetEntryInternal", stat), "GetEntryInternal");
                _clear = Require(entriesType.GetMethod("Clear", stat), "Clear");

                // Optional: absent on older editors. Entries just lose their timestamps.
                _getTimestamp = entriesType.GetMethod("GetEntryTimestampInternal", stat);

                _fMessage = Require(_entryType.GetField("message", inst), "LogEntry.message");
                _fFile = Require(_entryType.GetField("file", inst), "LogEntry.file");
                _fLine = Require(_entryType.GetField("line", inst), "LogEntry.line");
                _fMode = Require(_entryType.GetField("mode", inst), "LogEntry.mode");

                // The whole point of this tool. Optional so a rename degrades to "keep the whole blob"
                // rather than breaking the reader outright.
                ResolveModeMasks();

                // Detail-view extras. Optional: their absence costs a line in Unity.LogDetail, nothing more.
                _fColumn = _entryType.GetField("column", inst);
                _fInstanceId = _entryType.GetField("instanceID", inst);

                _fCallstackStart = _entryType.GetField("callstackTextStartUTF16", inst);
                if (_fCallstackStart == null)
                    Debug.LogError("[Unity.Logs] OUTDATED REFLECTION: LogEntry.callstackTextStartUTF16 is gone. " +
                                   "Messages will include their stack traces until the field is re-targeted in " +
                                   "UnityJigs/Assistant/Editor/ConsoleLogsMcpTool.cs.");
            }
            catch (Exception e)
            {
                _reflectionError = e.Message;
            }
        }

        static T Require<T>(T member, string name) where T : class
        {
            if (member == null) throw new Exception($"could not resolve {name}");
            return member;
        }

        /// These reports are built with AppendLine, so on Windows every break is CRLF — and the transport
        /// JSON-encodes the result, so each one reaches the reader as a literal "\r\n". Collapsing to "\n"
        /// halves that noise. It can't be removed entirely: the bridge requires the {success, message}
        /// envelope (returning a bare string fails the call outright), so the escaping is inherent.
        static string Normalise(string report) => report.Replace("\r\n", "\n");

        // ---- Entry model ---------------------------------------------------------------------------

        enum Sev
        {
            Info,
            Warning,
            Error
        }

        struct Entry
        {
            public string Message; // head only — stack stripped
            public string Stack; // null when the entry had none
            public string File;
            public int Line;
            public int Column;
            public int Mode;
            public int InstanceId; // context object from Debug.Log(msg, obj); 0 when none
            public int Index; // position in the console, for Unity.LogDetail
            public Sev Severity;
            public string Time; // "18:24:54", or "" when unavailable
        }

        // Severity is a mask test over UnityEditor.ConsoleWindow.Mode, resolved by NAME at load so the bit
        // positions can move between Unity versions without silently misclassifying everything.
        //
        // Do NOT shortcut this to "Error|Warning|Log" base bits: compiler diagnostics don't set one. A CS
        // warning's mode is ScriptCompileWarning|DontExtractStacktrace (266240 on 6000.3) with no base bit,
        // so a three-bit test reports every compile warning as Info — which is exactly the bug this replaced.
        static int _errorMask = 1 | 2 | 16 | 64 | 256 | 2048 | 8192 | 32768 | 65536 | 131072 | 1048576 |
                                2097152 | 4194304;

        static int _warningMask = 128 | 512 | 4096;

        static readonly string[] ErrorFlagNames =
        {
            "Error", "Assert", "Fatal", "AssetImportError", "ScriptingError", "ScriptCompileError",
            "ScriptingException", "GraphCompileError", "ScriptingAssertion", "StickyError", "ReportBug",
            "DisplayPreviousErrorInStatusBar", "VisualScriptingError"
        };

        static readonly string[] WarningFlagNames =
        {
            "AssetImportWarning", "ScriptingWarning", "ScriptCompileWarning"
        };

        static void ResolveModeMasks()
        {
            _modeType = typeof(EditorApplication).Assembly
                .GetType("UnityEditor.ConsoleWindow")
                ?.GetNestedType("Mode", BindingFlags.Public | BindingFlags.NonPublic);
            if (_modeType == null) return; // keep the numeric fallbacks

            var error = Accumulate(_modeType, ErrorFlagNames);
            var warning = Accumulate(_modeType, WarningFlagNames);

            // Only adopt reflected values if both resolved to something; a partial read would be worse
            // than the fallback.
            if (error == 0 || warning == 0) return;
            _errorMask = error;
            _warningMask = warning;
        }

        static int Accumulate(Type modeType, string[] names)
        {
            var mask = 0;
            for (var i = 0; i < names.Length; i++)
                if (Enum.TryParse(modeType, names[i], out var value))
                    mask |= Convert.ToInt32(value);
            return mask;
        }

        static Sev SeverityOf(int mode)
        {
            if ((mode & _errorMask) != 0) return Sev.Error;
            if ((mode & _warningMask) != 0) return Sev.Warning;
            return Sev.Info;
        }

        // ---- Read cursor ---------------------------------------------------------------------------
        // LogEntry has no stable id (identifier is always 0, globalLineIndex is just the row index), so
        // "what's new" is synthesized: remember how many entries we had consumed and a fingerprint of the
        // last one. If that fingerprint still matches, the console only grew and the delta is real; if it
        // doesn't, the console was cleared (clear-on-play does this constantly) and we re-baseline.
        // SessionState survives domain reloads and dies with the editor, which is exactly the lifetime we want.
        const string KeyCount = "UnityJigs.Logs.Count";
        const string KeyFingerprint = "UnityJigs.Logs.Fingerprint";

        static string Fingerprint(in Entry e)
        {
            var head = e.Message.Length <= 32 ? e.Message : e.Message.Substring(0, 32);
            return $"{e.File}|{e.Line}|{e.Mode}|{e.Message.Length}|{head}";
        }

        // ---- Tools ---------------------------------------------------------------------------------

        [McpTool("Unity.Logs", Description, Title, Groups = new[] { "debug", "editor" })]
        public static object ReadLogs(JigsLogsParams parameters)
        {
            if (_reflectionError != null)
                return Response.Error($"Unity.Logs cannot read the console: {_reflectionError}. " +
                                      "UnityEditor's internal LogEntries API has changed; re-target the " +
                                      "reflection in UnityJigs/Assistant/Editor/ConsoleLogsMcpTool.cs.");

            var p = parameters ?? new JigsLogsParams();
            if (p.Refresh) AssetDatabase.Refresh();

            var all = new List<Entry>();
            try
            {
                ReadAll(all);
            }
            catch (Exception e)
            {
                return Response.Error($"Failed reading console entries: {e.Message}");
            }

            // Cursor: how many of these have we already shown?
            var storedCount = SessionState.GetInt(KeyCount, 0);
            var storedFingerprint = SessionState.GetString(KeyFingerprint, "");
            var continuous = storedCount > 0
                             && storedCount <= all.Count
                             && Fingerprint(all[storedCount - 1]) == storedFingerprint;
            var newFrom = continuous ? storedCount : 0;
            var wasReset = storedCount > 0 && !continuous;

            // Re-baseline before any early return, so a filtered read still advances the cursor.
            SessionState.SetInt(KeyCount, all.Count);
            SessionState.SetString(KeyFingerprint, all.Count > 0 ? Fingerprint(all[all.Count - 1]) : "");

            var from = p.Since == SinceModes.LastRead ? newFrom : 0;

            var kept = new List<Entry>();
            for (var i = from; i < all.Count; i++)
            {
                var e = all[i];
                if (!WantsSeverity(p.Types, e.Severity)) continue;
                if (!string.IsNullOrEmpty(p.Filter) &&
                    e.Message.IndexOf(p.Filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                kept.Add(e);
            }

            var groups = GroupConsecutive(kept);
            var dropped = Trim(groups, p.Max);

            var report = Render(groups, all, kept.Count, all.Count - newFrom, wasReset, dropped, p);

            return Response.Success(Normalise(report));
        }

        const string DetailTitle = "Inspect one console entry in full";

        const string DetailDescription =
            "Full, uncollapsed detail for a single console entry: complete message, complete stack trace, " +
            "file/line/column, decoded mode flags, and the context object the log was attached to " +
            "(Debug.Log(msg, obj)).\n\n" +
            "Use after Unity.Logs when a row isn't enough — Unity.Logs collapses runs, hides stacks on " +
            "non-errors, and may template a message down to its shared parts.\n\n" +
            "Address the entry with the same 'file:line' the Unity.Logs row printed. With no arguments it " +
            "returns the most recent error, or the most recent entry if there are none.\n\n" +
            "Args:\n" +
            "    File: file name or path fragment, e.g. 'SkaterFail.cs'.\n" +
            "    Line: line number, to disambiguate several sites in one file.\n" +
            "    Match: case-insensitive substring of the message, as an alternative to File/Line.\n" +
            "    Occurrence: Last (default), First, or All when several entries match.\n" +
            "    Max: cap on entries rendered when Occurrence is All (default 5).";

        [McpTool("Unity.LogDetail", DetailDescription, DetailTitle, Groups = new[] { "debug", "editor" })]
        public static object LogDetail(JigsLogDetailParams parameters)
        {
            if (_reflectionError != null)
                return Response.Error($"Unity.LogDetail cannot read the console: {_reflectionError}");

            var p = parameters ?? new JigsLogDetailParams();

            var all = new List<Entry>();
            try
            {
                ReadAll(all);
            }
            catch (Exception e)
            {
                return Response.Error($"Failed reading console entries: {e.Message}");
            }

            if (all.Count == 0) return Response.Success("(console empty)");

            var matches = new List<Entry>();
            var line = p.Line.GetValueOrDefault();
            var addressed = !string.IsNullOrEmpty(p.File) || line > 0 || !string.IsNullOrEmpty(p.Match);

            for (var i = 0; i < all.Count; i++)
            {
                var e = all[i];
                if (!string.IsNullOrEmpty(p.File) &&
                    e.File.IndexOf(p.File, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (line > 0 && e.Line != line) continue;
                if (!string.IsNullOrEmpty(p.Match) &&
                    e.Message.IndexOf(p.Match, StringComparison.OrdinalIgnoreCase) < 0) continue;
                matches.Add(e);
            }

            // No address given: the interesting entry is almost always the newest error.
            if (!addressed)
            {
                matches.Clear();
                for (var i = all.Count - 1; i >= 0; i--)
                {
                    if (all[i].Severity != Sev.Error) continue;
                    matches.Add(all[i]);
                    break;
                }

                if (matches.Count == 0) matches.Add(all[all.Count - 1]);
            }

            if (matches.Count == 0)
                return Response.Success($"No console entry matched (console holds {all.Count} entries). " +
                                        "Run Unity.Logs and address the entry with the file:line it printed.");

            var chosen = new List<Entry>();
            switch (p.Occurrence)
            {
                case Occurrences.First:
                    chosen.Add(matches[0]);
                    break;
                case Occurrences.All:
                    var cap = p.Max > 0 ? p.Max : 5;
                    for (var i = 0; i < matches.Count && i < cap; i++) chosen.Add(matches[i]);
                    break;
                default:
                    chosen.Add(matches[matches.Count - 1]);
                    break;
            }

            var sb = new StringBuilder();
            if (matches.Count > chosen.Count)
                sb.Append(matches.Count).Append(" entries matched; showing ").Append(chosen.Count)
                    .AppendLine(p.Occurrence == Occurrences.All
                        ? " (raise Max for more)"
                        : " (Occurrence=All for the rest)");
            else if (!addressed)
                sb.AppendLine("(no address given — showing the most recent error)");

            // A single entry gets its whole trace — that's the point of the tool. Several entries would
            // otherwise multiply a 25-frame trace by N and dwarf what was actually asked for.
            var maxFrames = chosen.Count > 1 ? 8 : 0;

            for (var i = 0; i < chosen.Count; i++)
            {
                if (i > 0) sb.AppendLine();
                RenderDetail(sb, chosen[i], all.Count, maxFrames);
            }

            return Response.Success(Normalise(sb.ToString().TrimEnd()));
        }

        static void RenderDetail(StringBuilder sb, in Entry e, int total, int maxFrames)
        {
            sb.Append("entry ").Append(e.Index).Append('/').Append(total - 1).Append(" · ")
                .Append(e.Severity.ToString().ToUpperInvariant());
            if (!string.IsNullOrEmpty(e.Time)) sb.Append(" · ").Append(e.Time);
            sb.AppendLine();

            sb.Append("site: ").Append(string.IsNullOrEmpty(e.File) ? "(no source)" : e.File);
            if (e.Line > 0) sb.Append(':').Append(e.Line);
            if (e.Column > 0) sb.Append(" col ").Append(e.Column);
            sb.AppendLine();

            sb.Append("mode: ").Append(e.Mode).Append(" = ").AppendLine(DescribeMode(e.Mode));

            if (e.InstanceId != 0)
            {
                // InstanceIDToObject(int) is deprecated in 6000.3 in favour of EntityIdToObject(EntityId).
                var obj = EditorUtility.EntityIdToObject((EntityId)e.InstanceId);
                sb.Append("context: ");
                if (obj != null) sb.Append(obj.name).Append(" (").Append(obj.GetType().Name).Append(')');
                else sb.Append("instanceID ").Append(e.InstanceId).Append(" (no longer resolvable)");
                sb.AppendLine();
            }

            sb.AppendLine("message:");
            var lines = e.Message.Split('\n');
            for (var i = 0; i < lines.Length; i++) sb.Append("  ").AppendLine(lines[i].TrimEnd('\r'));

            if (e.Stack == null)
            {
                sb.AppendLine("stack: (none recorded)");
                return;
            }

            sb.AppendLine("stack:");
            var frames = e.Stack.Split('\n');
            var shown = 0;
            for (var i = 0; i < frames.Length; i++)
            {
                var frame = frames[i].TrimEnd('\r');
                if (frame.Length == 0) continue;
                if (maxFrames > 0 && shown >= maxFrames)
                {
                    sb.Append("  (+").Append(frames.Length - i)
                        .AppendLine(" more frames — address this entry alone for the full trace)");
                    return;
                }

                sb.Append("  ").AppendLine(frame);
                shown++;
            }
        }

        /// Names the set bits so an odd severity can be diagnosed from the output instead of by re-probing.
        static string DescribeMode(int mode)
        {
            if (_modeType == null) return "0x" + mode.ToString("X");

            var sb = new StringBuilder();
            var covered = 0;
            var names = Enum.GetNames(_modeType);
            for (var i = 0; i < names.Length; i++)
            {
                if (!Enum.TryParse(_modeType, names[i], out var boxed)) continue;
                var bit = Convert.ToInt32(boxed);
                if (bit == 0 || (mode & bit) != bit) continue;
                if (sb.Length > 0) sb.Append(" | ");
                sb.Append(names[i]);
                covered |= bit;
            }

            var leftover = mode & ~covered;
            if (leftover != 0)
            {
                if (sb.Length > 0) sb.Append(" | ");
                sb.Append("0x").Append(leftover.ToString("X")).Append(" (unnamed)");
            }

            return sb.Length > 0 ? sb.ToString() : "0";
        }

        [McpTool("Unity.LogsClear",
            "Clears the Unity Editor console. Pair with Unity.Logs to scope a read to one reproduction: " +
            "clear, trigger the behaviour, then read.",
            "Clear Unity console", Groups = new[] { "debug", "editor" })]
        public static object ClearLogs()
        {
            if (_reflectionError != null)
                return Response.Error($"Unity.LogsClear cannot reach the console: {_reflectionError}");

            _clear.Invoke(null, null);
            SessionState.SetInt(KeyCount, 0);
            SessionState.SetString(KeyFingerprint, "");
            return Response.Success("Console cleared.");
        }

        // ---- Reading -------------------------------------------------------------------------------

        static void ReadAll(List<Entry> into)
        {
            _start.Invoke(null, null);
            try
            {
                var count = (int)_getCount.Invoke(null, null);
                var box = Activator.CreateInstance(_entryType);
                var args = new object[2];
                var tsArgs = new object[2];

                for (var i = 0; i < count; i++)
                {
                    args[0] = i;
                    args[1] = box;
                    _getEntry.Invoke(null, args);

                    var raw = (string)_fMessage.GetValue(box) ?? "";
                    if (raw.Length == 0) continue;

                    var split = _fCallstackStart != null ? (int)_fCallstackStart.GetValue(box) : 0;
                    string message, stack;
                    if (split > 0 && split <= raw.Length)
                    {
                        message = raw.Substring(0, split).TrimEnd('\r', '\n');
                        stack = split < raw.Length ? raw.Substring(split).TrimEnd('\r', '\n') : null;
                    }
                    else
                    {
                        message = raw.TrimEnd('\r', '\n');
                        stack = null;
                    }

                    var mode = (int)_fMode.GetValue(box);
                    into.Add(new Entry
                    {
                        Message = message,
                        Stack = string.IsNullOrEmpty(stack) ? null : stack,
                        File = (string)_fFile.GetValue(box) ?? "",
                        Line = (int)_fLine.GetValue(box),
                        Column = _fColumn != null ? (int)_fColumn.GetValue(box) : 0,
                        Mode = mode,
                        InstanceId = _fInstanceId != null ? (int)_fInstanceId.GetValue(box) : 0,
                        Index = i,
                        Severity = SeverityOf(mode),
                        Time = ReadTimestamp(i, tsArgs)
                    });
                }
            }
            finally
            {
                _end.Invoke(null, null);
            }
        }

        /// GetEntryTimestampInternal hands back the whole message prefixed with "[HH:MM:SS] "; we only want
        /// the bracket.
        static string ReadTimestamp(int index, object[] args)
        {
            if (_getTimestamp == null) return "";
            try
            {
                args[0] = index;
                args[1] = "";
                _getTimestamp.Invoke(null, args);
                var s = args[1] as string;
                if (string.IsNullOrEmpty(s) || s[0] != '[') return "";
                var close = s.IndexOf(']');
                return close > 1 ? s.Substring(1, close - 1) : "";
            }
            catch
            {
                return "";
            }
        }

        static bool WantsSeverity(LogSeverities[] types, Sev sev)
        {
            if (types == null || types.Length == 0) return true;
            for (var i = 0; i < types.Length; i++)
            {
                if (types[i] == LogSeverities.All) return true;
                if (types[i] == LogSeverities.Info && sev == Sev.Info) return true;
                if (types[i] == LogSeverities.Warning && sev == Sev.Warning) return true;
                if (types[i] == LogSeverities.Error && sev == Sev.Error) return true;
            }

            return false;
        }

        // ---- Grouping ------------------------------------------------------------------------------

        class Group
        {
            public string File;
            public int Line;
            public Sev Severity;
            public int Count;
            public string FirstTime;
            public string LastTime;

            // Distinct messages in first-occurrence order, with how many times each fired. The run rule
            // guarantees a message occupies one contiguous block, so these two lists are a lossless
            // encoding of the run — "A, B ×3" can only have been A B B B.
            public readonly List<string> Distinct = new();
            public readonly List<int> Counts = new();

            public string Stack; // first stack seen in the run
        }

        static List<Group> GroupConsecutive(List<Entry> entries)
        {
            var groups = new List<Group>();
            Group current = null;
            string previous = null;

            for (var i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                var sameSite = current != null
                               && current.Line == e.Line
                               && current.Severity == e.Severity
                               && current.File == e.File;

                // A single call site can alternate between messages (A B A B). Collapsing that to "×4, two
                // distinct messages" would erase the cycle, which is usually the whole signal. So a run only
                // extends on an adjacent repeat or a message not yet seen in the run; a message coming back
                // after something else intervened means we're interleaving, and the run ends there.
                var continuesRun = sameSite && (e.Message == previous || !current.Distinct.Contains(e.Message));

                if (!continuesRun)
                {
                    current = new Group
                    {
                        File = e.File,
                        Line = e.Line,
                        Severity = e.Severity,
                        FirstTime = e.Time,
                        Stack = e.Stack
                    };
                    groups.Add(current);
                }

                current.Count++;
                current.LastTime = e.Time;
                current.Stack ??= e.Stack;

                if (e.Message == previous && current.Counts.Count > 0) current.Counts[current.Counts.Count - 1]++;
                else
                {
                    current.Distinct.Add(e.Message);
                    current.Counts.Add(1);
                }

                previous = e.Message;
            }

            return groups;
        }

        /// Drops the oldest Info groups when over budget. Errors and warnings are never dropped — they are
        /// the reason to read the console at all.
        static int Trim(List<Group> groups, int max)
        {
            if (max <= 0 || groups.Count <= max) return 0;

            var over = groups.Count - max;
            var dropped = 0;
            for (var i = 0; i < groups.Count && dropped < over; i++)
            {
                if (groups[i].Severity != Sev.Info) continue;
                groups.RemoveAt(i);
                i--;
                dropped++;
            }

            return dropped;
        }

        // ---- Rendering -----------------------------------------------------------------------------

        static string Render(List<Group> groups, List<Entry> all, int keptCount, int newCount,
            bool wasReset, int dropped, JigsLogsParams p)
        {
            int err = 0, warn = 0, info = 0;
            for (var i = 0; i < all.Count; i++)
            {
                switch (all[i].Severity)
                {
                    case Sev.Error:
                        err++;
                        break;
                    case Sev.Warning:
                        warn++;
                        break;
                    default:
                        info++;
                        break;
                }
            }

            var sb = new StringBuilder();
            sb.Append(all.Count).Append(" entries · ")
                .Append(err).Append(" err · ")
                .Append(warn).Append(" warn · ")
                .Append(info).Append(" info");

            if (newCount > 0 && newCount < all.Count) sb.Append(" · ").Append(newCount).Append(" new since last read");
            else if (newCount == 0 && all.Count > 0) sb.Append(" · nothing new since last read");

            var span = TimeSpanOf(all);
            if (span != null) sb.Append(" · ").Append(span);
            if (wasReset) sb.Append(" · console was cleared since last read");
            if (dropped > 0) sb.Append(" · ").Append(dropped).Append(" oldest info rows dropped (raise Max)");
            sb.AppendLine();

            if (groups.Count == 0)
            {
                if (all.Count == 0) sb.Append("(console empty)");
                else if (p.Since == SinceModes.LastRead && newCount == 0) sb.Append("(nothing new)");
                else sb.Append("(no entries matched Types/Filter)");
                return sb.ToString();
            }

            for (var i = 0; i < groups.Count; i++) RenderGroup(sb, groups[i], p);
            return sb.ToString().TrimEnd();
        }

        static string TimeSpanOf(List<Entry> all)
        {
            string first = null, last = null;
            for (var i = 0; i < all.Count; i++)
            {
                if (string.IsNullOrEmpty(all[i].Time)) continue;
                first ??= all[i].Time;
                last = all[i].Time;
            }

            if (first == null) return null;
            return first == last ? first : first + "–" + last;
        }

        static void RenderGroup(StringBuilder sb, Group g, JigsLogsParams p)
        {
            sb.Append(g.Severity switch
            {
                Sev.Error => "ERR  ",
                Sev.Warning => "WARN ",
                _ => "INFO "
            });

            sb.Append(FileName(g.File));
            if (g.Line > 0) sb.Append(':').Append(g.Line);
            if (g.Count > 1) sb.Append(" ×").Append(g.Count);

            if (!string.IsNullOrEmpty(g.FirstTime))
            {
                sb.Append("  ").Append(g.FirstTime);
                if (g.Count > 1 && g.LastTime != g.FirstTime) sb.Append('–').Append(g.LastTime);
            }

            sb.AppendLine();

            if (g.Distinct.Count == 1)
            {
                // The header's ×N already states the count.
                AppendIndented(sb, g.Distinct[0], 1);
            }
            else if (TryTemplate(g.Distinct, g.Counts, out var template, out var varying))
            {
                AppendIndented(sb, template, 1);
                sb.Append("     varying: ").AppendLine(varying);
            }
            else
            {
                var show = Math.Min(g.Distinct.Count, 8);
                for (var i = 0; i < show; i++) AppendIndented(sb, g.Distinct[i], g.Counts[i]);
                if (g.Distinct.Count > show)
                    sb.Append("     + ").Append(g.Distinct.Count - show).AppendLine(" more distinct messages");
            }

            var wantStack = p.Stacks == StackModes.All ||
                            (p.Stacks == StackModes.Errors && g.Severity == Sev.Error);
            if (wantStack && g.Stack != null)
            {
                var lines = g.Stack.Split('\n');
                for (var i = 0; i < lines.Length; i++)
                {
                    var line = lines[i].TrimEnd('\r');
                    if (line.Length > 0) sb.Append("       ").AppendLine(line);
                }
            }
        }

        static void AppendIndented(StringBuilder sb, string message, int count)
        {
            var lines = message.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                sb.Append("     ").Append(lines[i].TrimEnd('\r'));
                if (i == lines.Length - 1 && count > 1) sb.Append(" ×").Append(count);
                sb.AppendLine();
            }
        }

        static string FileName(string path)
        {
            if (string.IsNullOrEmpty(path)) return "(no source)";
            var slash = path.LastIndexOfAny(new[] { '/', '\\' });
            return slash >= 0 ? path.Substring(slash + 1) : path;
        }

        /// Messages from one call site usually differ in a single interpolated span. Pulling out the exact
        /// common prefix/suffix is a pure string operation — no parsing, nothing to misread — and turns N
        /// near-identical lines into one template plus the values that varied.
        static bool TryTemplate(List<string> distinct, List<int> counts, out string template, out string varying)
        {
            template = null;
            varying = null;
            if (distinct.Count < 2) return false;

            var min = int.MaxValue;
            for (var i = 0; i < distinct.Count; i++) min = Math.Min(min, distinct[i].Length);
            if (min == 0) return false;

            var prefix = 0;
            while (prefix < min && SameCharAt(distinct, prefix, false)) prefix++;

            var suffix = 0;
            while (suffix < min - prefix && SameCharAt(distinct, suffix, true)) suffix++;

            // Snap both affixes out to word boundaries. Without this, "State: Skating" vs "State: Stomping"
            // shares the "S" and the "ing" and you get "State: S…ing / varying: kat | tomp" — technically
            // reconstructable, useless to read.
            var sample = distinct[0];
            while (prefix > 0 && IsWordChar(sample[prefix - 1])) prefix--;
            while (suffix > 0 && IsWordChar(sample[sample.Length - suffix]) &&
                   IsWordChar(sample[sample.Length - suffix - 1])) suffix--;

            // Only worth it when the shared part genuinely dominates; otherwise listing the messages
            // verbatim is clearer and barely longer.
            var shared = prefix + suffix;
            if (shared < 24 || shared < min / 2) return false;

            var head = distinct[0].Substring(0, prefix);
            var tail = distinct[0].Substring(distinct[0].Length - suffix, suffix);
            template = head + "…" + tail;

            var sb = new StringBuilder();
            for (var i = 0; i < distinct.Count; i++)
            {
                if (i > 0) sb.Append(" | ");
                var s = distinct[i];
                sb.Append(s.Substring(prefix, s.Length - suffix - prefix));
                if (counts[i] > 1) sb.Append(" ×").Append(counts[i]);
            }

            varying = sb.ToString();
            return true;
        }

        static bool IsWordChar(char c) => char.IsLetterOrDigit(c);

        static bool SameCharAt(List<string> items, int offset, bool fromEnd)
        {
            var first = items[0];
            var c = fromEnd ? first[first.Length - 1 - offset] : first[offset];
            for (var i = 1; i < items.Count; i++)
            {
                var s = items[i];
                var d = fromEnd ? s[s.Length - 1 - offset] : s[offset];
                if (c != d) return false;
            }

            return true;
        }
    }
}
