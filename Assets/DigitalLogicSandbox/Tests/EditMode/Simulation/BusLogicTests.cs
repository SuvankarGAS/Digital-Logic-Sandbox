using DigitalLogicSandbox.Components.BuiltIn;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.Core.Signals;
using DigitalLogicSandbox.Core.Validation;
using DigitalLogicSandbox.Simulation.Context;
using DigitalLogicSandbox.Simulation.Engine;
using DigitalLogicSandbox.Tests.EditMode.Support;
using DigitalLogicSandbox.TruthTable.Evaluation;
using DigitalLogicSandbox.TruthTable.Generation;
using NUnit.Framework;
using static DigitalLogicSandbox.Tests.EditMode.Support.SimulationTestUtil;
using static DigitalLogicSandbox.Tests.EditMode.Support.TestCircuits;

namespace DigitalLogicSandbox.Tests.EditMode.Simulation
{
    /// <summary>
    /// Phase C: multi-bit boundaries, splitters, mergers, bit-sliced gates, shared buses with tri-state drivers
    /// and bus contention.
    /// </summary>
    public sealed class BusLogicTests
    {
        private TestEnvironment _env;

        [SetUp]
        public void SetUp()
        {
            _env = new TestEnvironment();
        }

        private static bool HasCode(ValidationReport report, DiagnosticCode code)
        {
            foreach (Diagnostic diagnostic in report.Diagnostics)
            {
                if (diagnostic.Code == code)
                {
                    return true;
                }
            }

            return false;
        }

        private static ulong Output(SimulationContext context, PortId port)
        {
            Assert.That(context.TryGetOutputValue(port, out ulong value), Is.True, "Output is not binary.");
            return value;
        }

        private static PinRef Pin(NodeDefinition node, int bit)
        {
            return new PinRef(node.Id, BoundarySignatures.BitPin(bit));
        }

        private static PinRef BusPin(NodeDefinition node)
        {
            return new PinRef(node.Id, BoundarySignatures.Pin);
        }

        /// <summary>IN[4] -> splitter -> NOT per bit -> merger -> OUT[4]: a 4-bit inverter.</summary>
        private static CircuitDefinition BuildBusInverter(out NodeDefinition input, out NodeDefinition output)
        {
            var circuit = new CircuitDefinition(DefinitionId.New(), "INV4");
            input = circuit.AddInputBoundary("A", CircuitPoint.Zero, 4);
            output = circuit.AddOutputBoundary("Y", new CircuitPoint(10, 0), 4);
            NodeDefinition split = circuit.AddNode(ComponentRef.Splitter(4), new CircuitPoint(2, 0));
            NodeDefinition merge = circuit.AddNode(ComponentRef.Merger(4), new CircuitPoint(8, 0));
            circuit.Connect(BoundaryOut(input), BusPin(split));
            for (int b = 0; b < 4; b++)
            {
                NodeDefinition not = circuit.AddNode(Not, new CircuitPoint(5, b));
                circuit.Connect(Pin(split, b), new PinRef(not.Id, NotGate.Input));
                circuit.Connect(new PinRef(not.Id, NotGate.Output), Pin(merge, b));
            }

            circuit.Connect(BusPin(merge), BoundaryIn(output));
            return circuit;
        }

        [Test]
        public void BusBoundariesFlattenInterfaceToBits()
        {
            CircuitDefinition circuit = BuildBusInverter(out _, out _);
            CompiledCircuit compiled = Compile(_env, circuit);

            Assert.That(circuit.Inputs[0].Width, Is.EqualTo(4));
            Assert.That(compiled.InputCount, Is.EqualTo(4));
            Assert.That(compiled.OutputCount, Is.EqualTo(4));
            PortId port = circuit.Inputs[0].Id;
            for (int b = 0; b < 4; b++)
            {
                Assert.That(compiled.GetInputPort(b), Is.EqualTo(port));
                Assert.That(compiled.GetInputBit(b), Is.EqualTo(b));
                Assert.That(compiled.IndexOfInput(port, b), Is.EqualTo(b));
            }

            Assert.That(compiled.GetInputWidth(port), Is.EqualTo(4));
            Assert.That(compiled.IndexOfInput(port, 4), Is.EqualTo(-1));
        }

        [Test]
        public void SplitterAndMergerCompileAwayAndInvertEveryBit()
        {
            CircuitDefinition circuit = BuildBusInverter(out _, out _);
            SimulationContext context = CreateContext(_env, circuit);
            Assert.That(context.Circuit.GateCount, Is.EqualTo(4), "Only the four NOT gates remain.");

            PortId a = circuit.Inputs[0].Id;
            PortId y = circuit.Outputs[0].Id;
            for (ulong value = 0; value < 16; value++)
            {
                context.SetInputValue(a, value);
                Assert.That(context.Settle().IsStable, Is.True);
                Assert.That(Output(context, y), Is.EqualTo(~value & 0xF), "A=" + value);
            }
        }

        [Test]
        public void PrimitiveOnBusPinsIsBitSliced()
        {
            // Bus boundaries cannot feed a 1-bit gate directly (width mismatch), so slicing applies to utilities
            // with bus pins: a 3-bit tri-state driver becomes three per-bit gates sharing one enable.
            var circuit = new CircuitDefinition(DefinitionId.New(), "TRI3");
            NodeDefinition data = circuit.AddInputBoundary("D", CircuitPoint.Zero, 3);
            NodeDefinition enable = circuit.AddInputBoundary("EN", new CircuitPoint(0, 3));
            NodeDefinition tri = circuit.AddNode(ComponentRef.TriState(3), new CircuitPoint(4, 0));
            NodeDefinition output = circuit.AddOutputBoundary("Y", new CircuitPoint(8, 0), 3);
            circuit.Connect(BoundaryOut(data), new PinRef(tri.Id, BoundarySignatures.TriStateData));
            circuit.Connect(BoundaryOut(enable), new PinRef(tri.Id, BoundarySignatures.TriStateEnable));
            circuit.Connect(new PinRef(tri.Id, BoundarySignatures.TriStateOutput), BoundaryIn(output));

            SimulationContext context = CreateContext(_env, circuit);
            Assert.That(context.Circuit.GateCount, Is.EqualTo(3));

            context.SetInputValue(circuit.Inputs[0].Id, 5);
            context.SetInputValue(circuit.Inputs[1].Id, 0);
            context.Settle();
            for (int b = 0; b < 3; b++)
            {
                Assert.That(context.GetOutput(b), Is.EqualTo(Signal.HighImpedance));
            }

            context.SetInputValue(circuit.Inputs[1].Id, 1);
            context.Settle();
            Assert.That(Output(context, circuit.Outputs[0].Id), Is.EqualTo(5UL));
        }

        [Test]
        public void TriStateBufferTruthTable()
        {
            var outputs = new Signal[1];
            void Eval(Signal d, Signal en)
            {
                TriStateBuffer.Instance.Evaluate(new[] { d, en }, outputs);
            }

            Eval(Signal.High, Signal.Low);
            Assert.That(outputs[0], Is.EqualTo(Signal.HighImpedance));
            Eval(Signal.High, Signal.High);
            Assert.That(outputs[0], Is.EqualTo(Signal.High));
            Eval(Signal.Low, Signal.High);
            Assert.That(outputs[0], Is.EqualTo(Signal.Low));
            Eval(Signal.HighImpedance, Signal.High);
            Assert.That(outputs[0], Is.EqualTo(Signal.Unknown));
            Eval(Signal.High, Signal.Unknown);
            Assert.That(outputs[0], Is.EqualTo(Signal.Unknown));
        }

        /// <summary>Two 2-bit tri-state drivers on one bus join; returns the circuit (inputs A, B, EA, EB; output BUS).</summary>
        private static CircuitDefinition BuildSharedBus(int drivers = 2)
        {
            var circuit = new CircuitDefinition(DefinitionId.New(), "SHARED");
            NodeDefinition join = circuit.AddNode(ComponentRef.BusJoin(2, drivers), new CircuitPoint(8, 0));
            NodeDefinition output = circuit.AddOutputBoundary("BUS", new CircuitPoint(12, 0), 2);
            for (int d = 0; d < 2; d++)
            {
                NodeDefinition data = circuit.AddInputBoundary(d == 0 ? "A" : "B", new CircuitPoint(0, d * 4), 2);
                NodeDefinition tri = circuit.AddNode(ComponentRef.TriState(2), new CircuitPoint(4, d * 4));
                circuit.Connect(BoundaryOut(data), new PinRef(tri.Id, BoundarySignatures.TriStateData));
                circuit.Connect(new PinRef(tri.Id, BoundarySignatures.TriStateOutput), Pin(join, d));
            }

            for (int d = 0; d < 2; d++)
            {
                NodeDefinition enable = circuit.AddInputBoundary(d == 0 ? "EA" : "EB", new CircuitPoint(0, 10 + d));
                NodeDefinition tri = circuit.Nodes[3 + d * 2];
                Assert.That(tri.Component.Kind, Is.EqualTo(ComponentKind.TriState));
                circuit.Connect(BoundaryOut(enable), new PinRef(tri.Id, BoundarySignatures.TriStateEnable));
            }

            circuit.Connect(BusPin(join), BoundaryIn(output));
            return circuit;
        }

        [Test]
        public void SharedBusCarriesTheEnabledDriver()
        {
            CircuitDefinition circuit = BuildSharedBus();
            SimulationContext context = CreateContext(_env, circuit);
            Assert.That(context.Circuit.HasSharedNets, Is.True);
            PortId a = circuit.Inputs[0].Id, b = circuit.Inputs[1].Id, ea = circuit.Inputs[2].Id, eb = circuit.Inputs[3].Id;
            PortId bus = circuit.Outputs[0].Id;
            context.SetInputValue(a, 1);
            context.SetInputValue(b, 2);
            context.Settle();

            Assert.That(context.GetOutput(0), Is.EqualTo(Signal.HighImpedance), "No driver enabled: floating bus.");

            context.SetInputValue(ea, 1);
            context.Settle();
            Assert.That(Output(context, bus), Is.EqualTo(1UL));

            context.SetInputValue(ea, 0);
            context.SetInputValue(eb, 1);
            context.Settle();
            Assert.That(Output(context, bus), Is.EqualTo(2UL));
            Assert.That(context.ContentionCount, Is.EqualTo(0));
        }

        [Test]
        public void ConflictingDriversAreContendedAndUnknown()
        {
            CircuitDefinition circuit = BuildSharedBus();
            SimulationContext context = CreateContext(_env, circuit);
            context.SetInputValue(circuit.Inputs[0].Id, 1); // 01
            context.SetInputValue(circuit.Inputs[1].Id, 3); // 11: bit 0 agrees, bit 1 conflicts
            context.SetInputValue(circuit.Inputs[2].Id, 1);
            context.SetInputValue(circuit.Inputs[3].Id, 1);
            context.Settle();

            Assert.That(context.GetOutput(0), Is.EqualTo(Signal.High), "Agreeing drivers are fine.");
            Assert.That(context.GetOutput(1), Is.EqualTo(Signal.Unknown));
            Assert.That(context.ContentionCount, Is.EqualTo(1));
            Assert.That(context.IsContended(context.Circuit.GetOutputNet(1)), Is.True);

            var report = new ValidationReport();
            DigitalLogicSandbox.Simulation.Diagnostics.SimulationDiagnostics.ReportLastSettle(context, report);
            Assert.That(HasCode(report, DiagnosticCode.BusContention), Is.True);

            context.SetInputValue(circuit.Inputs[3].Id, 0);
            context.Settle();
            Assert.That(context.ContentionCount, Is.EqualTo(0));
            Assert.That(Output(context, circuit.Outputs[0].Id), Is.EqualTo(1UL));

            context.Reset();
            Assert.That(context.ContentionCount, Is.EqualTo(0));
        }

        [Test]
        public void UnconnectedBusJoinInputContributesNothing()
        {
            // Three-input join with only two drivers wired: the free input must not pull the bus LOW.
            CircuitDefinition circuit = BuildSharedBus(3);
            SimulationContext context = CreateContext(_env, circuit);
            context.SetInputValue(circuit.Inputs[0].Id, 3);
            context.SetInputValue(circuit.Inputs[2].Id, 1);
            context.Settle();
            Assert.That(Output(context, circuit.Outputs[0].Id), Is.EqualTo(3UL));
        }

        [Test]
        public void JoiningACircuitInputToAGateIsRejected()
        {
            var circuit = new CircuitDefinition(DefinitionId.New(), "BAD");
            NodeDefinition a = circuit.AddInputBoundary("A", CircuitPoint.Zero);
            NodeDefinition b = circuit.AddInputBoundary("B", new CircuitPoint(0, 2));
            NodeDefinition not = circuit.AddNode(Not, new CircuitPoint(3, 2));
            NodeDefinition join = circuit.AddNode(ComponentRef.BusJoin(1, 2), new CircuitPoint(6, 0));
            NodeDefinition output = circuit.AddOutputBoundary("Y", new CircuitPoint(9, 0));
            circuit.Connect(BoundaryOut(b), new PinRef(not.Id, NotGate.Input));
            circuit.Connect(BoundaryOut(a), Pin(join, 0));
            circuit.Connect(new PinRef(not.Id, NotGate.Output), Pin(join, 1));
            circuit.Connect(BusPin(join), BoundaryIn(output));

            CompileResult result = new CircuitCompiler(_env.Catalog).Compile(circuit);
            Assert.That(result.Succeeded, Is.False);
            Assert.That(HasCode(result.Report, DiagnosticCode.InvalidBusJoin), Is.True);
        }

        [Test]
        public void WidthMismatchIsRejectedByRulesAndValidation()
        {
            var circuit = new CircuitDefinition(DefinitionId.New(), "MISMATCH");
            NodeDefinition bus = circuit.AddInputBoundary("A", CircuitPoint.Zero, 4);
            NodeDefinition not = circuit.AddNode(Not, new CircuitPoint(3, 0));
            NodeDefinition wide = circuit.AddOutputBoundary("Y", new CircuitPoint(6, 0), 8);

            Assert.That(ConnectionRules.Check(circuit, _env.Catalog, BoundaryOut(bus), new PinRef(not.Id, NotGate.Input)),
                Is.EqualTo(ConnectionError.WidthMismatch));
            Assert.That(ConnectionRules.Check(circuit, _env.Catalog, BoundaryOut(bus), BoundaryIn(wide)),
                Is.EqualTo(ConnectionError.WidthMismatch));

            // Structural connect bypasses the rules; validation still reports it.
            circuit.Connect(BoundaryOut(bus), BoundaryIn(wide));
            ValidationReport report = new CircuitValidator(_env.Catalog).Validate(circuit);
            Assert.That(HasCode(report, DiagnosticCode.WidthMismatch), Is.True);
        }

        [Test]
        public void BusPortsWorkThroughCustomGates()
        {
            CircuitDefinition inverter = BuildBusInverter(out _, out _);
            DefinitionRef reference = _env.Publish(inverter);
            Assert.That(_env.Catalog.TryGetSignature(ComponentRef.Custom(reference), out ComponentSignature signature), Is.True);
            Assert.That(signature.Inputs[0].Width, Is.EqualTo(4));
            Assert.That(signature.Outputs[0].Width, Is.EqualTo(4));

            // Two inverters in series give the identity.
            var outer = new CircuitDefinition(DefinitionId.New(), "TWICE");
            NodeDefinition input = outer.AddInputBoundary("A", CircuitPoint.Zero, 4);
            NodeDefinition first = outer.AddNode(ComponentRef.Custom(reference), new CircuitPoint(3, 0));
            NodeDefinition second = outer.AddNode(ComponentRef.Custom(reference), new CircuitPoint(6, 0));
            NodeDefinition output = outer.AddOutputBoundary("Y", new CircuitPoint(9, 0), 4);
            PortId inPort = inverter.Inputs[0].Id, outPort = inverter.Outputs[0].Id;
            Assert.That(ConnectionRules.Check(outer, _env.Catalog, BoundaryOut(input), new PinRef(first.Id, inPort)),
                Is.EqualTo(ConnectionError.None));
            outer.Connect(BoundaryOut(input), new PinRef(first.Id, inPort));
            outer.Connect(new PinRef(first.Id, outPort), new PinRef(second.Id, inPort));
            outer.Connect(new PinRef(second.Id, outPort), BoundaryIn(output));

            SimulationContext context = CreateContext(_env, outer);
            for (ulong value = 0; value < 16; value++)
            {
                context.SetInputValue(outer.Inputs[0].Id, value);
                context.Settle();
                Assert.That(Output(context, outer.Outputs[0].Id), Is.EqualTo(value));
            }
        }

        [Test]
        public void RunnerCarriesBusInputsOverPerBit()
        {
            CircuitDefinition circuit = BuildBusInverter(out _, out _);
            var runner = new SimulationRunner();
            runner.Load(Compile(_env, circuit));
            runner.Play();
            runner.SetInputValue(circuit.Inputs[0].Id, 0b1010);

            runner.Load(Compile(_env, circuit));
            Assert.That(runner.Context.TryGetInputValue(circuit.Inputs[0].Id, out ulong value), Is.True);
            Assert.That(value, Is.EqualTo(0b1010UL));
        }

        [Test]
        public void TruthTableHasOneColumnPerBit_MostSignificantFirst()
        {
            // 2-bit inverter: Y = NOT A, per bit.
            var circuit = new CircuitDefinition(DefinitionId.New(), "INV2");
            NodeDefinition input = circuit.AddInputBoundary("A", CircuitPoint.Zero, 2);
            NodeDefinition output = circuit.AddOutputBoundary("Y", new CircuitPoint(10, 0), 2);
            NodeDefinition split = circuit.AddNode(ComponentRef.Splitter(2), new CircuitPoint(2, 0));
            NodeDefinition merge = circuit.AddNode(ComponentRef.Merger(2), new CircuitPoint(8, 0));
            circuit.Connect(BoundaryOut(input), BusPin(split));
            for (int b = 0; b < 2; b++)
            {
                NodeDefinition not = circuit.AddNode(Not, new CircuitPoint(5, b));
                circuit.Connect(Pin(split, b), new PinRef(not.Id, NotGate.Input));
                circuit.Connect(new PinRef(not.Id, NotGate.Output), Pin(merge, b));
            }

            circuit.Connect(BusPin(merge), BoundaryIn(output));
            TruthTableJob job = new TruthTableGenerator(_env.Catalog).Generate(circuit);
            TruthTableData table = job.Table;
            Assert.That(table.InputCount, Is.EqualTo(2));
            Assert.That(table.RowCount, Is.EqualTo(4));
            Assert.That(table.Inputs[0].Name, Is.EqualTo("A[1]"));
            Assert.That(table.Inputs[1].Name, Is.EqualTo("A[0]"));
            Assert.That(table.Outputs[0].Name, Is.EqualTo("Y[1]"));
            for (int row = 0; row < 4; row++)
            {
                // Row index = A (first column is the MSB), and Y = NOT A.
                int y = ~row & 3;
                Assert.That(table.GetOutput(row, 0), Is.EqualTo((y & 2) != 0 ? Signal.High : Signal.Low), "row " + row);
                Assert.That(table.GetOutput(row, 1), Is.EqualTo((y & 1) != 0 ? Signal.High : Signal.Low), "row " + row);
            }
        }

        [Test]
        public void WidthOneCircuitsAreUnchanged()
        {
            CircuitDefinition nand = BuildNand();
            CompiledCircuit compiled = Compile(_env, nand);
            Assert.That(compiled.InputCount, Is.EqualTo(2));
            Assert.That(compiled.HasSharedNets, Is.False);
            Assert.That(compiled.IndexOfInput(nand.Inputs[1].Id), Is.EqualTo(1));
            AssertTruthTable(new SimulationContext(compiled), 1, 1, 1, 0);
        }
    }
}
