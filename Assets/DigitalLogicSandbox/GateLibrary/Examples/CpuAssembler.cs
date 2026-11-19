using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DigitalLogicSandbox.GateLibrary.Examples
{
    /// <summary>
    /// Assembler for the example CPU 4-bit (opcode in bits 7–4, operand in bits 3–0). It encodes and decodes single
    /// words, parses a whole program (one instruction per line) with line-numbered errors, and produces the listing
    /// and word labels ("3: LDA 0") shown on program Constants. Pure data: no Unity or editor dependencies.
    /// </summary>
    public static class CpuAssembler
    {
        /// <summary>Mnemonic of each opcode (null = unused, executes as NOP).</summary>
        private static readonly string[] Mnemonics =
        {
            "NOP", "LDI", "LDA", "STA", "ADD", "SUB", "ADDI", "SUBI", "JMP", "JZ", "JC", "OUT", null, null, null, "HLT",
        };

        /// <summary>Instruction set summary for help text.</summary>
        public const string Reference =
            "NOP · LDI n (A = n) · LDA n (A = RAM[n]) · STA n (RAM[n] = A) · ADD n / SUB n (A ± RAM[n]) · " +
            "ADDI n / SUBI n (A ± n) · JMP n · JZ n · JC n · OUT · HLT. n is 0–15; a bare number is a raw word; " +
            "';' starts a comment.";

        /// <summary>True when the instruction takes an operand (everything except NOP, OUT and HLT).</summary>
        public static bool TakesOperand(string mnemonic)
        {
            return mnemonic != "NOP" && mnemonic != "OUT" && mnemonic != "HLT";
        }

        /// <summary>Encodes one instruction: opcode in bits 7–4, operand in bits 3–0.</summary>
        public static byte Assemble(string mnemonic, int operand = 0)
        {
            int opcode = OpcodeOf(mnemonic);
            if (opcode < 0)
            {
                throw new ArgumentException("Unknown instruction '" + mnemonic + "'.", nameof(mnemonic));
            }

            if (operand < 0 || operand > 15)
            {
                throw new ArgumentOutOfRangeException(nameof(operand), operand, "Operands are 0 to 15.");
            }

            return (byte)((opcode << 4) | operand);
        }

        /// <summary>
        /// Readable form of an instruction word, e.g. "LDI 1", "OUT" or "? 0xC3". Lossless: unused opcodes, and
        /// NOP/OUT/HLT with stray operand bits, are shown as raw words so the listing parses back to the same value.
        /// </summary>
        public static string Disassemble(int word)
        {
            word &= 0xFF;
            string mnemonic = Mnemonics[word >> 4];
            if (mnemonic == null || (!TakesOperand(mnemonic) && (word & 15) != 0))
            {
                return "? 0x" + word.ToString("X2", CultureInfo.InvariantCulture);
            }

            return TakesOperand(mnemonic) ? mnemonic + " " + (word & 15) : mnemonic;
        }

        /// <summary>Label shown on a program word: "address: instruction".</summary>
        public static string WordLabel(int address, int word)
        {
            return address.ToString(CultureInfo.InvariantCulture) + ": " + Disassemble(word);
        }

        /// <summary>
        /// True when <paramref name="label"/> has the <see cref="WordLabel"/> form ("N: instruction"), so it can be
        /// kept in step with the word's value. Free-text labels return false.
        /// </summary>
        public static bool IsWordLabel(string label, out int address)
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
        public static string Listing(IReadOnlyList<int> words)
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
        /// bare number (or "? 0xC3") as a raw 8-bit word. Missing words are padded with NOP.
        /// </summary>
        public static AssemblyResult Parse(string source, int wordCount = 16)
        {
            if (wordCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(wordCount), wordCount, "A program has at least one word.");
            }

            var words = new byte[wordCount];
            var errors = new List<AssemblyError>();
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

                if (TryParseInstruction(text, out byte word, out string error))
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

        private static int OpcodeOf(string mnemonic)
        {
            return Array.IndexOf(Mnemonics, (mnemonic ?? string.Empty).Trim().ToUpperInvariant());
        }

        /// <summary>Parses one instruction ("LDA 3", "OUT", "0x2A", "? 0xC3") into a word.</summary>
        private static bool TryParseInstruction(string text, out byte word, out string error)
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
                if (!TryParseNumber(text, out int raw) || raw > 0xFF)
                {
                    error = "'" + text + "' is not an 8-bit word (0 to 255).";
                    return false;
                }

                word = (byte)raw;
                return true;
            }

            int space = IndexOfWhiteSpace(text);
            string mnemonic = (space < 0 ? text : text.Substring(0, space)).ToUpperInvariant();
            string operandText = space < 0 ? string.Empty : text.Substring(space + 1).Trim();
            int opcode = OpcodeOf(mnemonic);
            if (opcode < 0)
            {
                error = "Unknown instruction '" + mnemonic + "'.";
                return false;
            }

            if (!TakesOperand(mnemonic))
            {
                if (operandText.Length > 0)
                {
                    error = mnemonic + " takes no operand.";
                    return false;
                }

                word = (byte)(opcode << 4);
                return true;
            }

            if (operandText.Length == 0)
            {
                error = mnemonic + " needs an operand (0 to 15).";
                return false;
            }

            if (!TryParseNumber(operandText, out int operand) || operand > 15)
            {
                error = "Operand '" + operandText + "' is out of range: use 0 to 15.";
                return false;
            }

            word = (byte)((opcode << 4) | operand);
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

    /// <summary>A problem on one line of a program.</summary>
    public readonly struct AssemblyError
    {
        public AssemblyError(int line, string message)
        {
            Line = line;
            Message = message;
        }

        /// <summary>1-based line number in the source text.</summary>
        public int Line { get; }

        public string Message { get; }

        public override string ToString()
        {
            return "Line " + Line + ": " + Message;
        }
    }

    /// <summary>Result of <see cref="CpuAssembler.Parse"/>: the words (valid only without errors) and the errors.</summary>
    public sealed class AssemblyResult
    {
        internal AssemblyResult(byte[] words, List<AssemblyError> errors, int paddedWords)
        {
            Words = words;
            Errors = errors;
            PaddedWords = paddedWords;
        }

        public IReadOnlyList<byte> Words { get; }

        public IReadOnlyList<AssemblyError> Errors { get; }

        public bool HasErrors => Errors.Count > 0;

        /// <summary>Words after the last instruction, filled with NOP.</summary>
        public int PaddedWords { get; }
    }
}
