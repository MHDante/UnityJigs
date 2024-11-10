using System;
using System.Text;

namespace UnityJigs.Extensions
{
    public class IndentedStringBuilder
    {
        private readonly StringBuilder _sb;
        public int IndentLevel { get; set; }
        public char IndentCharacter { get; set; }
        public int IndentSize { get; set; }

        public IndentedStringBuilder(char indentCharacter = ' ', int indentSize = 4)
        {
            _sb = new StringBuilder();
            IndentCharacter = indentCharacter;
            IndentSize = indentSize;
        }

        public StringBuilder StartLine => _sb.Append(IndentCharacter, IndentLevel * IndentSize);
        public StringBuilder Continue => _sb;

        public IndentScope BraceScope(string startLine = "{", string endLine = "}", int indentDelta = 1)
        {
            StartLine.AppendLine(startLine);
            IndentLevel += indentDelta;
            return new IndentScope(this, indentDelta, endLine);
        }

        public IndentScope IndentScope(int indentDelta = 1)
        {
            IndentLevel += indentDelta;
            return new IndentScope(this, indentDelta, null);
        }

        public  void Clear()
        {
            _sb.Clear();
            IndentLevel = 0;
        }

        public override string ToString() => _sb.ToString();
    }

    public readonly struct IndentScope : IDisposable
    {
        private readonly IndentedStringBuilder Builder;
        private readonly int Delta;
        private readonly string? ExitString;

        internal IndentScope(IndentedStringBuilder builder, int delta, string? exitString)
        {
            Builder = builder;
            Delta = delta;
            ExitString = exitString;
        }

        public void Dispose()
        {
            Builder.IndentLevel -= Delta;
            if (ExitString != null) Builder.StartLine.AppendLine(ExitString);
        }
    }
}
