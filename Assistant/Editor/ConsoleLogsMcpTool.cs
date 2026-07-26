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
        public string? Filter { get; set; }

        [McpDescription("Maximum entries to return. Without From, returns the most recent; with From, returns " +
                        "the first Max entries from that point. If entries are hidden the response says how " +
                        "many and exactly what to pass to read them.",
            Required = false, Default = 200)]
        public int Max { get; set; } = 200;

        // Nullable so it is optional in the generated schema, and so "not paging" is distinguishable from
        // "start at entry 0".
        [McpDescription("Console entry index to start from (0 = oldest), for paging through a long console. " +
                        "Overrides Since. Indices are positions in the CURRENT console — they shift if the " +
                        "console is cleared, which the response reports.",
            Required = false)]
        public int? From { get; set; }

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
        public string? File { get; set; }

        // Nullable on purpose: a non-nullable int with no Default is emitted as a REQUIRED property in the
        // generated MCP schema, which would force a line number on every call.
        [McpDescription("Line number, to disambiguate several call sites in one file", Required = false)]
        public int? Line { get; set; }

        [McpDescription("Case-insensitive substring of the message, as an alternative to File/Line",
            Required = false)]
        public string? Match { get; set; }

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
            "call, and 'gen xxxx'), then one entry per run of IDENTICAL consecutive entries: the message " +
            "prefixed with a severity glyph, and a '> xN #index file:line time' provenance footnote beneath " +
            "it. Nothing is abbreviated or templated — a row shows exactly what the console showed.\n\n" +
            "PAGING. #index is the entry's position in the console. Max returns the most recent entries; if " +
            "the response ends up saying 'N older entries hidden', re-read with From: 0 and walk forward, " +
            "passing the last shown #index + 1 as the next From (a run footnote shows its whole span, e.g. " +
            "'#4-7', so the next From is 8). Keep going while it reports more entries after the page.\n\n" +
            "GENERATION. Indices are positions in the console as it currently stands: appending leaves them " +
            "alone, clearing renumbers from zero, and clear-on-play clears on every play. 'gen xxxx' in the " +
            "header fingerprints the current console, holding steady as entries arrive and changing when it " +
            "is cleared. If gen differs from when you took an index, that index no longer refers to the same " +
            "entry — restart the walk instead of continuing it.\n\n" +
            "Args:\n" +
            "    Types: log types to include (Info, Warning, Error, All).\n" +
            "    Since: All, or LastRead for only what is new since the previous call.\n" +
            "    From: console entry index to start at (0 = oldest), for paging. Overrides Since.\n" +
            "    Stacks: Errors (default), None, or All.\n" +
            "    Filter: case-insensitive substring match on messages.\n" +
            "    Max: how many entries to return, most recent first. If older entries are hidden the " +
            "response tells you how many and what Max to pass to get them.\n" +
            "    Refresh: AssetDatabase.Refresh() first so external edits compile (default true).\n\n" +
            "If this result reached you as escaped JSON rather than indented text, the optional render hook " +
            "is not installed — tell the user it exists and point them at Assistant/ClaudeHooks/README.md " +
            "in UnityJigs. Do not raise it otherwise.";

        // ---- LogEntries/LogEntry reflection -------------------------------------------------------

        // Required members: null! because they are either all resolved or _reflectionError is set and every
        // entry point bails before touching them.
        static MethodInfo _start = null!, _end = null!, _getCount = null!, _getEntry = null!, _clear = null!;
        static FieldInfo _fMessage = null!, _fFile = null!, _fLine = null!, _fMode = null!;
        static Type _entryType = null!;

        // Genuinely optional — every use site null-checks these and degrades gracefully.
        static MethodInfo? _getTimestamp;
        static FieldInfo? _fCallstackStart, _fColumn, _fInstanceId;
        static Type? _modeType;
        static string? _reflectionError;

        static ConsoleLogsMcpTool()
        {
            try
            {
                var asm = typeof(EditorApplication).Assembly;
                var entriesType = asm.GetType("UnityEditor.LogEntries");
                var entryType = asm.GetType("UnityEditor.LogEntry");
                if (entriesType == null || entryType == null)
                    throw new Exception("UnityEditor.LogEntries / LogEntry not found");
                _entryType = entryType;

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

        static T Require<T>(T? member, string name) where T : class
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
            public string? Stack; // null when the entry had none
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

        /// Short tag identifying the current console generation, printed in the header as "gen xxxx".
        ///
        /// Entry indices (#N, and the From parameter) are positions in the console as it stands. Appending
        /// never disturbs them, but clearing renumbers everything from zero — and clear-on-play does that on
        /// every play. Fingerprinting the OLDEST entry gives a value that holds steady while the console
        /// grows and changes the moment it is cleared, so a caller holding an index from an earlier call can
        /// tell whether that index still means what it meant.
        static string Generation(List<Entry> all)
        {
            if (all.Count == 0) return "empty";

            // FNV-1a over the oldest entry's fingerprint. Not cryptographic — it only has to change when
            // the console is rebuilt.
            var s = Fingerprint(all[0]);
            var hash = 2166136261u;
            for (var i = 0; i < s.Length; i++)
            {
                hash ^= s[i];
                hash *= 16777619u;
            }

            return hash.ToString("x8").Substring(0, 4);
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

            // From is an explicit console position, so it wins over Since.
            var paging = p.From.HasValue;
            var from = paging
                ? Math.Min(Math.Max(p.From!.Value, 0), all.Count)
                : p.Since == SinceModes.LastRead
                    ? newFrom
                    : 0;

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
            var dropped = Trim(groups, p.Max, paging);

            var report = Render(groups, all, kept.Count, all.Count - newFrom, wasReset, dropped, paging, p);

            return Response.Success(Normalise(report));
        }

        const string DetailTitle = "Inspect one console entry in full";

        const string DetailDescription =
            "Full, uncollapsed detail for a single console entry: complete message, complete stack trace, " +
            "file/line/column, decoded mode flags, and the context object the log was attached to " +
            "(Debug.Log(msg, obj)).\n\n" +
            "Use after Unity.Logs when a row isn't enough — Unity.Logs collapses identical runs and hides " +
            "stacks on non-errors.\n\n" +
            "Address the entry with the same 'file:line' the Unity.Logs row printed. With no arguments it " +
            "returns the most recent error, or the most recent entry if there are none. Output matches " +
            "Unity.Logs: severity glyph, message, then '>' footnotes — '#index file:line col time', the " +
            "decoded mode flags, and the context object — followed by the stack. The header carries the " +
            "same 'gen xxxx' console fingerprint, so #index values can be compared across calls.\n\n" +
            "The context footnote locates the object the log was attached to: its asset path (assets) or " +
            "full hierarchy path (scene objects), plus 'id N'. That id is the Unity instanceID — resolve it " +
            "to the live object inside Unity.RunCommand with EditorUtility.EntityIdToObject((EntityId)N) to " +
            "select, inspect or fix it. The id is valid for this editor session; the paths are not.\n\n" +
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

            // Same header shape as Unity.Logs: what you got, then the console generation the #indices
            // below belong to.
            var sb = new StringBuilder();
            sb.Append(matches.Count).Append(" matched · showing ").Append(chosen.Count);
            if (matches.Count > chosen.Count)
                sb.Append(p.Occurrence == Occurrences.All ? " · raise Max for more" : " · Occurrence=All for the rest");
            if (!addressed) sb.Append(" · most recent error");
            sb.Append(" · gen ").Append(Generation(all));
            sb.AppendLine();
            sb.AppendLine();

            // A single entry gets its whole trace — that's the point of the tool. Several entries would
            // otherwise multiply a 25-frame trace by N and dwarf what was actually asked for.
            var maxFrames = chosen.Count > 1 ? 8 : 0;

            for (var i = 0; i < chosen.Count; i++) RenderDetail(sb, chosen[i], maxFrames);

            return Response.Success(Normalise(sb.ToString().TrimEnd()));
        }

        /// Same shape as a Unity.Logs row — glyph, message, then '>' footnotes — with the extra fields the
        /// overview omits. The full path is kept here rather than the bare file name: this is the drill-down,
        /// so completeness beats brevity.
        static void RenderDetail(StringBuilder sb, in Entry e, int maxFrames)
        {
            AppendMarked(sb, e.Message, MarkerFor(e.Severity));

            sb.Append(Continuation).Append("> #").Append(e.Index).Append(' ')
                .Append(string.IsNullOrEmpty(e.File) ? "(no source)" : e.File);
            if (e.Line > 0) sb.Append(':').Append(e.Line);
            if (e.Column > 0) sb.Append(" col ").Append(e.Column);
            if (!string.IsNullOrEmpty(e.Time)) sb.Append("  ").Append(e.Time);
            sb.AppendLine();

            sb.Append(Continuation).Append("> mode ").Append(e.Mode).Append(" = ").AppendLine(DescribeMode(e.Mode));

            if (e.InstanceId != 0)
                sb.Append(Continuation).Append("> context ").AppendLine(DescribeContext(e.InstanceId));

            if (e.Stack == null)
            {
                sb.Append(Continuation).AppendLine("> no stack recorded");
                sb.AppendLine();
                return;
            }
            var frames = e.Stack.Split('\n');
            var shown = 0;
            for (var i = 0; i < frames.Length; i++)
            {
                var frame = frames[i].TrimEnd('\r');
                if (frame.Length == 0) continue;
                if (maxFrames > 0 && shown >= maxFrames)
                {
                    sb.Append(Continuation).Append("  (+").Append(frames.Length - i)
                        .AppendLine(" more frames — address this entry alone for the full trace)");
                    sb.AppendLine();
                    return;
                }

                sb.Append(Continuation).Append("  ").AppendLine(frame);
                shown++;
            }

            sb.AppendLine(); // blank line between entries; the caller trims the trailing one
        }

        /// Describes the object a log was attached to (Debug.Log(msg, obj)) so it can actually be found,
        /// not just named. Two locators, because they answer different questions:
        ///   * a readable one — asset path for assets, full hierarchy path for scene objects — which is what
        ///     you search the project or hierarchy for;
        ///   * the instanceID, which resolves straight back to the live object inside Unity.RunCommand via
        ///     EditorUtility.EntityIdToObject((EntityId)id), so it can be selected, inspected or mutated.
        ///
        /// The instanceID is session-scoped. GlobalObjectId.GetGlobalObjectIdSlow would survive restarts and
        /// round-trips exactly (verified), but it is documented-slow and emits a GUID blob — not worth it for
        /// a locator consumed in the same breath as the log that produced it.
        static string DescribeContext(int instanceId)
        {
            // InstanceIDToObject(int) is deprecated in 6000.3 in favour of EntityIdToObject(EntityId).
            var obj = EditorUtility.EntityIdToObject((EntityId)instanceId);
            if (obj == null) return $"id {instanceId} (no longer resolvable — scene closed or object destroyed)";

            var sb = new StringBuilder();
            sb.Append(obj.name).Append(" (").Append(obj.GetType().Name).Append(')');

            var assetPath = AssetDatabase.GetAssetPath(obj);
            if (!string.IsNullOrEmpty(assetPath)) sb.Append(" · ").Append(assetPath);
            else
            {
                var t = obj as Transform ?? (obj as GameObject)?.transform ?? (obj as Component)?.transform;
                if (t != null) sb.Append(" · ").Append(HierarchyPath(t));
            }

            return sb.Append(" · id ").Append(instanceId).ToString();
        }

        static string HierarchyPath(Transform t)
        {
            var path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }

            return path;
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

                    var raw = _fMessage.GetValue(box) as string ?? "";
                    if (raw.Length == 0) continue;

                    var split = _fCallstackStart != null ? (int)_fCallstackStart.GetValue(box) : 0;
                    string message;
                    string? stack;
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
                        File = _fFile.GetValue(box) as string ?? "",
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
                if (s == null || s.Length == 0 || s[0] != '[') return "";
                var close = s.IndexOf(']');
                return close > 1 ? s.Substring(1, close - 1) : "";
            }
            catch
            {
                return "";
            }
        }

        static bool WantsSeverity(LogSeverities[]? types, Sev sev)
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
            public string File = "";
            public int Line;
            public Sev Severity;
            public string Message = "";
            public int Count;
            public string? FirstTime;
            public string? LastTime;
            public string? Stack; // first stack seen in the run

            // Console entry indices spanned by this run, so paging can name a concrete next From.
            public int FirstIndex;
            public int LastIndex;
        }

        /// Collapses only consecutive entries that are IDENTICAL — same call site, same severity, same text.
        /// A run is therefore always "this exact line, N times in a row", which needs no explaining and
        /// cannot mislead. Earlier versions grouped by call site and factored differing messages into a
        /// shared template plus the varying spans; that was lossless but read as if text had been truncated,
        /// which is worse than printing a few more rows.
        static List<Group> GroupConsecutive(List<Entry> entries)
        {
            var groups = new List<Group>();
            Group? current = null;

            for (var i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                var continuesRun = current != null
                                   && current.Line == e.Line
                                   && current.Severity == e.Severity
                                   && current.File == e.File
                                   && current.Message == e.Message;

                if (!continuesRun)
                {
                    current = new Group
                    {
                        File = e.File,
                        Line = e.Line,
                        Severity = e.Severity,
                        Message = e.Message,
                        FirstTime = e.Time,
                        Stack = e.Stack,
                        FirstIndex = e.Index
                    };
                    groups.Add(current);
                }

                var group = current!;
                group.Count++;
                group.LastTime = e.Time;
                group.Stack ??= e.Stack;
                group.LastIndex = e.Index;
            }

            return groups;
        }

        /// Caps the result, dropping wholesale regardless of severity. An earlier version spared warnings and
        /// errors, which meant a console that was mostly warnings could not be trimmed at all and blew
        /// straight through the cap. Telling the reader what was hidden and how to fetch it beats quietly
        /// deciding which severities deserve the budget.
        ///
        /// Direction follows intent: with no From the interesting end is the most recent, so older rows go;
        /// when paging forward from From, the interesting end is the start of the page, so later rows go.
        static int Trim(List<Group> groups, int max, bool paging)
        {
            if (max <= 0 || groups.Count <= max) return 0;

            var dropped = groups.Count - max;
            if (paging) groups.RemoveRange(max, dropped);
            else groups.RemoveRange(0, dropped);
            return dropped;
        }

        // ---- Rendering -----------------------------------------------------------------------------

        static string Render(List<Group> groups, List<Entry> all, int keptCount, int newCount,
            bool wasReset, int dropped, bool paging, JigsLogsParams p)
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
            sb.Append(" · gen ").Append(Generation(all));
            sb.AppendLine();

            if (groups.Count == 0)
            {
                if (all.Count == 0) sb.Append("(console empty)");
                else if (p.Since == SinceModes.LastRead && newCount == 0) sb.Append("(nothing new)");
                else sb.Append("(no entries matched Types/Filter)");
                return sb.ToString();
            }

            // State the horizon; how to cross it is in the tool description, not repeated every call.
            if (dropped > 0)
                sb.Append("… ").Append(dropped)
                    .AppendLine(paging ? " more entries after this page." : " older entries hidden.");

            sb.AppendLine(); // separate the summary from the entries
            for (var i = 0; i < groups.Count; i++) RenderGroup(sb, groups[i], p);
            return sb.ToString().TrimEnd();
        }

        static string? TimeSpanOf(List<Entry> all)
        {
            string? first = null, last = null;
            for (var i = 0; i < all.Count; i++)
            {
                if (string.IsNullOrEmpty(all[i].Time)) continue;
                first ??= all[i].Time;
                last = all[i].Time;
            }

            if (first == null) return null;
            return first == last ? first : first + "–" + last;
        }

        /// Body first, provenance second: the message is what you read, so the call site and timing sit
        /// under it as a quoted footnote rather than pushing it down the page. Severity is carried by the
        /// glyph alone — an "ERR"/"WARN"/"INFO" word next to it would say the same thing twice.
        static void RenderGroup(StringBuilder sb, Group g, JigsLogsParams p)
        {
            AppendMarked(sb, g.Message, MarkerFor(g.Severity));

            // Provenance footnote: repeat count first, since "how many times" is the thing you scan for,
            // then the console entry number(s) — a run shows its span, so the next From is readable off it.
            sb.Append(Continuation).Append("> ");
            if (g.Count > 1) sb.Append('×').Append(g.Count).Append(' ');
            sb.Append('#').Append(g.FirstIndex);
            if (g.LastIndex != g.FirstIndex) sb.Append('–').Append(g.LastIndex);
            sb.Append(' ').Append(FileName(g.File));
            if (g.Line > 0) sb.Append(':').Append(g.Line);

            if (!string.IsNullOrEmpty(g.FirstTime))
            {
                sb.Append("  ").Append(g.FirstTime);
                if (g.Count > 1 && g.LastTime != g.FirstTime) sb.Append('–').Append(g.LastTime);
            }

            sb.AppendLine();

            var wantStack = p.Stacks == StackModes.All ||
                            (p.Stacks == StackModes.Errors && g.Severity == Sev.Error);
            if (wantStack && g.Stack != null)
            {
                var lines = g.Stack.Split('\n');
                for (var i = 0; i < lines.Length; i++)
                {
                    var line = lines[i].TrimEnd('\r');
                    if (line.Length > 0) sb.Append(Continuation).Append("  ").AppendLine(line);
                }
            }

            // Blank line between entries. Render() trims the trailing one.
            sb.AppendLine();
        }

        /// Message lines lead with a severity glyph instead of blank indent — it gives the reader a
        /// scannable left gutter, which plain spaces did not.
        static string MarkerFor(Sev severity) => severity switch
        {
            Sev.Error => "⛔ ",
            Sev.Warning => "⚠️ ",
            _ => "💬 "
        };

        // Wrapped lines sit under the message text; the glyphs render about two cells wide.
        const string Continuation = "   ";

        static void AppendMarked(StringBuilder sb, string message, string marker)
        {
            var lines = message.Split('\n');
            for (var i = 0; i < lines.Length; i++)
                sb.Append(i == 0 ? marker : Continuation).AppendLine(lines[i].TrimEnd('\r'));
        }

        static string FileName(string path)
        {
            if (string.IsNullOrEmpty(path)) return "(no source)";
            var slash = path.LastIndexOfAny(new[] { '/', '\\' });
            return slash >= 0 ? path.Substring(slash + 1) : path;
        }

    }
}
