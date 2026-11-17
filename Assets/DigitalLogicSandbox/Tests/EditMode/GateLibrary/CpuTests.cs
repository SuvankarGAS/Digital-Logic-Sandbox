using System;
using System.Collections.Generic;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.GateLibrary.Examples;
using DigitalLogicSandbox.Rendering;
using DigitalLogicSandbox.Simulation.Context;
using DigitalLogicSandbox.Tests.EditMode.Support;
using NUnit.Framework;

namespace DigitalLogicSandbox.Tests.EditMode.GateLibrary
{
    /// <summary>
    /// Phase E+: the CPU 4-bit and ROM 16×8 example gates, run through the real engine from a program held in
    /// Constant words. Expected values come from hand-traced programs, not from the CPU.
    /// </summary>
    public sealed class CpuTests
    {
        private TestEnvironment _env;
        private ExamplePack _pack;

        [SetUp]
        public void SetUp()
        {
            _env = new TestEnvironment();
            _pack = ExampleGatePack.Build(_env.Catalog.Primitives, new GeometryNodeSizer().Measure);
            foreach (CircuitDefinition definition in _pack.Definitions)
            {
                _env.Library.Add(definition);
            }
        }

        /// <summary>A computer driven by a CLK input switch, with PC, A, OUT and HALT as output boundaries.</summary>
        private sealed class Computer
        {
            public SimulationContext Context;
            public CircuitDefinition Circuit;
            public PortId Clock;
            public PortId Pc;
            public PortId A;
            public PortId Out;
            public PortId Halt;

            public ulong Read(PortId port)
            {
                Assert.That(Context.TryGetOutputValue(port, out ulong value), Is.True, "Output is not binary.");
                return value;
            }

            /// <summary>One instruction: a rising then a falling CLK edge, each settled.</summary>
            public void Cycle()
            {
                Context.SetInputValue(Clock, 1);
                Assert.That(Context.Settle().IsStable, Is.True, "Rising edge did not settle.");
                Context.SetInputValue(Clock, 0);
                Assert.That(Context.Settle().IsStable, Is.True, "Falling edge did not settle.");
            }
        }

        private Computer Build(byte[] program)
        {
            var circuit = new CircuitDefinition(DefinitionId.New(), "Harness");
            CpuWiring wiring = CpuDemo.AddComputer(circuit, _pack.Get("rom16x8"), _pack.Get("cpu4"), program, CircuitPoint.Zero);
            NodeDefinition clock = circuit.AddInputBoundary("CLK", new CircuitPoint(-20, -12));
            circuit.Connect(new PinRef(clock.Id, BoundarySignatures.Pin), wiring.Input(1));
            var computer = new Computer { Circuit = circuit, Clock = clock.InterfacePort };
            string[] names = { "PC", "A", "OUT", "HALT" };
            var ports = new PortId[4];
            for (int i = 0; i < 4; i++)
            {
                NodeDefinition output = circuit.AddOutputBoundary(names[i], new CircuitPoint(30, 6 - 3 * i), i == 3 ? 1 : 4);
                circuit.Connect(wiring.Output(i), new PinRef(output.Id, BoundarySignatures.Pin));
                ports[i] = output.InterfacePort;
            }

            computer.Pc = ports[0];
            computer.A = ports[1];
            computer.Out = ports[2];
            computer.Halt = ports[3];
            computer.Context = SimulationTestUtil.CreateContext(_env, circuit);
            return computer;
        }

        private static byte[] Program(params byte[] words)
        {
            var program = new byte[16];
            Array.Copy(words, program, words.Length);
            return program;
        }

        private static byte I(string mnemonic, int operand = 0) => CpuDemo.Assemble(mnemonic, operand);

        [Test]
        public void Assembler_EncodesOpcodeAndOperand_AndDisassemblesBack()
        {
            Assert.That(I("LDI", 13), Is.EqualTo(0x1D));
            Assert.That(I("hlt"), Is.EqualTo(0xF0));
            Assert.That(CpuDemo.Disassemble(0xA7), Is.EqualTo("JC 7"));
            Assert.That(CpuDemo.Disassemble(0xB0), Is.EqualTo("OUT"));
            Assert.That(CpuDemo.Disassemble(0xC3), Is.EqualTo("? 0xC3"));
            Assert.Throws<ArgumentException>(() => I("MUL", 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => I("LDI", 16));
        }

        [Test]
        public void Rom_ReturnsTheAddressedWord()
        {
            var random = new Random(1234);
            var words = new byte[16];
            random.NextBytes(words);
            var circuit = new CircuitDefinition(DefinitionId.New(), "Rom");
            CircuitDefinition rom = _pack.Get("rom16x8");
            NodeDefinition node = circuit.AddNode(ComponentRef.Custom(rom.Reference), CircuitPoint.Zero);
            for (int w = 0; w < 16; w++)
            {
                NodeDefinition word = circuit.AddNode(ComponentRef.Constant(8, words[w]), new CircuitPoint(-10, w));
                circuit.Connect(new PinRef(word.Id, BoundarySignatures.Pin), new PinRef(node.Id, rom.Inputs[w].Id));
            }

            NodeDefinition address = circuit.AddInputBoundary("ADDR", new CircuitPoint(-10, -3), 4);
            NodeDefinition data = circuit.AddOutputBoundary("DATA", new CircuitPoint(10, 0), 8);
            circuit.Connect(new PinRef(address.Id, BoundarySignatures.Pin), new PinRef(node.Id, rom.Inputs[16].Id));
            circuit.Connect(new PinRef(node.Id, rom.Outputs[0].Id), new PinRef(data.Id, BoundarySignatures.Pin));
            SimulationContext context = SimulationTestUtil.CreateContext(_env, circuit);
            for (int a = 0; a < 16; a++)
            {
                context.SetInputValue(address.InterfacePort, (ulong)a);
                Assert.That(context.Settle().IsStable, Is.True);
                Assert.That(context.TryGetOutputValue(data.InterfacePort, out ulong value), Is.True);
                Assert.That(value, Is.EqualTo((ulong)words[a]), "word " + a);
            }
        }

        [Test]
        public void PowersOn_AtPcZero_WithAZero()
        {
            Computer computer = Build(Program(I("LDI", 7)));
            Assert.That(computer.Read(computer.Pc), Is.Zero);
            Assert.That(computer.Read(computer.A), Is.Zero);
            Assert.That(computer.Read(computer.Out), Is.Zero);
            Assert.That(computer.Read(computer.Halt), Is.Zero);
        }

        [Test]
        public void Instructions_UpdatePcAndA_AndJumpOnFlags()
        {
            Computer computer = Build(Program(
                I("LDI", 5),    // 0  A = 5
                I("ADDI", 3),   // 1  A = 8, Z = 0, C = 0
                I("JZ", 15),    // 2  not taken
                I("JC", 15),    // 3  not taken
                I("SUBI", 8),   // 4  A = 0, Z = 1, C = 1 (no borrow)
                I("JZ", 7),     // 5  taken
                I("HLT"),       // 6  skipped
                I("JC", 9),     // 7  taken
                I("HLT"),       // 8  skipped
                I("ADDI", 15),  // 9  A = 15, Z = 0, C = 0
                I("JMP", 12),   // 10
                I("HLT"),       // 11 skipped
                I("OUT"),       // 12 OUT = 15
                I("NOP"),       // 13
                I("HLT"),       // 14 holds
                I("HLT")));     // 15

            int[,] expected =
            {
                { 1, 5 }, { 2, 8 }, { 3, 8 }, { 4, 8 }, { 5, 0 }, { 7, 0 }, { 9, 0 }, { 10, 15 }, { 12, 15 }, { 13, 15 },
                { 14, 15 }, { 14, 15 }, { 14, 15 },
            };
            for (int step = 0; step < expected.GetLength(0); step++)
            {
                computer.Cycle();
                string at = "after cycle " + (step + 1);
                Assert.That(computer.Read(computer.Pc), Is.EqualTo((ulong)expected[step, 0]), "PC " + at);
                Assert.That(computer.Read(computer.A), Is.EqualTo((ulong)expected[step, 1]), "A " + at);
            }

            Assert.That(computer.Read(computer.Out), Is.EqualTo(15UL));
            Assert.That(computer.Read(computer.Halt), Is.EqualTo(1UL));
        }

        [Test]
        public void Memory_StoresAndLoads_AndFeedsArithmetic()
        {
            Computer computer = Build(Program(
                I("LDI", 9), I("STA", 14),      // RAM[14] = 9
                I("LDI", 3), I("STA", 15),      // RAM[15] = 3
                I("LDI", 0), I("LDA", 14),      // A = 9
                I("ADD", 15),                   // A = 12
                I("SUB", 14),                   // A = 3
                I("OUT"), I("LDA", 15), I("HLT")));
            for (int i = 0; i < 6; i++)
            {
                computer.Cycle();
            }

            Assert.That(computer.Read(computer.A), Is.EqualTo(9UL), "LDA reads back what STA wrote.");
            computer.Cycle();
            Assert.That(computer.Read(computer.A), Is.EqualTo(12UL), "ADD n uses RAM[n].");
            computer.Cycle();
            Assert.That(computer.Read(computer.A), Is.EqualTo(3UL), "SUB n uses RAM[n].");
            computer.Cycle();
            Assert.That(computer.Read(computer.Out), Is.EqualTo(3UL));
            computer.Cycle();
            Assert.That(computer.Read(computer.A), Is.EqualTo(3UL), "RAM[15] kept its value.");
        }

        [Test]
        public void EveryRamAddress_RoundTrips()
        {
            for (int address = 0; address < 16; address++)
            {
                int value = (address * 5 + 3) & 15;
                Computer computer = Build(Program(I("LDI", value), I("STA", address), I("LDI", 0), I("LDA", address),
                    I("HLT")));
                for (int i = 0; i < 4; i++)
                {
                    computer.Cycle();
                }

                Assert.That(computer.Read(computer.A), Is.EqualTo((ulong)value), "address " + address);
            }
        }

        [Test]
        public void FibonacciDemo_PrintsTheSequence_AndHalts()
        {
            Computer computer = Build(CpuDemo.Fibonacci);
            var printed = new List<ulong>();
            int cycles = 0;
            while (computer.Read(computer.Halt) == 0)
            {
                Assert.That(cycles, Is.LessThan(200), "The program halts.");
                bool output = CpuDemo.Fibonacci[(int)computer.Read(computer.Pc)] >> 4 == 0xB;
                computer.Cycle();
                cycles++;
                if (output)
                {
                    printed.Add(computer.Read(computer.Out));
                }
            }

            Assert.That(printed, Is.EqualTo(new ulong[] { 1, 1, 2, 3, 5, 8 }));
            Assert.That(computer.Read(computer.Pc), Is.EqualTo(13UL));
            computer.Cycle();
            Assert.That(computer.Read(computer.Pc), Is.EqualTo(13UL), "HLT holds the PC.");
            Assert.That(computer.Read(computer.Out), Is.EqualTo(8UL));
        }

        [Test]
        public void ClockEdges_AreAllocationFree_AndBounded()
        {
            Computer computer = Build(Program(I("LDI", 1), I("ADDI", 1), I("STA", 3), I("JMP", 1)));
            SimulationContext context = computer.Context;
            for (int i = 0; i < 8; i++)
            {
                computer.Cycle(); // warm up
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            int worst = 0;
            for (int i = 0; i < 24; i++)
            {
                context.SetInputValue(computer.Clock, (ulong)((i + 1) & 1));
                worst = Math.Max(worst, context.Settle().Evaluations);
            }

            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.EqualTo(0), "Clock edges allocate nothing.");
            TestContext.WriteLine("Gates: " + context.Circuit.GateCount + ", worst edge: " + worst + " evaluations.");
            // Measured: up to 2 703 evaluations for 2 842 gates. In a single-cycle CPU a new instruction legitimately
            // ripples through the decoder, ROM, RAM read path, ALU and register inputs, counting re-evaluations
            // across waves. The bound guards against regressions such as glitch storms, not against that.
            Assert.That(worst, Is.LessThan(2 * context.Circuit.GateCount), "An edge settles in bounded work.");
        }
    }
}
