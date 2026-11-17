using System;
using System.Collections.Generic;
using DigitalLogicSandbox.Components.Abstractions;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;

namespace DigitalLogicSandbox.GateLibrary.Examples
{
    /// <summary>
    /// The CPU demo: the example pack plus a main circuit in which sixteen Constant program words feed the
    /// ROM 16×8, the ROM feeds the CPU 4-bit, and a clock and displays make it run visibly. Also holds the tiny
    /// assembler used to write the demo program (and by tests).
    /// </summary>
    public static class CpuDemo
    {
        /// <summary>Mnemonic of each opcode (null = unused, executes as NOP).</summary>
        private static readonly string[] Mnemonics =
        {
            "NOP", "LDI", "LDA", "STA", "ADD", "SUB", "ADDI", "SUBI", "JMP", "JZ", "JC", "OUT", null, null, null, "HLT",
        };

        /// <summary>Clock rate the demo project is saved with (instructions per second).</summary>
        public const float ClockFrequency = 4f;

        public const string MainCircuitName = "CPU demo";

        /// <summary>
        /// The demo program: prints Fibonacci numbers (1, 1, 2, 3, 5, 8) on OUT and halts when the next sum
        /// overflows 4 bits. RAM[0] and RAM[1] hold the last two numbers, RAM[2] is scratch.
        /// </summary>
        public static readonly byte[] Fibonacci =
        {
            Assemble("LDI", 1),  // 0
            Assemble("STA", 0),  // 1  a = 1
            Assemble("STA", 1),  // 2  b = 1
            Assemble("LDA", 0),  // 3  loop: A = a
            Assemble("OUT"),     // 4  show a
            Assemble("ADD", 1),  // 5  A = a + b
            Assemble("JC", 13),  // 6  overflow: stop
            Assemble("STA", 2),  // 7  t = a + b
            Assemble("LDA", 1),  // 8
            Assemble("STA", 0),  // 9  a = b
            Assemble("LDA", 2),  // 10
            Assemble("STA", 1),  // 11 b = t
            Assemble("JMP", 3),  // 12
            Assemble("HLT"),     // 13
            Assemble("NOP"),     // 14
            Assemble("NOP"),     // 15
        };

        /// <summary>Encodes one instruction: opcode in bits 7–4, operand in bits 3–0.</summary>
        public static byte Assemble(string mnemonic, int operand = 0)
        {
            int opcode = Array.IndexOf(Mnemonics, (mnemonic ?? string.Empty).Trim().ToUpperInvariant());
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

        /// <summary>Readable form of an instruction word, e.g. "LDI 1", "OUT" or "? 0xC3".</summary>
        public static string Disassemble(int word)
        {
            word &= 0xFF;
            string mnemonic = Mnemonics[word >> 4];
            if (mnemonic == null)
            {
                return "? 0x" + word.ToString("X2");
            }

            bool takesOperand = mnemonic != "NOP" && mnemonic != "OUT" && mnemonic != "HLT";
            return takesOperand ? mnemonic + " " + (word & 15) : mnemonic;
        }

        /// <summary>
        /// Places a ROM 16×8 fed by one labelled Constant per program word and a CPU 4-bit fetching from it
        /// (CPU PC → ROM ADDR, ROM DATA → CPU INSTR). The caller connects the CPU's CLK input and its outputs.
        /// </summary>
        public static CpuWiring AddComputer(CircuitDefinition circuit, CircuitDefinition rom, CircuitDefinition cpu,
            IReadOnlyList<byte> program, CircuitPoint origin)
        {
            if (circuit == null || rom == null || cpu == null || program == null)
            {
                throw new ArgumentNullException(circuit == null ? nameof(circuit) : rom == null ? nameof(rom) :
                    cpu == null ? nameof(cpu) : nameof(program));
            }

            if (program.Count != 16)
            {
                throw new ArgumentException("A program has exactly 16 words.", nameof(program));
            }

            NodeDefinition romNode = circuit.AddNode(ComponentRef.Custom(rom.Reference),
                new CircuitPoint(origin.X, origin.Y));
            NodeDefinition cpuNode = circuit.AddNode(ComponentRef.Custom(cpu.Reference),
                new CircuitPoint(origin.X + 12f, origin.Y));
            var words = new NodeId[16];
            for (int w = 0; w < 16; w++)
            {
                // Each word sits level with its ROM pin (pins are one unit apart, W0 on top), alternating between two
                // columns so neighbouring words do not overlap and every wire runs straight.
                NodeDefinition word = circuit.AddNode(ComponentRef.Constant(8, program[w]),
                    new CircuitPoint(origin.X - (w % 2 == 0 ? 7.5f : 13.5f), origin.Y + 8f - w));
                circuit.SetNodeLabel(word.Id, w + ": " + Disassemble(program[w]));
                circuit.Connect(new PinRef(word.Id, BoundarySignatures.Pin), new PinRef(romNode.Id, rom.Inputs[w].Id));
                words[w] = word.Id;
            }

            circuit.Connect(new PinRef(romNode.Id, rom.Outputs[0].Id), new PinRef(cpuNode.Id, cpu.Inputs[0].Id));
            circuit.Connect(new PinRef(cpuNode.Id, cpu.Outputs[0].Id), new PinRef(romNode.Id, rom.Inputs[16].Id));
            return new CpuWiring(cpuNode.Id, romNode.Id, words, cpu);
        }

        /// <summary>
        /// Builds the demo: the example pack (sized with <paramref name="sizer"/>) and a main circuit running
        /// <see cref="Fibonacci"/> with a clock and displays for OUT, A, PC and HALT.
        /// </summary>
        public static CpuDemoProject Build(PrimitiveRegistry primitives, NodeSizer sizer = null)
        {
            ExamplePack pack = ExampleGatePack.Build(primitives, sizer);
            var main = new CircuitDefinition(DefinitionId.New(), MainCircuitName)
            {
                Description = "A 4-bit CPU built from AND and NOT gates running a Fibonacci program. Press Play. " +
                              "Change a program word in the inspector to change the program.",
            };

            CpuWiring wiring = AddComputer(main, pack.Get("rom16x8"), pack.Get("cpu4"), Fibonacci, new CircuitPoint(-6f, 0f));
            NodeDefinition clock = main.AddNode(ComponentRef.Clock, new CircuitPoint(0f, -11f));
            main.Connect(new PinRef(clock.Id, BoundarySignatures.Pin), wiring.Input(1));

            // Top to bottom in the CPU's pin order (PC, A, OUT, HALT), so the wires do not cross.
            AddDisplay(main, wiring.Output(0), ComponentRef.Display(DisplayStyle.Hex, 4), new CircuitPoint(18f, 6f), "PC");
            AddDisplay(main, wiring.Output(1), ComponentRef.Display(DisplayStyle.Hex, 4), new CircuitPoint(18f, 1f), "A");
            AddDisplay(main, wiring.Output(2), ComponentRef.Display(DisplayStyle.Decimal, 4), new CircuitPoint(18f, -4f), "OUT");
            AddDisplay(main, wiring.Output(3), ComponentRef.Display(DisplayStyle.Led), new CircuitPoint(18f, -8f), "HALT");
            return new CpuDemoProject(pack.Definitions, main, ClockFrequency);
        }

        private static void AddDisplay(CircuitDefinition circuit, PinRef source, ComponentRef display, CircuitPoint at,
            string label)
        {
            NodeDefinition node = circuit.AddNode(display, at);
            circuit.SetNodeLabel(node.Id, label);
            circuit.Connect(source, new PinRef(node.Id, BoundarySignatures.Pin));
        }
    }

    /// <summary>Where <see cref="CpuDemo.AddComputer"/> put the CPU, the ROM and the program words.</summary>
    public sealed class CpuWiring
    {
        private readonly CircuitDefinition _cpu;

        internal CpuWiring(NodeId cpu, NodeId rom, NodeId[] words, CircuitDefinition cpuDefinition)
        {
            Cpu = cpu;
            Rom = rom;
            Words = words;
            _cpu = cpuDefinition;
        }

        public NodeId Cpu { get; }

        public NodeId Rom { get; }

        /// <summary>The Constant node of each program word, by address.</summary>
        public IReadOnlyList<NodeId> Words { get; }

        /// <summary>CPU input pin by interface index (0 = INSTR, 1 = CLK).</summary>
        public PinRef Input(int index)
        {
            return new PinRef(Cpu, _cpu.Inputs[index].Id);
        }

        /// <summary>CPU output pin by interface index (0 = PC, 1 = A, 2 = OUT, 3 = HALT).</summary>
        public PinRef Output(int index)
        {
            return new PinRef(Cpu, _cpu.Outputs[index].Id);
        }
    }

    /// <summary>Everything a CPU demo project needs: definitions in dependency order, the main circuit and the clock rate.</summary>
    public sealed class CpuDemoProject
    {
        internal CpuDemoProject(IReadOnlyList<CircuitDefinition> definitions, CircuitDefinition main, float clockFrequency)
        {
            Definitions = definitions;
            Main = main;
            ClockFrequency = clockFrequency;
        }

        public IReadOnlyList<CircuitDefinition> Definitions { get; }

        public CircuitDefinition Main { get; }

        public float ClockFrequency { get; }
    }
}
