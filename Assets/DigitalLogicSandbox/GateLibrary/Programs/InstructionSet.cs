using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DigitalLogicSandbox.GateLibrary.Programs
{
    /// <summary>One instruction of an <see cref="InstructionSet"/>.</summary>
    public sealed class Instruction
    {
        public Instruction(string mnemonic, int opcode, string operandName, string description)
        {
            Mnemonic = mnemonic ?? throw new ArgumentNullException(nameof(mnemonic));
            Opcode = opcode;
            OperandName = string.IsNullOrEmpty(operandName) ? null : operandName;
            Description = description ?? string.Empty;
        }

        /// <summary>Upper-case mnemonic, e.g. "LDI".</summary>
        public string Mnemonic { get; }

        /// <summary>Value of the opcode field (the word's top bits).</summary>
        public int Opcode { get; }

        /// <summary>Name of the operand in help text ("n"), or null when the instruction takes no operand.</summary>
        public string OperandName { get; }

        public bool TakesOperand => OperandName != null;

        /// <summary>Short explanation from the spec's trailing comment ("A = n"); may be empty.</summary>
        public string Description { get; }
    }

    /// <summary>
    /// A user-definable instruction set: an opcode field in the top bits of each program word and an optional operand
    /// in the remaining low bits. Written as a small text spec (see <see cref="Parse"/>), so the program editor can
    /// assemble for any CPU the user builds, not only the example one. Immutable; pure data.
    /// </summary>
    public sealed class InstructionSet
    {
        /// <summary>Widest opcode field a spec may declare (constants are at most 8 bits wide).</summary>
        public const int MaxOpcodeBits = 8;

        /// <summary>Longest mnemonic accepted.</summary>
        public const int MaxMnemonicLength = 12;

        /// <summary>The spec keyword that declares the opcode width.</summary>
        public const string OpcodeKeyword = "opcode";

        /// <summary>Starting text for a new instruction set.</summary>
        public const string Template =
            "; One line per instruction: MNEMONIC opcode [operand]. A trailing comment describes it.\n" +
            "; The opcode sits in the word's top bits; the operand (if any) gets the rest.\n" +
            "opcode 2\n" +
            "NOP 0      ; do nothing\n" +
            "LDI 1 n    ; A = n\n" +
            "JMP 2 n    ; jump to n\n" +
            "HLT 3      ; stop\n";

        /// <summary>The example CPU 4-bit's instruction set, written as a spec.</summary>
        public const string ExampleCpuSource =
            "; Example CPU 4-bit: opcode in bits 7-4, operand n in bits 3-0.\n" +
            "opcode 4\n" +
            "NOP  0       ; do nothing\n" +
            "LDI  1  n    ; A = n\n" +
            "LDA  2  n    ; A = RAM[n]\n" +
            "STA  3  n    ; RAM[n] = A\n" +
            "ADD  4  n    ; A + RAM[n]\n" +
            "SUB  5  n    ; A - RAM[n]\n" +
            "ADDI 6  n    ; A + n\n" +
            "SUBI 7  n    ; A - n\n" +
            "JMP  8  n    ; jump to n\n" +
            "JZ   9  n    ; jump if zero\n" +
            "JC   10 n    ; jump if carry\n" +
            "OUT  11      ; show A\n" +
            "HLT  15      ; stop\n";

        private static InstructionSet _exampleCpu;

        private readonly Instruction[] _instructions;
        private readonly Dictionary<string, Instruction> _byMnemonic;
        private readonly Dictionary<int, Instruction> _byOpcode;

        private InstructionSet(string name, int opcodeBits, List<Instruction> instructions)
        {
            Name = name;
            OpcodeBits = opcodeBits;
            _instructions = instructions.ToArray();
            _byMnemonic = new Dictionary<string, Instruction>(_instructions.Length, StringComparer.Ordinal);
            _byOpcode = new Dictionary<int, Instruction>(_instructions.Length);
            for (int i = 0; i < _instructions.Length; i++)
            {
                _byMnemonic[_instructions[i].Mnemonic] = _instructions[i];
                _byOpcode[_instructions[i].Opcode] = _instructions[i];
            }
        }

        /// <summary>The example CPU 4-bit's set (parsed once from <see cref="ExampleCpuSource"/>).</summary>
        public static InstructionSet ExampleCpu
        {
            get
            {
                if (_exampleCpu == null)
                {
                    InstructionSetParseResult result = Parse(ExampleCpuSource, "Example CPU");
                    if (result.HasErrors)
                    {
                        throw new InvalidOperationException("The built-in instruction set does not parse: " +
                                                            result.Errors[0]);
                    }

                    _exampleCpu = result.Set;
                }

                return _exampleCpu;
            }
        }

        public string Name { get; }

        /// <summary>Width of the opcode field in bits.</summary>
        public int OpcodeBits { get; }

        /// <summary>Instructions in spec order.</summary>
        public IReadOnlyList<Instruction> Instructions => _instructions;

        /// <summary>True when at least one instruction takes an operand.</summary>
        public bool HasOperands
        {
            get
            {
                for (int i = 0; i < _instructions.Length; i++)
                {
                    if (_instructions[i].TakesOperand)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>Finds an instruction by mnemonic (case-insensitive).</summary>
        public bool TryGetInstruction(string mnemonic, out Instruction instruction)
        {
            instruction = null;
            return !string.IsNullOrEmpty(mnemonic) &&
                   _byMnemonic.TryGetValue(mnemonic.Trim().ToUpperInvariant(), out instruction);
        }

        /// <summary>
        /// Finds an instruction by mnemonic in <paramref name="text"/>[start, start+length), without allocating when
        /// the text is already upper case. Used by syntax colouring.
        /// </summary>
        public bool ContainsMnemonic(string text, int start, int length)
        {
            if (text == null || length <= 0 || length > MaxMnemonicLength)
            {
                return false;
            }

            foreach (KeyValuePair<string, Instruction> pair in _byMnemonic)
            {
                string key = pair.Key;
                if (key.Length != length)
                {
                    continue;
                }

                bool equal = true;
                for (int i = 0; i < length; i++)
                {
                    if (char.ToUpperInvariant(text[start + i]) != key[i])
                    {
                        equal = false;
                        break;
                    }
                }

                if (equal)
                {
                    return true;
                }
            }

            return false;
        }

        public bool TryGetInstruction(int opcode, out Instruction instruction)
        {
            return _byOpcode.TryGetValue(opcode, out instruction);
        }

        /// <summary>
        /// Why this set cannot encode words of <paramref name="wordWidth"/> bits, or null when it can. The opcode must
        /// fit the word, and leave at least one operand bit when any instruction takes an operand.
        /// </summary>
        public string CheckWordWidth(int wordWidth)
        {
            if (OpcodeBits > wordWidth)
            {
                return "The instruction set needs " + OpcodeBits + "-bit opcodes, but the program words are " +
                       wordWidth + "-bit.";
            }

            if (OpcodeBits == wordWidth && HasOperands)
            {
                return "The instruction set's " + OpcodeBits + "-bit opcodes leave no room for operands in " +
                       wordWidth + "-bit words.";
            }

            return null;
        }

        /// <summary>
        /// Help text for the program editor, e.g. "NOP (do nothing) · LDI n (A = n) · … n is 0–15; a bare number
        /// is a raw word; ';' starts a comment."
        /// </summary>
        public string Reference(int wordWidth)
        {
            var builder = new StringBuilder(_instructions.Length * 16 + 64);
            var operandNames = new List<string>(2);
            for (int i = 0; i < _instructions.Length; i++)
            {
                Instruction instruction = _instructions[i];
                if (i > 0)
                {
                    builder.Append(" · ");
                }

                builder.Append(instruction.Mnemonic);
                if (instruction.TakesOperand)
                {
                    builder.Append(' ').Append(instruction.OperandName);
                    if (!operandNames.Contains(instruction.OperandName))
                    {
                        operandNames.Add(instruction.OperandName);
                    }
                }

                if (instruction.Description.Length > 0)
                {
                    builder.Append(" (").Append(instruction.Description).Append(')');
                }
            }

            builder.Append(". ");
            int operandBits = wordWidth - OpcodeBits;
            if (operandNames.Count > 0 && operandBits > 0)
            {
                builder.Append(string.Join(", ", operandNames)).Append(operandNames.Count == 1 ? " is " : " are ")
                    .Append("0–").Append(((1 << operandBits) - 1).ToString(CultureInfo.InvariantCulture)).Append("; ");
            }

            builder.Append("a bare number is a raw word; ';' starts a comment.");
            return builder.ToString();
        }

        /// <summary>
        /// Parses a spec. Format: <c>;</c> comments; a first statement <c>opcode N</c> (opcode field width, 1–8
        /// bits); then one instruction per line, <c>MNEMONIC opcode [operand]</c>, where a third token (any name, e.g.
        /// <c>n</c>) means the instruction takes an operand and a trailing comment becomes its description. Mnemonics
        /// and opcodes must be unique; opcodes must fit the opcode field.
        /// </summary>
        public static InstructionSetParseResult Parse(string spec, string name)
        {
            var errors = new List<AssemblyError>();
            var instructions = new List<Instruction>();
            var mnemonics = new HashSet<string>(StringComparer.Ordinal);
            var opcodes = new HashSet<int>();
            int opcodeBits = 0;
            var tokens = new List<string>(4);
            string[] lines = (spec ?? string.Empty).Replace("\r", string.Empty).Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                int lineNumber = i + 1;
                string text = lines[i];
                string description = string.Empty;
                int comment = text.IndexOf(';');
                if (comment >= 0)
                {
                    description = text.Substring(comment + 1).Trim();
                    text = text.Substring(0, comment);
                }

                Split(text, tokens);
                if (tokens.Count == 0)
                {
                    continue;
                }

                if (string.Equals(tokens[0], OpcodeKeyword, StringComparison.OrdinalIgnoreCase))
                {
                    if (opcodeBits != 0)
                    {
                        errors.Add(new AssemblyError(lineNumber, "The opcode width is already set."));
                        continue;
                    }

                    if (tokens.Count != 2 || !Assembler.TryParseNumber(tokens[1], out int bits) || bits < 1 ||
                        bits > MaxOpcodeBits)
                    {
                        errors.Add(new AssemblyError(lineNumber,
                            "Write 'opcode N' with N from 1 to " + MaxOpcodeBits + " bits."));
                        opcodeBits = -1;
                        continue;
                    }

                    if (instructions.Count > 0)
                    {
                        errors.Add(new AssemblyError(lineNumber, "'opcode N' must come before the instructions."));
                    }

                    opcodeBits = bits;
                    continue;
                }

                if (opcodeBits == 0)
                {
                    errors.Add(new AssemblyError(lineNumber, "Start with 'opcode N' (the opcode width in bits)."));
                    opcodeBits = -1;
                }

                string mnemonic = tokens[0].ToUpperInvariant();
                if (!IsIdentifier(mnemonic))
                {
                    errors.Add(new AssemblyError(lineNumber, "'" + tokens[0] +
                        "' is not a mnemonic: use letters, digits and '_', starting with a letter."));
                    continue;
                }

                if (mnemonic.Length > MaxMnemonicLength)
                {
                    errors.Add(new AssemblyError(lineNumber, "'" + tokens[0] + "' is longer than " +
                                                             MaxMnemonicLength + " characters."));
                    continue;
                }

                if (tokens.Count < 2 || tokens.Count > 3)
                {
                    errors.Add(new AssemblyError(lineNumber, "Write " + mnemonic + " opcode [operand], e.g. '" +
                                                             mnemonic + " 1 n'."));
                    continue;
                }

                if (!Assembler.TryParseNumber(tokens[1], out int opcode))
                {
                    errors.Add(new AssemblyError(lineNumber, "'" + tokens[1] + "' is not an opcode number."));
                    continue;
                }

                if (opcodeBits > 0 && opcode >= 1 << opcodeBits)
                {
                    errors.Add(new AssemblyError(lineNumber, "Opcode " + opcode + " does not fit in " + opcodeBits +
                        " bits (0 to " + ((1 << opcodeBits) - 1) + ")."));
                    continue;
                }

                string operand = tokens.Count == 3 ? tokens[2] : null;
                if (operand != null && !IsIdentifier(operand))
                {
                    errors.Add(new AssemblyError(lineNumber, "'" + operand +
                        "' is not an operand name: use a word such as n or addr."));
                    continue;
                }

                if (!mnemonics.Add(mnemonic))
                {
                    errors.Add(new AssemblyError(lineNumber, mnemonic + " is defined twice."));
                    continue;
                }

                if (!opcodes.Add(opcode))
                {
                    errors.Add(new AssemblyError(lineNumber, "Opcode " + opcode + " is already used."));
                    continue;
                }

                instructions.Add(new Instruction(mnemonic, opcode, operand, description));
            }

            if (errors.Count == 0 && opcodeBits == 0)
            {
                errors.Add(new AssemblyError(1, "Start with 'opcode N' (the opcode width in bits)."));
            }

            if (errors.Count == 0 && instructions.Count == 0)
            {
                errors.Add(new AssemblyError(lines.Length, "Define at least one instruction."));
            }

            InstructionSet set = errors.Count == 0
                ? new InstructionSet(string.IsNullOrWhiteSpace(name) ? "Instruction set" : name.Trim(), opcodeBits,
                    instructions)
                : null;
            return new InstructionSetParseResult(set, errors);
        }

        /// <summary>True for a letter followed by letters, digits or underscores.</summary>
        internal static bool IsIdentifier(string text)
        {
            if (string.IsNullOrEmpty(text) || !char.IsLetter(text[0]))
            {
                return false;
            }

            for (int i = 1; i < text.Length; i++)
            {
                if (!char.IsLetterOrDigit(text[i]) && text[i] != '_')
                {
                    return false;
                }
            }

            return true;
        }

        private static void Split(string text, List<string> tokens)
        {
            tokens.Clear();
            int start = -1;
            for (int i = 0; i <= text.Length; i++)
            {
                bool space = i == text.Length || char.IsWhiteSpace(text[i]);
                if (space && start >= 0)
                {
                    tokens.Add(text.Substring(start, i - start));
                    start = -1;
                }
                else if (!space && start < 0)
                {
                    start = i;
                }
            }
        }
    }

    /// <summary>Result of <see cref="InstructionSet.Parse"/>: the set (null when there are errors) and the errors.</summary>
    public sealed class InstructionSetParseResult
    {
        internal InstructionSetParseResult(InstructionSet set, List<AssemblyError> errors)
        {
            Set = set;
            Errors = errors;
        }

        public InstructionSet Set { get; }

        public IReadOnlyList<AssemblyError> Errors { get; }

        public bool HasErrors => Errors.Count > 0;
    }
}
