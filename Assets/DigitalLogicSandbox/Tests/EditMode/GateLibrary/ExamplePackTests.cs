using System;
using System.Collections.Generic;
using DigitalLogicSandbox.Commands.Library;
using DigitalLogicSandbox.Commands.UndoRedo;
using DigitalLogicSandbox.Components.Abstractions;
using DigitalLogicSandbox.Components.BuiltIn;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.GateLibrary.Custom;
using DigitalLogicSandbox.GateLibrary.Examples;
using DigitalLogicSandbox.GateLibrary.Packaging;
using DigitalLogicSandbox.Persistence;
using DigitalLogicSandbox.Persistence.Deserialization;
using DigitalLogicSandbox.Persistence.Serialization;
using DigitalLogicSandbox.Rendering;
using DigitalLogicSandbox.Simulation.Context;
using DigitalLogicSandbox.Tests.EditMode.Support;
using NUnit.Framework;

namespace DigitalLogicSandbox.Tests.EditMode.GateLibrary
{
    /// <summary>
    /// Phase E example pack: every gate is built from AND/NOT (plus wiring and earlier pack gates) and is checked
    /// exhaustively against a C# reference through the real simulation engine. Nothing here asks the pack what a
    /// gate should do: the expected values are computed independently.
    /// </summary>
    public sealed class ExamplePackTests
    {
        private TestEnvironment _env;
        private ExamplePackPlan _plan;

        [SetUp]
        public void SetUp()
        {
            _env = new TestEnvironment();
            _plan = ExampleGatePack.Prepare(_env.Catalog.Primitives, _env.Library, new GeometryNodeSizer().Measure);
            Assert.That(_plan.Succeeded, Is.True, _plan.Error);
            foreach (CircuitDefinition definition in _plan.Definitions)
            {
                _env.Library.Add(definition);
            }
        }

        private CircuitDefinition Gate(string key)
        {
            Assert.That(_env.Library.TryGetLatest(new DefinitionId(ExampleGatePack.IdPrefix + key), out CircuitDefinition gate),
                Is.True, key);
            return gate;
        }

        private SimulationContext Context(string key, out CircuitDefinition gate)
        {
            gate = Gate(key);
            return SimulationTestUtil.CreateContext(_env, gate);
        }

        private static void Set(SimulationContext context, CircuitDefinition gate, string port, ulong value)
        {
            foreach (var input in gate.Inputs)
            {
                if (input.Name == port)
                {
                    context.SetInputValue(input.Id, value); // returns "changed", not success
                    return;
                }
            }

            Assert.Fail("No input " + port);
        }

        private static ulong Get(SimulationContext context, CircuitDefinition gate, string port)
        {
            foreach (var output in gate.Outputs)
            {
                if (output.Name == port)
                {
                    Assert.That(context.TryGetOutputValue(output.Id, out ulong value), Is.True, port + " is not binary");
                    return value;
                }
            }

            Assert.Fail("No output " + port);
            return 0;
        }

        private static void Settle(SimulationContext context)
        {
            Assert.That(context.Settle().IsStable, Is.True, "Did not settle.");
        }

        // ------------------------------------------------------------------------------------------------
        // Structure
        // ------------------------------------------------------------------------------------------------

        [Test]
        public void Pack_HasTwentySevenValidGates_BuiltOnlyFromAndNotWiringAndEarlierPackGates()
        {
            Assert.That(_plan.Definitions.Count, Is.EqualTo(27));
            var packager = new CircuitPackager(_env.Catalog, _env.Library);
            var earlier = new HashSet<DefinitionId>();
            foreach (CircuitDefinition gate in _plan.Definitions)
            {
                Assert.That(ExampleGatePack.IsExampleId(gate.Id), Is.True);
                Assert.That(gate.Category, Does.StartWith("Examples · "));
                Assert.That(gate.Description, Is.Not.Empty);
                Assert.That(packager.ValidateDefinition(gate).HasErrors, Is.False, gate.Name);
                foreach (NodeDefinition node in gate.Nodes)
                {
                    switch (node.Component.Kind)
                    {
                        case ComponentKind.Primitive:
                            Assert.That(node.Component.PrimitiveId, Is.EqualTo(BuiltInComponentIds.And).Or.EqualTo(BuiltInComponentIds.Not),
                                gate.Name + " uses only the AND and NOT primitives.");
                            break;
                        case ComponentKind.Custom:
                            Assert.That(earlier, Does.Contain(node.Component.Definition.Id),
                                gate.Name + " references only earlier pack gates.");
                            break;
                        case ComponentKind.InputBoundary:
                        case ComponentKind.OutputBoundary:
                        case ComponentKind.Splitter:
                        case ComponentKind.Merger:
                        case ComponentKind.Constant: // itself compiled to AND/NOT gates
                            break;
                        default:
                            Assert.Fail(gate.Name + " contains a " + node.Component.Kind + ".");
                            break;
                    }
                }

                earlier.Add(gate.Id);
            }
        }

        [Test]
        public void NestedGates_ShareDefinitions_InsteadOfCopyingGraphs()
        {
            CircuitDefinition halfAdder = Gate("half-adder");
            CircuitDefinition fullAdder = Gate("full-adder");
            int instances = 0;
            foreach (NodeDefinition node in fullAdder.Nodes)
            {
                if (node.Component.Kind == ComponentKind.Custom && node.Component.Definition == halfAdder.Reference)
                {
                    instances++;
                }
            }

            Assert.That(instances, Is.EqualTo(2));
            Assert.That(fullAdder.Nodes.Count, Is.LessThan(halfAdder.Nodes.Count * 2), "No inlined copies.");
        }

        [Test]
        public void Layout_KeepsBodiesApart_AndReadsLeftToRight()
        {
            var geometry = new CircuitGeometry(_env.Catalog);
            foreach (CircuitDefinition gate in _plan.Definitions)
            {
                geometry.Bind(gate);
                IReadOnlyList<NodeDefinition> nodes = gate.Nodes;
                float inputsRight = float.MinValue;
                float outputsLeft = float.MaxValue;
                for (int i = 0; i < nodes.Count; i++)
                {
                    UnityEngine.Rect a = geometry.GetBounds(nodes[i].Id);
                    if (nodes[i].Component.Kind == ComponentKind.InputBoundary)
                    {
                        inputsRight = Math.Max(inputsRight, a.xMax);
                    }
                    else if (nodes[i].Component.Kind == ComponentKind.OutputBoundary)
                    {
                        outputsLeft = Math.Min(outputsLeft, a.xMin);
                    }

                    for (int j = i + 1; j < nodes.Count; j++)
                    {
                        Assert.That(a.Overlaps(geometry.GetBounds(nodes[j].Id)), Is.False,
                            gate.Name + ": " + nodes[i].Component + " overlaps " + nodes[j].Component);
                    }
                }

                Assert.That(inputsRight, Is.LessThan(outputsLeft), gate.Name + ": inputs sit left of outputs.");
            }
        }

        // ------------------------------------------------------------------------------------------------
        // Combinational truth tables (exhaustive)
        // ------------------------------------------------------------------------------------------------

        [TestCase("nand", 0b0111)]
        [TestCase("or", 0b1110)]
        [TestCase("nor", 0b0001)]
        [TestCase("xor", 0b0110)]
        public void TwoInputGates_MatchTheirTruthTables(string key, int outByRow)
        {
            // Bit r of outByRow is the output for row r = A*2 + B, so 0b0111 means rows 0..2 are HIGH.
            SimulationContext context = Context(key, out CircuitDefinition gate);
            for (int row = 0; row < 4; row++)
            {
                Set(context, gate, "A", (ulong)(row >> 1));
                Set(context, gate, "B", (ulong)(row & 1));
                Settle(context);
                Assert.That(Get(context, gate, "OUT"), Is.EqualTo((ulong)((outByRow >> row) & 1)), key + " row " + row);
            }
        }

        [Test]
        public void Multiplexers_SelectTheAddressedInput()
        {
            SimulationContext mux2 = Context("mux2", out CircuitDefinition two);
            for (int row = 0; row < 8; row++)
            {
                ulong s = (ulong)(row >> 2), a = (ulong)((row >> 1) & 1), b = (ulong)(row & 1);
                Set(mux2, two, "S", s);
                Set(mux2, two, "A", a);
                Set(mux2, two, "B", b);
                Settle(mux2);
                Assert.That(Get(mux2, two, "OUT"), Is.EqualTo(s == 0 ? a : b), "MUX 2:1 row " + row);
            }

            SimulationContext mux4 = Context("mux4", out CircuitDefinition four);
            for (int row = 0; row < 64; row++)
            {
                ulong s = (ulong)(row >> 4);
                Set(mux4, four, "S", s);
                for (int i = 0; i < 4; i++)
                {
                    Set(mux4, four, "I" + i, (ulong)((row >> i) & 1));
                }

                Settle(mux4);
                Assert.That(Get(mux4, four, "OUT"), Is.EqualTo((ulong)((row >> (int)s) & 1)), "MUX 4:1 row " + row);
            }
        }

        [Test]
        public void ControlUnit_DecodesEveryOpcode_WithBothFlags()
        {
            // Expected signals written from the instruction set, not from the gate.
            SimulationContext context = Context("ctrl4", out CircuitDefinition gate);
            for (int row = 0; row < 64; row++)
            {
                int op = row >> 2;
                int z = (row >> 1) & 1;
                int c = row & 1;
                Set(context, gate, "OPCODE", (ulong)op);
                Set(context, gate, "Z", (ulong)z);
                Set(context, gate, "C", (ulong)c);
                Settle(context);
                bool arithmetic = op >= 4 && op <= 7;
                string at = "opcode " + op + ", Z " + z + ", C " + c;
                Assert.That(Get(context, gate, "IMM"), Is.EqualTo(op == 1 || op == 6 || op == 7 ? 1UL : 0UL), "IMM " + at);
                Assert.That(Get(context, gate, "ARITH"), Is.EqualTo(arithmetic ? 1UL : 0UL), "ARITH " + at);
                Assert.That(Get(context, gate, "SUB"), Is.EqualTo(op == 5 || op == 7 ? 1UL : 0UL), "SUB " + at);
                Assert.That(Get(context, gate, "LOADA"), Is.EqualTo(arithmetic || op == 1 || op == 2 ? 1UL : 0UL),
                    "LOADA " + at);
                Assert.That(Get(context, gate, "STORE"), Is.EqualTo(op == 3 ? 1UL : 0UL), "STORE " + at);
                Assert.That(Get(context, gate, "OUT"), Is.EqualTo(op == 11 ? 1UL : 0UL), "OUT " + at);
                bool jump = op == 8 || (op == 9 && z == 1) || (op == 10 && c == 1);
                Assert.That(Get(context, gate, "JUMP"), Is.EqualTo(jump ? 1UL : 0UL), "JUMP " + at);
                Assert.That(Get(context, gate, "HALT"), Is.EqualTo(op == 15 ? 1UL : 0UL), "HALT " + at);
            }
        }

        [TestCase("dec2", 2)]
        [TestCase("dec4", 4)]
        public void Decoders_RaiseExactlyTheAddressedLine(string key, int bits)
        {
            SimulationContext context = Context(key, out CircuitDefinition gate);
            for (int a = 0; a < 1 << bits; a++)
            {
                Set(context, gate, "A", (ulong)a);
                Settle(context);
                for (int y = 0; y < 1 << bits; y++)
                {
                    Assert.That(Get(context, gate, "Y" + y), Is.EqualTo(y == a ? 1UL : 0UL), key + " A=" + a + " Y" + y);
                }
            }
        }

        [Test]
        public void Adders_AddEveryCombination()
        {
            SimulationContext half = Context("half-adder", out CircuitDefinition ha);
            SimulationContext full = Context("full-adder", out CircuitDefinition fa);
            for (int row = 0; row < 8; row++)
            {
                int a = row >> 2, b = (row >> 1) & 1, c = row & 1;
                if (c == 0)
                {
                    Set(half, ha, "A", (ulong)a);
                    Set(half, ha, "B", (ulong)b);
                    Settle(half);
                    Assert.That(Get(half, ha, "S") + 2 * Get(half, ha, "C"), Is.EqualTo((ulong)(a + b)));
                }

                Set(full, fa, "A", (ulong)a);
                Set(full, fa, "B", (ulong)b);
                Set(full, fa, "CIN", (ulong)c);
                Settle(full);
                Assert.That(Get(full, fa, "S") + 2 * Get(full, fa, "COUT"), Is.EqualTo((ulong)(a + b + c)));
            }

            SimulationContext adder = Context("adder4", out CircuitDefinition add);
            for (int row = 0; row < 512; row++)
            {
                int a = row >> 5, b = (row >> 1) & 15, c = row & 1;
                Set(adder, add, "A", (ulong)a);
                Set(adder, add, "B", (ulong)b);
                Set(adder, add, "CIN", (ulong)c);
                Settle(adder);
                int total = a + b + c;
                Assert.That(Get(adder, add, "S"), Is.EqualTo((ulong)(total & 15)), a + "+" + b + "+" + c);
                Assert.That(Get(adder, add, "COUT"), Is.EqualTo((ulong)(total >> 4)), a + "+" + b + "+" + c);
            }
        }

        [Test]
        public void Alu_ComputesAllFourOperations_WithCarryAndZeroFlags()
        {
            SimulationContext alu = Context("alu4", out CircuitDefinition gate);
            for (int op = 0; op < 4; op++)
            {
                for (int a = 0; a < 16; a++)
                {
                    for (int b = 0; b < 16; b++)
                    {
                        Set(alu, gate, "A", (ulong)a);
                        Set(alu, gate, "B", (ulong)b);
                        Set(alu, gate, "OP", (ulong)op);
                        Settle(alu);

                        int y;
                        int carry;
                        switch (op)
                        {
                            case 0:
                                y = (a + b) & 15;
                                carry = (a + b) >> 4;
                                break;
                            case 1:
                                y = (a - b) & 15;
                                carry = a >= b ? 1 : 0; // carry out of A + NOT B + 1: no borrow
                                break;
                            case 2:
                                y = a & b;
                                carry = 0;
                                break;
                            default:
                                y = a | b;
                                carry = 0;
                                break;
                        }

                        string row = "op " + op + " a " + a + " b " + b;
                        Assert.That(Get(alu, gate, "Y"), Is.EqualTo((ulong)y), row);
                        Assert.That(Get(alu, gate, "COUT"), Is.EqualTo((ulong)carry), row);
                        Assert.That(Get(alu, gate, "ZERO"), Is.EqualTo(y == 0 ? 1UL : 0UL), row);
                    }
                }
            }
        }

        // ------------------------------------------------------------------------------------------------
        // Sequential parts
        // ------------------------------------------------------------------------------------------------

        [Test]
        public void DLatch_IsTransparentWhileEnabled_AndHoldsOtherwise()
        {
            SimulationContext latch = Context("d-latch", out CircuitDefinition gate);
            Set(latch, gate, "E", 1);
            foreach (ulong d in new ulong[] { 1, 0, 1 })
            {
                Set(latch, gate, "D", d);
                Settle(latch);
                Assert.That(Get(latch, gate, "Q"), Is.EqualTo(d));
                Assert.That(Get(latch, gate, "QN"), Is.EqualTo(1 - d));
            }

            Set(latch, gate, "E", 0);
            Settle(latch);
            Set(latch, gate, "D", 0);
            Settle(latch);
            Assert.That(Get(latch, gate, "Q"), Is.EqualTo(1UL), "Holds while E is LOW.");
        }

        [Test]
        public void DFlipFlop_CapturesOnTheRisingEdgeOnly()
        {
            SimulationContext ff = Context("dff", out CircuitDefinition gate);
            Set(ff, gate, "CLK", 0);
            Set(ff, gate, "D", 1);
            Settle(ff);
            Assert.That(Get(ff, gate, "Q"), Is.Zero, "Powers on storing 0.");
            Assert.That(Get(ff, gate, "QN"), Is.EqualTo(1UL));

            Set(ff, gate, "CLK", 1);
            Settle(ff);
            Assert.That(Get(ff, gate, "Q"), Is.EqualTo(1UL), "Rising edge stores D.");

            Set(ff, gate, "D", 0);
            Settle(ff);
            Assert.That(Get(ff, gate, "Q"), Is.EqualTo(1UL), "D changes while CLK is HIGH are ignored.");
            Set(ff, gate, "CLK", 0);
            Settle(ff);
            Assert.That(Get(ff, gate, "Q"), Is.EqualTo(1UL), "Falling edge stores nothing.");

            Set(ff, gate, "CLK", 1);
            Settle(ff);
            Assert.That(Get(ff, gate, "Q"), Is.EqualTo(0UL));
        }

        private static void Pulse(SimulationContext context, CircuitDefinition gate)
        {
            Set(context, gate, "CLK", 1);
            Settle(context);
            Set(context, gate, "CLK", 0);
            Settle(context);
        }

        [Test]
        public void Register_LoadsOnTheEdgeWhenLoadIsHigh_AndHoldsOtherwise()
        {
            SimulationContext register = Context("register4", out CircuitDefinition gate);
            Assert.That(Get(register, gate, "Q"), Is.Zero, "Powers on at 0.");
            Set(register, gate, "CLK", 0);
            Set(register, gate, "LOAD", 1);
            Set(register, gate, "D", 0b1011);
            Settle(register);
            Pulse(register, gate);
            Assert.That(Get(register, gate, "Q"), Is.EqualTo(0b1011UL));

            Set(register, gate, "LOAD", 0);
            Set(register, gate, "D", 0b0100);
            Settle(register);
            Pulse(register, gate);
            Assert.That(Get(register, gate, "Q"), Is.EqualTo(0b1011UL), "LOAD LOW keeps the value.");

            Set(register, gate, "LOAD", 1);
            Settle(register);
            Assert.That(Get(register, gate, "Q"), Is.EqualTo(0b1011UL), "Nothing changes before the edge.");
            Pulse(register, gate);
            Assert.That(Get(register, gate, "Q"), Is.EqualTo(0b0100UL));
        }

        [Test]
        public void Counter_CountsUpOnEachRisingEdge_AndWraps()
        {
            SimulationContext counter = Context("counter4", out CircuitDefinition gate);
            Set(counter, gate, "CLK", 0);
            Settle(counter);
            Assert.That(Get(counter, gate, "Q"), Is.Zero, "Powers on at 0.");
            for (int edge = 1; edge <= 20; edge++)
            {
                Pulse(counter, gate);
                Assert.That(Get(counter, gate, "Q"), Is.EqualTo((ulong)edge & 15), "after edge " + edge);
            }
        }

        private static void Write(SimulationContext ram, CircuitDefinition gate, int address, int data)
        {
            Set(ram, gate, "ADDR", (ulong)address);
            Set(ram, gate, "DIN", (ulong)data);
            Set(ram, gate, "WE", 1);
            Settle(ram);
            Pulse(ram, gate);
            Set(ram, gate, "WE", 0);
            Settle(ram);
        }

        [Test]
        public void Ram_StoresEveryWord_AndOnlyWritesWhenEnabled()
        {
            SimulationContext ram = Context("ram16x4", out CircuitDefinition gate);
            Set(ram, gate, "CLK", 0);
            Set(ram, gate, "WE", 0);
            Settle(ram);
            for (int address = 0; address < 16; address++)
            {
                Write(ram, gate, address, (address * 7 + 3) & 15);
            }

            for (int address = 0; address < 16; address++)
            {
                Set(ram, gate, "ADDR", (ulong)address);
                Settle(ram);
                Assert.That(Get(ram, gate, "DOUT"), Is.EqualTo((ulong)((address * 7 + 3) & 15)), "word " + address);
            }

            // WE LOW: a clock pulse with new data on the bus changes nothing.
            Set(ram, gate, "ADDR", 5);
            Set(ram, gate, "DIN", 0);
            Settle(ram);
            Pulse(ram, gate);
            Assert.That(Get(ram, gate, "DOUT"), Is.EqualTo((ulong)((5 * 7 + 3) & 15)));

            // Overwriting one word leaves its neighbours alone.
            Write(ram, gate, 5, 0b1001);
            Assert.That(Get(ram, gate, "DOUT"), Is.EqualTo(0b1001UL));
            Set(ram, gate, "ADDR", 4);
            Settle(ram);
            Assert.That(Get(ram, gate, "DOUT"), Is.EqualTo((ulong)((4 * 7 + 3) & 15)));
        }

        [Test]
        public void Ram_ReadsAreLocal_AndAllocationFree()
        {
            SimulationContext ram = Context("ram16x4", out CircuitDefinition gate);
            Assert.That(ram.Circuit.GateCount, Is.InRange(900, 1300), "About a thousand AND/NOT gates.");
            Set(ram, gate, "ADDR", 0);
            Settle(ram);
            Set(ram, gate, "ADDR", 1);
            Settle(ram); // warm up

            // Gray code: one address bit changes per step.
            long before = GC.GetAllocatedBytesForCurrentThread();
            int evaluations = 0;
            for (int i = 0; i < 32; i++)
            {
                Set(ram, gate, "ADDR", (ulong)((i ^ (i >> 1)) & 15));
                evaluations = Math.Max(evaluations, ram.Settle().Evaluations);
            }

            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.EqualTo(0), "Address changes allocate nothing.");
            // Measured: 239 (low bit) to 583 (high bit) evaluations of 1 041 gates, counting re-evaluations
            // across waves. Less than one pass over the circuit: the decoder and read multiplexers react, the
            // memory cells do not.
            Assert.That(evaluations, Is.LessThan(ram.Circuit.GateCount),
                "A one-bit address change re-evaluates the decoder and read path, not every cell.");

            // With CLK LOW, raising WE reaches only the write-enable AND.
            Set(ram, gate, "CLK", 0);
            Settle(ram);
            Set(ram, gate, "WE", 1);
            Assert.That(ram.Settle().Evaluations, Is.LessThanOrEqualTo(2));
        }

        // ------------------------------------------------------------------------------------------------
        // Loading into a project
        // ------------------------------------------------------------------------------------------------

        [Test]
        public void Prepare_RefusesASecondLoad_AndRenamesClashingNames()
        {
            ExamplePackPlan again = ExampleGatePack.Prepare(_env.Catalog.Primitives, _env.Library);
            Assert.That(again.Succeeded, Is.False);
            Assert.That(again.Error, Does.Contain("already"));

            var fresh = new TestEnvironment();
            fresh.Library.Add(TestCircuits.BuildNand()); // the user's own NAND from the acceptance workflow
            ExamplePackPlan plan = ExampleGatePack.Prepare(fresh.Catalog.Primitives, fresh.Library);
            Assert.That(plan.Succeeded, Is.True, plan.Error);
            Assert.That(plan.Renamed, Is.EqualTo(new[] { "NAND" }));
            Assert.That(plan.Definitions[0].Name, Is.EqualTo("NAND" + ExampleGatePack.ClashSuffix));
        }

        [Test]
        public void Publishing_IsOneUndoStep()
        {
            var fresh = new TestEnvironment();
            ExamplePackPlan plan = ExampleGatePack.Prepare(fresh.Catalog.Primitives, fresh.Library);
            var commands = new ICommand[plan.Definitions.Count];
            for (int i = 0; i < commands.Length; i++)
            {
                commands[i] = new PublishDefinitionCommand(fresh.Library, plan.Definitions[i]);
            }

            var history = new CommandHistory();
            history.Execute(new CompositeCommand("Add example gates", commands));
            Assert.That(fresh.Library.DefinitionCount, Is.EqualTo(27));
            history.Undo();
            Assert.That(fresh.Library.DefinitionCount, Is.Zero);
            history.Redo();
            Assert.That(fresh.Library.DefinitionCount, Is.EqualTo(27));
        }

        [Test]
        public void Pack_SurvivesSaveAndOpen()
        {
            var main = new CircuitDefinition(DefinitionId.New(), "Main");
            CircuitDefinition alu = Gate("alu4");
            main.AddNode(ComponentRef.Custom(alu.Reference), CircuitPoint.Zero);
            var document = new ProjectDocument { Name = "Pack", RootCircuit = main };
            document.Definitions.AddRange(_plan.Definitions);

            string json = ProjectSerializer.ToJson(document);
            ProjectLoadResult result = new ProjectDeserializer(null, PrimitiveRegistry.CreateDefault()).Read(json);
            Assert.That(result.Document, Is.Not.Null);
            Assert.That(result.Report.HasErrors, Is.False);
            ProjectDocument loaded = result.Document;
            Assert.That(loaded.Definitions.Count, Is.EqualTo(27));
            var reloaded = new TestEnvironment();
            foreach (CircuitDefinition definition in loaded.Definitions)
            {
                Assert.That(ExampleGatePack.IsExampleId(definition.Id), Is.True);
                Assert.That(definition.Category, Does.StartWith("Examples · "));
                reloaded.Library.Add(definition);
            }

            Assert.That(reloaded.Library.TryGetLatest(alu.Id, out CircuitDefinition aluAgain), Is.True);
            SimulationContext context = SimulationTestUtil.CreateContext(reloaded, aluAgain);
            Set(context, aluAgain, "A", 9);
            Set(context, aluAgain, "B", 5);
            Set(context, aluAgain, "OP", 1);
            Settle(context);
            Assert.That(Get(context, aluAgain, "Y"), Is.EqualTo(4UL), "9 - 5 after a round trip.");
        }
    }
}
