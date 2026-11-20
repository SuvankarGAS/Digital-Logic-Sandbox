using System.Collections.Generic;
using DigitalLogicSandbox.GateLibrary.Programs;

namespace DigitalLogicSandbox.GateLibrary.Examples
{
    /// <summary>
    /// Assembler for the example CPU 4-bit (opcode in bits 7–4, operand in bits 3–0): a fixed
    /// <see cref="Assembler"/> over <see cref="InstructionSet.ExampleCpu"/> with 8-bit words. Kept for the demo
    /// builder and tests; the program editor uses whatever instruction set the program gate is bound to.
    /// </summary>
    public static class CpuAssembler
    {
        private static Assembler _instance;

        /// <summary>The underlying generic assembler.</summary>
        public static Assembler Instance => _instance ??= new Assembler(InstructionSet.ExampleCpu, 8);

        /// <summary>Instruction set summary for help text.</summary>
        public static string Reference => Instance.Reference;

        /// <summary>True when the instruction takes an operand (everything except NOP, OUT and HLT).</summary>
        public static bool TakesOperand(string mnemonic)
        {
            return InstructionSet.ExampleCpu.TryGetInstruction(mnemonic, out Instruction instruction) &&
                   instruction.TakesOperand;
        }

        /// <summary>Encodes one instruction: opcode in bits 7–4, operand in bits 3–0.</summary>
        public static byte Assemble(string mnemonic, int operand = 0)
        {
            return (byte)Instance.Assemble(mnemonic, operand);
        }

        /// <summary>Readable, lossless form of an instruction word, e.g. "LDI 1", "OUT" or "? 0xC3".</summary>
        public static string Disassemble(int word)
        {
            return Instance.Disassemble(word);
        }

        /// <summary>Label shown on a program word: "address: instruction".</summary>
        public static string WordLabel(int address, int word)
        {
            return Instance.WordLabel(address, word);
        }

        /// <summary>True when <paramref name="label"/> has the <see cref="WordLabel"/> form ("N: instruction").</summary>
        public static bool IsWordLabel(string label, out int address)
        {
            return Instance.IsWordLabel(label, out address);
        }

        /// <summary>The program as text, one disassembled word per line with its address prefix.</summary>
        public static string Listing(IReadOnlyList<int> words)
        {
            return Instance.Listing(words);
        }

        /// <summary>Parses a program (see <see cref="Assembler.Parse"/>). Missing words are padded with NOP.</summary>
        public static AssemblyResult Parse(string source, int wordCount = 16)
        {
            return Instance.Parse(source, wordCount);
        }

        /// <summary>Parses a decimal, 0x or 0b number (see <see cref="Assembler.TryParseNumber"/>).</summary>
        public static bool TryParseNumber(string text, out int value)
        {
            return Assembler.TryParseNumber(text, out value);
        }
    }
}
