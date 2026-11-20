using System;
using System.Collections.Generic;
using System.Text;

namespace DigitalLogicSandbox.GateLibrary.Programs
{
    /// <summary>What a span of program or spec text is, for syntax colouring.</summary>
    public enum SyntaxKind
    {
        /// <summary>An "N:" address prefix.</summary>
        Address = 0,

        /// <summary>A mnemonic known to the instruction set (or defined by a spec line).</summary>
        Mnemonic = 1,

        /// <summary>A word that is neither a known mnemonic nor a number.</summary>
        UnknownWord = 2,

        Number = 3,

        /// <summary>From ';' to the end of the line.</summary>
        Comment = 4,

        /// <summary>The spec's "opcode" keyword.</summary>
        Keyword = 5,

        /// <summary>An operand name in a spec line ("n").</summary>
        Operand = 6,

        /// <summary>The '?' raw-word marker.</summary>
        Marker = 7,
    }

    /// <summary>A coloured span: <see cref="Start"/> and <see cref="Length"/> index the whole source text.</summary>
    public readonly struct SyntaxToken
    {
        public SyntaxToken(int start, int length, SyntaxKind kind)
        {
            Start = start;
            Length = length;
            Kind = kind;
        }

        public int Start { get; }

        public int Length { get; }

        public SyntaxKind Kind { get; }

        public override string ToString()
        {
            return Kind + "@" + Start + "+" + Length;
        }
    }

    /// <summary>
    /// Splits program and instruction-set text into coloured spans, and renders them as TextMeshPro rich text. The
    /// tokenizer reuses the caller's list and does not allocate. Pure data: the UI passes colours as hex strings.
    /// </summary>
    public static class ProgramSyntax
    {
        /// <summary>Number of <see cref="SyntaxKind"/> values (size of the colour table).</summary>
        public const int KindCount = 8;

        /// <summary>Tokenizes a program for <paramref name="set"/> (null = raw words: every word is unknown).</summary>
        public static void TokenizeProgram(string text, InstructionSet set, List<SyntaxToken> into)
        {
            Tokenize(text, set, false, into);
        }

        /// <summary>Tokenizes an instruction-set spec.</summary>
        public static void TokenizeSpec(string text, List<SyntaxToken> into)
        {
            Tokenize(text, null, true, into);
        }

        /// <summary>
        /// Appends <paramref name="text"/> to <paramref name="builder"/> as rich text: each token wrapped in a colour
        /// tag from <paramref name="hexColors"/> (indexed by <see cref="SyntaxKind"/>, "RRGGBB" without '#', null =
        /// no colour). Every '&lt;' of the source is wrapped in &lt;noparse&gt; so the user's text can never form a tag;
        /// the visible characters are exactly the source.
        /// </summary>
        public static void AppendRichText(StringBuilder builder, string text, List<SyntaxToken> tokens,
            string[] hexColors)
        {
            if (builder == null || tokens == null || hexColors == null)
            {
                throw new ArgumentNullException(builder == null ? nameof(builder) :
                    tokens == null ? nameof(tokens) : nameof(hexColors));
            }

            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            int position = 0;
            for (int i = 0; i < tokens.Count; i++)
            {
                SyntaxToken token = tokens[i];
                AppendEscaped(builder, text, position, token.Start - position);
                string color = (int)token.Kind < hexColors.Length ? hexColors[(int)token.Kind] : null;
                if (color != null)
                {
                    builder.Append("<color=#").Append(color).Append('>');
                }

                AppendEscaped(builder, text, token.Start, token.Length);
                if (color != null)
                {
                    builder.Append("</color>");
                }

                position = token.Start + token.Length;
            }

            AppendEscaped(builder, text, position, text.Length - position);
        }

        /// <summary>Number of lines in <paramref name="text"/> (an empty text has one line).</summary>
        public static int LineCount(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 1;
            }

            int lines = 1;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    lines++;
                }
            }

            return lines;
        }

        private static void AppendEscaped(StringBuilder builder, string text, int start, int length)
        {
            int end = start + length;
            for (int i = start; i < end; i++)
            {
                char c = text[i];
                if (c == '<')
                {
                    builder.Append("<noparse><</noparse>");
                }
                else if (c != '\r')
                {
                    builder.Append(c);
                }
            }
        }

        private static void Tokenize(string text, InstructionSet set, bool spec, List<SyntaxToken> into)
        {
            if (into == null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            into.Clear();
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            int lineStart = 0;
            while (lineStart <= text.Length)
            {
                int lineEnd = text.IndexOf('\n', lineStart);
                if (lineEnd < 0)
                {
                    lineEnd = text.Length;
                }

                if (spec)
                {
                    SpecLine(text, lineStart, lineEnd, into);
                }
                else
                {
                    ProgramLine(text, lineStart, lineEnd, set, into);
                }

                lineStart = lineEnd + 1;
            }
        }

        private static void ProgramLine(string text, int start, int end, InstructionSet set, List<SyntaxToken> into)
        {
            int comment = IndexOf(text, ';', start, end);
            int codeEnd = comment < 0 ? end : comment;
            int position = SkipSpace(text, start, codeEnd);

            // "N:" prefix (anything up to the first colon that looks like a number).
            int colon = IndexOf(text, ':', position, codeEnd);
            if (colon > position && IsNumberWord(text, position, TrimEnd(text, position, colon)))
            {
                into.Add(new SyntaxToken(position, colon + 1 - position, SyntaxKind.Address));
                position = colon + 1;
            }

            bool first = true;
            while (true)
            {
                position = SkipSpace(text, position, codeEnd);
                if (position >= codeEnd)
                {
                    break;
                }

                if (text[position] == '?')
                {
                    into.Add(new SyntaxToken(position, 1, SyntaxKind.Marker));
                    position++;
                    continue;
                }

                int wordEnd = WordEnd(text, position, codeEnd);
                SyntaxKind kind;
                if (char.IsDigit(text[position]))
                {
                    kind = SyntaxKind.Number;
                }
                else if (first && set != null && set.ContainsMnemonic(text, position, wordEnd - position))
                {
                    kind = SyntaxKind.Mnemonic;
                }
                else
                {
                    kind = SyntaxKind.UnknownWord;
                }

                into.Add(new SyntaxToken(position, wordEnd - position, kind));
                first = false;
                position = wordEnd;
            }

            if (comment >= 0)
            {
                into.Add(new SyntaxToken(comment, end - comment, SyntaxKind.Comment));
            }
        }

        private static void SpecLine(string text, int start, int end, List<SyntaxToken> into)
        {
            int comment = IndexOf(text, ';', start, end);
            int codeEnd = comment < 0 ? end : comment;
            int position = start;
            int index = 0;
            bool keywordLine = false;
            while (true)
            {
                position = SkipSpace(text, position, codeEnd);
                if (position >= codeEnd)
                {
                    break;
                }

                int wordEnd = WordEnd(text, position, codeEnd);
                int length = wordEnd - position;
                SyntaxKind kind;
                if (char.IsDigit(text[position]))
                {
                    kind = SyntaxKind.Number;
                }
                else if (index == 0 && length == InstructionSet.OpcodeKeyword.Length &&
                         string.Compare(text, position, InstructionSet.OpcodeKeyword, 0, length,
                             StringComparison.OrdinalIgnoreCase) == 0)
                {
                    kind = SyntaxKind.Keyword;
                    keywordLine = true;
                }
                else if (index == 0)
                {
                    kind = SyntaxKind.Mnemonic;
                }
                else if (index == 2 && !keywordLine)
                {
                    kind = SyntaxKind.Operand;
                }
                else
                {
                    kind = SyntaxKind.UnknownWord;
                }

                into.Add(new SyntaxToken(position, length, kind));
                index++;
                position = wordEnd;
            }

            if (comment >= 0)
            {
                into.Add(new SyntaxToken(comment, end - comment, SyntaxKind.Comment));
            }
        }

        private static bool IsNumberWord(string text, int start, int end)
        {
            if (end <= start || !char.IsDigit(text[start]))
            {
                return false;
            }

            for (int i = start; i < end; i++)
            {
                if (char.IsWhiteSpace(text[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static int TrimEnd(string text, int start, int end)
        {
            while (end > start && char.IsWhiteSpace(text[end - 1]))
            {
                end--;
            }

            return end;
        }

        private static int IndexOf(string text, char c, int start, int end)
        {
            for (int i = start; i < end; i++)
            {
                if (text[i] == c)
                {
                    return i;
                }
            }

            return -1;
        }

        private static int SkipSpace(string text, int position, int end)
        {
            while (position < end && char.IsWhiteSpace(text[position]))
            {
                position++;
            }

            return position;
        }

        private static int WordEnd(string text, int position, int end)
        {
            while (position < end && !char.IsWhiteSpace(text[position]))
            {
                position++;
            }

            return position;
        }
    }
}
