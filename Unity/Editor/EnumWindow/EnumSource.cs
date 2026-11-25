using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace UnityJigs.Editor.EnumWindow
{
    // One enum's declaration in its source file, read as text, and the three edits the Enums window makes to it: add a
    // value, remove one, move one. An edit changes only the lines it means to; every other byte of the file (comments,
    // attributes, blank lines, line endings, a byte-order mark) is written back as it was read.
    //
    // It understands one shape and refuses the rest rather than guess: the braces on lines of their own, one value per
    // line, every value with an explicit whole number, no block comments or preprocessor lines inside the braces, and
    // one enum of that name in the file. A refusal says why, so a programmer can make the change by hand.
    internal sealed class EnumSource
    {
        public sealed class Member
        {
            public string Name = "";
            public long Number;
            public int Line;      // index into Lines of the value's own line
            public int FirstLine; // the first line that goes with it: comments and attributes directly above it
            public bool HasComma;
        }

        public readonly byte[] Bytes;
        public readonly string? Refusal;
        public readonly List<Member> Members = new();

        private bool _bom;
        private string _newline = "\n";
        private readonly List<string> _lines = new(); // each with its own line ending, so joining them gives the text back

        private static readonly UTF8Encoding StrictUtf8 = new(false, true);

        private static readonly Regex MemberLine = new(
            @"^\s*(?<attr>\[[^\]]*\]\s*)*(?<name>@?[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<num>-?[0-9]+)\s*(?<comma>,)?\s*$");

        private EnumSource(byte[] bytes, string? refusal)
        {
            Bytes = bytes;
            Refusal = refusal;
        }

        public static EnumSource Read(byte[] bytes, string enumName)
        {
            var source = new EnumSource(bytes, null);
            var refusal = source.Parse(enumName);
            return refusal == null ? source : new EnumSource(bytes, refusal);
        }

        private string? Parse(string enumName)
        {
            var bom = Bytes.Length >= 3 && Bytes[0] == 0xEF && Bytes[1] == 0xBB && Bytes[2] == 0xBF;
            string text;
            try { text = StrictUtf8.GetString(Bytes, bom ? 3 : 0, Bytes.Length - (bom ? 3 : 0)); }
            catch (ArgumentException) { return "The file isn't plain UTF-8 text."; }
            _bom = bom;

            var firstBreak = text.IndexOf('\n');
            _newline = firstBreak > 0 && text[firstBreak - 1] == '\r' ? "\r\n" : "\n";
            var start = 0;
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] != '\n') continue;
                _lines.Add(text.Substring(start, i + 1 - start));
                start = i + 1;
            }
            if (start < text.Length) _lines.Add(text.Substring(start));

            // The same text with comments and strings blanked out (newlines kept), so searching it never matches inside one.
            var masked = Mask(text);
            var decl = new Regex(@"\benum\s+" + Regex.Escape(enumName) + @"\b");
            var found = decl.Matches(masked);
            if (found.Count == 0) return $"The file has no enum called {enumName}.";
            if (found.Count > 1) return $"The file has {found.Count} enums called {enumName}.";

            var open = masked.IndexOf('{', found[0].Index);
            if (open < 0) return "The enum has no opening brace.";
            var close = masked.IndexOf('}', open + 1);
            if (close < 0) return "The enum has no closing brace.";
            if (masked.IndexOf('{', open + 1, close - open - 1) >= 0) return "There is a brace inside the enum.";

            var openLine = LineOf(open);
            var closeLine = LineOf(close);
            if (openLine == closeLine) return "The enum is written on one line.";
            var maskedLines = SplitLikeLines(masked);
            if (maskedLines[openLine].Substring(ColumnOf(open) + 1).Trim().Length > 0)
                return "There is something after the opening brace on its line.";
            if (maskedLines[closeLine].Substring(0, ColumnOf(close)).Trim().Length > 0)
                return "There is something before the closing brace on its line.";

            var pendingFirst = -1;
            for (var l = openLine + 1; l < closeLine; l++)
            {
                var raw = _lines[l];
                var code = maskedLines[l].Trim();
                if (raw.Contains("/*")) return "There is a block comment inside the enum.";
                if (code.StartsWith("#")) return "There is a preprocessor line (#if...) inside the enum.";
                if (code.Length == 0)
                {
                    // A comment-only line belongs to the value below it; a blank line belongs to nobody.
                    if (raw.Trim().Length == 0) pendingFirst = -1;
                    else if (pendingFirst < 0) pendingFirst = l;
                    continue;
                }
                if (code.StartsWith("[") && code.EndsWith("]"))
                {
                    if (pendingFirst < 0) pendingFirst = l;
                    continue;
                }
                var m = MemberLine.Match(maskedLines[l].TrimEnd('\r', '\n'));
                if (!m.Success)
                {
                    if (!code.Contains("=")) return $"A value has no number of its own: \"{code}\".";
                    return code.Contains(",") && code.IndexOf(',') < code.Length - 1
                        ? "More than one value is written on one line."
                        : $"A value's number is written as a formula: \"{code}\".";
                }
                if (!long.TryParse(m.Groups["num"].Value, out var number)) return $"A number is too big: \"{code}\".";
                Members.Add(new Member
                {
                    Name = m.Groups["name"].Value.TrimStart('@'), Number = number, Line = l,
                    FirstLine = pendingFirst >= 0 ? pendingFirst : l, HasComma = m.Groups["comma"].Success,
                });
                pendingFirst = -1;
            }
            for (var i = 0; i < Members.Count - 1; i++)
                if (!Members[i].HasComma) return $"{Members[i].Name} has no comma after it.";
            return null;

            int LineOf(int index)
            {
                var n = 0;
                for (var i = 0; i < index; i++) if (text[i] == '\n') n++;
                return n;
            }

            int ColumnOf(int index) => index - (text.LastIndexOf('\n', Math.Max(0, index - 1)) + 1);
        }

        // ---- the edits: each returns the whole new file, or throws with the reason if the edit can't be made

        public byte[] Add(string name, out long number)
        {
            Check();
            if (Members.Count == 0) throw new InvalidOperationException("The enum is empty; add its first value by hand.");
            var problem = NameProblem(name);
            if (problem != null) throw new InvalidOperationException(problem);
            var max = long.MinValue;
            foreach (var m in Members) max = Math.Max(max, m.Number);
            number = max + 1;

            var lines = new List<string>(_lines);
            var last = Members[^1];
            var lastText = Body(lines[last.Line]);
            var indent = lastText.Substring(0, lastText.Length - lastText.TrimStart().Length);
            var ending = Ending(lines[last.Line]);
            string newLine;
            if (last.HasComma) newLine = $"{indent}{name} = {number},";
            else
            {
                lines[last.Line] = SetComma(lines[last.Line], true);
                newLine = $"{indent}{name} = {number}";
            }
            // The last value's line may have been the file's last line without an ending; the new one gets the file's.
            if (ending.Length == 0) lines[last.Line] += _newline;
            lines.Insert(last.Line + 1, newLine + (ending.Length > 0 ? ending : _newline));
            return Encode(lines);
        }

        public byte[] Remove(string name)
        {
            Check();
            var i = Members.FindIndex(m => m.Name == name);
            if (i < 0) throw new InvalidOperationException($"There is no value called {name}.");
            if (Members.Count == 1) throw new InvalidOperationException("An enum needs at least one value.");
            var lines = new List<string>(_lines);
            var member = Members[i];
            if (i == Members.Count - 1 && !member.HasComma)
                lines[Members[i - 1].Line] = SetComma(lines[Members[i - 1].Line], false);
            lines.RemoveRange(member.FirstLine, member.Line - member.FirstLine + 1);
            return Encode(lines);
        }

        // Moves a value one place up (-1) or down (+1). Its comments and attributes go with it; blank lines stay where
        // they are; numbers never change. Which value has no trailing comma follows the position, not the value.
        public byte[] Move(string name, int direction)
        {
            Check();
            var i = Members.FindIndex(m => m.Name == name);
            if (i < 0) throw new InvalidOperationException($"There is no value called {name}.");
            var j = i + Math.Sign(direction);
            if (direction == 0 || j < 0 || j >= Members.Count) throw new InvalidOperationException("It can't move that way.");
            var (a, b) = i < j ? (Members[i], Members[j]) : (Members[j], Members[i]);
            var lines = new List<string>(_lines);
            var aBlock = lines.GetRange(a.FirstLine, a.Line - a.FirstLine + 1);
            var bBlock = lines.GetRange(b.FirstLine, b.Line - b.FirstLine + 1);
            var gap = lines.GetRange(a.Line + 1, b.FirstLine - a.Line - 1);
            // The comma belongs to the place: a's place (first) keeps a's comma, b's place keeps b's.
            bBlock[^1] = SetComma(bBlock[^1], a.HasComma);
            aBlock[^1] = SetComma(aBlock[^1], b.HasComma);
            // A value moved onto the file's last line without an ending, or off it, keeps the endings where they were.
            var aEnd = Ending(lines[a.Line]);
            var bEnd = Ending(lines[b.Line]);
            bBlock[^1] = Body(bBlock[^1]) + aEnd;
            aBlock[^1] = Body(aBlock[^1]) + bEnd;
            var replaced = new List<string>();
            replaced.AddRange(bBlock);
            replaced.AddRange(gap);
            replaced.AddRange(aBlock);
            lines.RemoveRange(a.FirstLine, b.Line - a.FirstLine + 1);
            lines.InsertRange(a.FirstLine, replaced);
            return Encode(lines);
        }

        // Why a name can't be a new value, or null when it can.
        public string? NameProblem(string name)
        {
            if (name.Length == 0) return "Type a name.";
            if (name.Contains(" ")) return $"No spaces in a name: try {Squash(name)}.";
            if (!Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]*$"))
                return char.IsDigit(name[0])
                    ? "A name can't start with a digit."
                    : $"Use only letters, digits and _: try {Squash(name)}.";
            if (Keywords.Contains(name)) return $"\"{name}\" is a word C# keeps for itself.";
            if (name == "value__") return "That name is kept by C# for every enum.";
            foreach (var m in Members)
            {
                if (m.Name == name) return $"{name} is already in the list.";
                if (string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))
                    return $"Too close to {m.Name}, which is already in the list.";
            }
            return null;
        }

        private static string Squash(string name)
        {
            var sb = new StringBuilder();
            var upper = true;
            foreach (var c in name)
            {
                if (!char.IsLetterOrDigit(c) || c > 127) { upper = true; continue; }
                sb.Append(upper ? char.ToUpperInvariant(c) : c);
                upper = false;
            }
            if (sb.Length == 0 || char.IsDigit(sb[0])) sb.Insert(0, '_');
            return sb.ToString();
        }

        private void Check()
        {
            if (Refusal != null) throw new InvalidOperationException(Refusal);
        }

        private byte[] Encode(List<string> lines)
        {
            var text = string.Concat(lines);
            var body = Encoding.UTF8.GetBytes(text);
            if (!_bom) return body;
            var all = new byte[body.Length + 3];
            all[0] = 0xEF; all[1] = 0xBB; all[2] = 0xBF;
            Buffer.BlockCopy(body, 0, all, 3, body.Length);
            return all;
        }

        private static string Ending(string line) =>
            line.EndsWith("\r\n") ? "\r\n" : line.EndsWith("\n") ? "\n" : line.EndsWith("\r") ? "\r" : "";

        private static string Body(string line) => line.Substring(0, line.Length - Ending(line).Length);

        // Adds or removes the comma right after a value's number, leaving any spaces and comment after it alone.
        private static string SetComma(string line, bool comma)
        {
            var end = Ending(line);
            var body = Body(line);
            var masked = Mask(body);
            var m = MemberLine.Match(masked);
            if (!m.Success) return line;
            var has = m.Groups["comma"].Success;
            if (has == comma) return line;
            if (has) return body.Remove(m.Groups["comma"].Index, 1) + end;
            var at = m.Groups["num"].Index + m.Groups["num"].Length;
            return body.Insert(at, ",") + end;
        }

        private static List<string> SplitLikeLines(string text)
        {
            var list = new List<string>();
            var start = 0;
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] != '\n') continue;
                list.Add(text.Substring(start, i + 1 - start));
                start = i + 1;
            }
            if (start < text.Length) list.Add(text.Substring(start));
            return list;
        }

        // Comments, strings and character literals replaced by spaces, keeping every newline and the text's length.
        private static string Mask(string text)
        {
            var sb = new StringBuilder(text);
            var i = 0;
            while (i < text.Length)
            {
                var c = text[i];
                var next = i + 1 < text.Length ? text[i + 1] : '\0';
                if (c == '/' && next == '/')
                {
                    while (i < text.Length && text[i] != '\n' && text[i] != '\r') sb[i++] = ' ';
                }
                else if (c == '/' && next == '*')
                {
                    // Kept visible as "/*" so a block comment inside an enum is seen and refused; blanked elsewhere.
                    i += 2;
                    while (i < text.Length && !(text[i] == '*' && i + 1 < text.Length && text[i + 1] == '/'))
                    {
                        if (text[i] != '\n' && text[i] != '\r') sb[i] = ' ';
                        i++;
                    }
                    i += 2;
                }
                else if (c == '"' || c == '\'')
                {
                    var verbatim = c == '"' && i > 0 && text[i - 1] == '@';
                    sb[i++] = ' ';
                    while (i < text.Length)
                    {
                        if (!verbatim && text[i] == '\\' && i + 1 < text.Length) { sb[i++] = ' '; sb[i++] = ' '; continue; }
                        if (text[i] == c)
                        {
                            if (verbatim && i + 1 < text.Length && text[i + 1] == '"') { sb[i++] = ' '; sb[i++] = ' '; continue; }
                            sb[i++] = ' ';
                            break;
                        }
                        if (text[i] != '\n' && text[i] != '\r') sb[i] = ' ';
                        i++;
                    }
                }
                else i++;
            }
            return sb.ToString();
        }

        private static readonly HashSet<string> Keywords = new()
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const",
            "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern",
            "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface",
            "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override",
            "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
            "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof",
            "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
        };
    }
}
