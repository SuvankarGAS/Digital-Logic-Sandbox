using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DigitalLogicSandbox.GateLibrary.Programs
{
    /// <summary>
    /// Text assembler for program words of a fixed width, driven by an <see cref="InstructionSet"/> (opcode in the
    /// top bits, operand in the rest). With no instruction set it works on raw numbers. Parses a whole program (one
    /// instruction per line) with line-numbered errors, and produces the listing and the word labels
    /// ("3: LDA 0") shown on program Constants. Lossless: every word lists as text that parses back to it.
    /// Pure data: no Unity or editor dependencies.
    /// </summary>
    public sealed class Assembler
    {
        /// <summary>Widest program word supported.</summary>
        public const int MaxWordWidth = 16;

        private readonly int _operandBits;
        private readonly int _wordMask;
        private readonly int _operandMask;
        private readonly int _hexDigits;

        /// <param name="set">Instruction set, or null for raw words.</param>
        /// <param name="wordWidth">Width of each program word in bits.</param>
        public Assembler(InstructionSet set, int wordWidth)
        {
            if (wordWidth < 1 || wordWidth > MaxWordWidth)
            {
                throw new ArgumentOutOfRangeException(nameof(wordWidth), wordWidth,
                    "Program words are 1 to " + MaxWordWidth + " bits wide.");
            }

            Set = set;
            WordWidth = wordWidth;
            Problem = set?.CheckWordWidth(wordWidth);
            _wordMask = (1 << wordWidth) - 1;
            _operandBits = set == null ? 0 : Math.Max(0, wordWidth - set.OpcodeBits);
            _operandMask = (1 << _operandBits) - 1;
            _hexDigits = (wordWidth + 3) / 4;
        }

        /// <summary>The instruction set, or null for raw words.</summary>
        public InstructionSet Set { get; }

        public int WordWidth { get; }

        /// <summary>Largest operand value (0 when there is no instruction set).</summary>
        public int MaxOperand => _operandMask;

        /// <summary>Largest word value.</summary>
        public int MaxWord => _wordMask;

        /// <summary>
        /// Why the instruction set cannot encode words of this width, or null. While set, <see cref="Parse"/> reports
        /// it as an error and listings show raw words.
        /// </summary>
        public string Problem { get; }

        private bool UsesSet => Set != null && Problem == null;

        /// <summary>Help text for the program editor.</summary>
        public string Reference
        {
            get
            {
                if (Set == null)
                {
                    return "Raw words: one number per line (decimal, 0x hex or 0b binary), 0 to " + _wordMask +
                           ". ';' starts a comment.";
                }

                return Problem ?? Set.Reference(WordWidth);
            }
        }

        /// <summary>Encodes one instruction: opcode in the top bits, operand in the rest.</summary>
        public int Assemble(string mnemonic, int operand = 0)
        {
            if (!UsesSet)
            {
                throw new InvalidOperationException(Problem ?? "Raw words have no instructions.");
            }

            if (!Set.TryGetInstruction(mnemonic, out Instruction instruction))
            {
                throw new ArgumentException("Unknown instruction '" + mnemonic + "'.", nameof(mnemonic));
            }

            if (operand < 0 || operand > _operandMask || (!instruction.TakesOperand && operand != 0))
            {
                throw new ArgumentOutOfRangeException(nameof(operand), operand,
                    instruction.TakesOperand ? "Operands are 0 to " + _operandMask + "." : mnemonic + " takes no operand.");
            }

            return (instruction.Opcode << _operandBits) | operand;
        }

        /// <summary>
        /// Readable form of a word, e.g. "LDI 1", "OUT" or "? 0xC3". Unused opcodes, and operand-less instructions with
        /// stray operand bits, are shown as raw words so the listing parses back to the same value. Raw mode lists
        /// every word as hexadecimal ("0x2A").
        /// </summary>
        public string Disassemble(int word)
        {
            word &= _wordMask;
            if (!UsesSet)
            {
                return Hex(word);
            }

            int operand = word & _operandMask;
            if (!Set.TryGetInstruction(word >> _operandBits, out Instruction instruction) ||
                (!instruction.TakesOperand && operand != 0))
            {
                return "? " + Hex(word);
            }

            return instruction.TakesOperand
                ? instruction.Mnemonic + " " + operand.ToString(CultureInfo.InvariantCulture)
                : instruction.Mnemonic;
        }

        /// <summary>Label shown on a program word: "address: instruction".</summary>
        public string WordLabel(int address, int word)
        {
            return address.ToString(CultureInfo.InvariantCulture) + ": " + Disassemble(word);
        }

        /// <summary>
        /// True when <paramref name="label"/> has the <see cref="WordLabel"/> form ("N: instruction"), so it can be
        /// kept in step with the word's value. Free-text labels return false.
        /// </summary>
        public bool IsWordLabel(string label, out int address)
        {
            address = -1;
            if (string.IsNullOrEmpty(label))
            {
                return false;
            }

            int colon = label.IndexOf(':');
            if (colon <= 0 || !int.TryParse(label.Substring(0, colon), NumberStyles.None, CultureInfo.InvariantCulture,
                    out int parsed))
            {
                return false;
            }

            if (!TryParseInstruction(label.Substring(colon + 1), out _, out _))
            {
                return false;
            }

            address = parsed;
            return true;
        }

        /// <summary>The program as text, one disassembled word per line with its address prefix.</summary>
        public string Listing(IReadOnlyList<int> words)
        {
            if (words == null)
            {
                throw new ArgumentNullException(nameof(words));
            }

            var builder = new StringBuilder(words.Count * 12);
            for (int i = 0; i < words.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append('\n');
                }

                builder.Append(WordLabel(i, words[i]));
            }

            return builder.ToString();
        }

        /// <summary>
        /// Parses a program: one instruction per line, blank lines and <c>;</c> comments ignored, an optional
        /// <c>N:</c> prefix that must equal the word's address, operands in decimal, <c>0x</c> or <c>0b</c>, and a
        /// bare number (or "? 0xC3") as a raw word. Missing words are padded with 0.
        /// </summary>
        public AssemblyResult Parse(string source, int wordCount)
        {
            if (wordCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(wordCount), wordCount, "A program has at least one word.");
            }

            var words = new int[wordCount];
            var errors = new List<AssemblyError>();
            if (Problem != null)
            {
                errors.Add(new AssemblyError(0, Problem));
                return new AssemblyResult(words, errors, wordCount);
            }

            int address = 0;
            string[] lines = (source ?? string.Empty).Replace("\r", string.Empty).Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                int lineNumber = i + 1;
                string text = lines[i];
                int comment = text.IndexOf(';');
                if (comment >= 0)
                {
                    text = text.Substring(0, comment);
                }

                text = text.Trim();
                if (text.Length == 0)
                {
                    continue;
                }

                int colon = text.IndexOf(':');
                if (colon >= 0)
                {
                    string prefix = text.Substring(0, colon).Trim();
                    if (!TryParseNumber(prefix, out int stated))
                    {
                        errors.Add(new AssemblyError(lineNumber, "'" + prefix + "' is not an address."));
                        address++;
                        continue;
                    }

                    if (stated != address)
                    {
                        errors.Add(new AssemblyError(lineNumber, "Address " + stated + " is out of order: this is word " +
                                                                 address + "."));
                        address++;
                        continue;
                    }

                    text = text.Substring(colon + 1).Trim();
                }

                if (address >= wordCount)
                {
                    errors.Add(new AssemblyError(lineNumber, "Too many instructions: the program holds " + wordCount +
                                                             " words."));
                    break;
                }

                if (TryParseInstruction(text, out int word, out string error))
                {
                    words[address] = word;
                }
                else
                {
                    errors.Add(new AssemblyError(lineNumber, error));
                }

                address++;
            }

            int used = Math.Min(address, wordCount);
            return new AssemblyResult(words, errors, wordCount - used);
        }

        /// <summary>
        /// Parses a number typed as decimal, <c>0x</c> hexadecimal or <c>0b</c> binary (underscores ignored).
        /// </summary>
        public static bool TryParseNumber(string text, out int value)
        {
            value = 0;
            text = (text ?? string.Empty).Trim().Replace("_", string.Empty);
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return int.TryParse(text.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture,
                    out value);
            }

            if (text.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
            {
                string digits = text.Substring(2);
                if (digits.Length == 0 || digits.Length > 16)
                {
                    return false;
                }

                for (int i = 0; i < digits.Length; i++)
                {
                    if (digits[i] != '0' && digits[i] != '1')
                    {
                        return false;
                    }

                    value = (value << 1) | (digits[i] - '0');
                }

                return true;
            }

            return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
        }

        private string Hex(int word)
        {
            return "0x" + word.ToString("X" + _hexDigits.ToString(CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture);
        }

        /// <summary>Parses one instruction ("LDA 3", "OUT", "0x2A", "? 0xC3") into a word.</summary>
        private bool TryParseInstruction(string text, out int word, out string error)
        {
            word = 0;
            error = null;
            text = (text ?? string.Empty).Trim();
            if (text.StartsWith("?", StringComparison.Ordinal))
            {
                text = text.Substring(1).Trim();
            }

            if (text.Length == 0)
            {
                error = "Missing instruction.";
                return false;
            }

            if (char.IsDigit(text[0]))
            {
                if (!TryParseNumber(text, out int raw) || raw > _wordMask)
                {
                    error = "'" + text + "' is not a " + WordWidth + "-bit word (0 to " + _wordMask + ").";
                    return false;
                }

                word = raw;
                return true;
            }

            int space = IndexOfWhiteSpace(text);
            string mnemonic = (space < 0 ? text : text.Substring(0, space)).ToUpperInvariant();
            string operandText = space < 0 ? string.Empty : text.Substring(space + 1).Trim();
            if (!UsesSet)
            {
                error = "'" + mnemonic + "' is not a number: raw words are numbers only.";
                return false;
            }

            if (!Set.TryGetInstruction(mnemonic, out Instruction instruction))
            {
                error = "Unknown instruction '" + mnemonic + "'.";
                return false;
            }

            if (!instruction.TakesOperand)
            {
                if (operandText.Length > 0)
                {
                    error = mnemonic + " takes no operand.";
                    return false;
                }

                word = instruction.Opcode << _operandBits;
                return true;
            }

            if (operandText.Length == 0)
            {
                error = mnemonic + " needs an operand (0 to " + _operandMask + ").";
                return false;
            }

            if (!TryParseNumber(operandText, out int operand) || operand > _operandMask)
            {
                error = "Operand '" + operandText + "' is out of range: use 0 to " + _operandMask + ".";
                return false;
            }

            word = (instruction.Opcode << _operandBits) | operand;
            return true;
        }

        private static int IndexOfWhiteSpace(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsWhiteSpace(text[i]))
                {
                    return i;
                }
            }

            return -1;
        }
    }

    /// <summary>A problem on one line of a program or instruction set spec.</summary>
    public readonly struct AssemblyError
    {
        public AssemblyError(int line, string message)
        {
            Line = line;
            Message = message;
        }

        /// <summary>1-based line number in the source text; 0 for a problem with the whole program.</summary>
        public int Line { get; }

        public string Message { get; }

        public override string ToString()
        {
            return Line > 0 ? "Line " + Line + ": " + Message : Message;
        }
    }

    /// <summary>Result of <see cref="Assembler.Parse"/>: the words (valid only without errors) and the errors.</summary>
    public sealed class AssemblyResult
    {
        internal AssemblyResult(int[] words, List<AssemblyError> errors, int paddedWords)
        {
            Words = words;
            Errors = errors;
            PaddedWords = paddedWords;
        }

        public IReadOnlyList<int> Words { get; }

        public IReadOnlyList<AssemblyError> Errors { get; }

        public bool HasErrors => Errors.Count > 0;

        /// <summary>Words after the last instruction, filled with 0.</summary>
        public int PaddedWords { get; }
    }
}
