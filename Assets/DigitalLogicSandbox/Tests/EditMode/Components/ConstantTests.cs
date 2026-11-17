using System;
using DigitalLogicSandbox.Components.Abstractions;
using DigitalLogicSandbox.Components.BuiltIn;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.Core.Signals;
using DigitalLogicSandbox.Persistence;
using DigitalLogicSandbox.Persistence.Deserialization;
using DigitalLogicSandbox.Persistence.Serialization;
using DigitalLogicSandbox.Rendering;
using DigitalLogicSandbox.Simulation.Context;
using DigitalLogicSandbox.Tests.EditMode.Support;
using DigitalLogicSandbox.TruthTable.Evaluation;
using DigitalLogicSandbox.TruthTable.Generation;
using DigitalLogicSandbox.UI.Inspector;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace DigitalLogicSandbox.Tests.EditMode.Components
{
    /// <summary>Phase E+: the Constant source utility (a fixed value built from AND/NOT gates by the compiler).</summary>
    public sealed class ConstantTests
    {
        private TestEnvironment _env;

        [SetUp]
        public void SetUp()
        {
            _env = new TestEnvironment();
        }

        private static PinRef Out(NodeDefinition node) => new PinRef(node.Id, BoundarySignatures.Pin);

        [Test]
        public void Reference_CarriesWidthAndValue_MaskedToTheWidth()
        {
            ComponentRef constant = ComponentRef.Constant(4, 0x1F);
            Assert.That(constant.Kind, Is.EqualTo(ComponentKind.Constant));
            Assert.That(constant.Width, Is.EqualTo(4));
            Assert.That(constant.ConstantValue, Is.EqualTo(0xF), "Bits above the width are dropped.");
            Assert.That(constant.IsUtility, Is.True, "A source utility, not a logic gate.");
            Assert.That(constant.WithConstantValue(6), Is.EqualTo(ComponentRef.Constant(4, 6)));
            Assert.That(ComponentRef.Constant(8, 0xA5), Is.Not.EqualTo(ComponentRef.Constant(8, 0xA4)));
            Assert.Throws<ArgumentOutOfRangeException>(() => ComponentRef.Constant(9, 0));
            Assert.Throws<InvalidOperationException>(() => ComponentRef.Clock.WithConstantValue(1));
            Assert.That(ComponentRef.Utility(ComponentKind.Constant, 40, 0x1FF), Is.EqualTo(ComponentRef.Constant(8, 0xFF)),
                "Damaged files are clamped, not rejected.");

            Assert.That(CircuitGeometry.ConstantText(ComponentRef.Constant(1, 1)), Is.EqualTo("1"));
            Assert.That(CircuitGeometry.ConstantText(ComponentRef.Constant(4, 5)), Is.EqualTo("0101"));
            Assert.That(CircuitGeometry.ConstantText(ComponentRef.Constant(8, 0x1F)), Is.EqualTo("0x1F"));
        }

        [Test]
        public void Compiled_DrivesItsBits_FromAndNotGates_AndSurvivesReset()
        {
            var circuit = new CircuitDefinition(DefinitionId.New(), "Const");
            NodeDefinition constant = circuit.AddNode(ComponentRef.Constant(8, 0xA5), CircuitPoint.Zero);
            NodeDefinition output = circuit.AddOutputBoundary("Y", new CircuitPoint(5, 0), 8);
            circuit.Connect(Out(constant), new PinRef(output.Id, BoundarySignatures.Pin));

            SimulationContext context = SimulationTestUtil.CreateContext(_env, circuit);
            Assert.That(context.Circuit.GateCount, Is.EqualTo(8), "One AND or NOT gate per bit.");
            for (int g = 0; g < context.Circuit.GateCount; g++)
            {
                string id = context.Circuit.GateComponents[g].Id;
                Assert.That(id, Is.EqualTo(BuiltInComponentIds.And).Or.EqualTo(BuiltInComponentIds.Not));
            }

            Assert.That(context.TryGetOutputValue(output.InterfacePort, out ulong value), Is.True);
            Assert.That(value, Is.EqualTo(0xA5UL));
            context.Reset();
            Assert.That(context.TryGetOutputValue(output.InterfacePort, out value), Is.True);
            Assert.That(value, Is.EqualTo(0xA5UL), "Reset (power-on) keeps the value: it is wiring, not state.");
        }

        [Test]
        public void TruthTable_TreatsAConstantAsFixedLogic()
        {
            // Y = A AND 1, Z = A AND 0: two outputs, one input column.
            var circuit = new CircuitDefinition(DefinitionId.New(), "Masks");
            NodeDefinition a = circuit.AddInputBoundary("A", new CircuitPoint(-5, 0));
            NodeDefinition one = circuit.AddNode(ComponentRef.Constant(1, 1), new CircuitPoint(-5, 2));
            NodeDefinition zero = circuit.AddNode(ComponentRef.Constant(1, 0), new CircuitPoint(-5, -2));
            NodeDefinition andOne = circuit.AddNode(ComponentRef.Primitive(BuiltInComponentIds.And), new CircuitPoint(0, 1));
            NodeDefinition andZero = circuit.AddNode(ComponentRef.Primitive(BuiltInComponentIds.And), new CircuitPoint(0, -1));
            NodeDefinition y = circuit.AddOutputBoundary("Y", new CircuitPoint(5, 1));
            NodeDefinition z = circuit.AddOutputBoundary("Z", new CircuitPoint(5, -1));
            circuit.Connect(Out(a), new PinRef(andOne.Id, AndGate.InputA));
            circuit.Connect(Out(one), new PinRef(andOne.Id, AndGate.InputB));
            circuit.Connect(Out(a), new PinRef(andZero.Id, AndGate.InputA));
            circuit.Connect(Out(zero), new PinRef(andZero.Id, AndGate.InputB));
            circuit.Connect(new PinRef(andOne.Id, AndGate.Output), new PinRef(y.Id, BoundarySignatures.Pin));
            circuit.Connect(new PinRef(andZero.Id, AndGate.Output), new PinRef(z.Id, BoundarySignatures.Pin));

            TruthTableData table = new TruthTableGenerator(_env.Catalog).Generate(circuit).Table;
            Assert.That(table.InputCount, Is.EqualTo(1), "A constant is not an input column.");
            Assert.That(table.RowCount, Is.EqualTo(2));
            for (int row = 0; row < 2; row++)
            {
                Assert.That(table.GetOutput(row, 0), Is.EqualTo(row == 1 ? Signal.High : Signal.Low), "Y row " + row);
                Assert.That(table.GetOutput(row, 1), Is.EqualTo(Signal.Low), "Z row " + row);
            }
        }

        [Test]
        public void TwoConstantsOnOneBus_AreReportedAsContention()
        {
            var circuit = new CircuitDefinition(DefinitionId.New(), "Clash");
            NodeDefinition one = circuit.AddNode(ComponentRef.Constant(1, 1), new CircuitPoint(-5, 1));
            NodeDefinition zero = circuit.AddNode(ComponentRef.Constant(1, 0), new CircuitPoint(-5, -1));
            NodeDefinition join = circuit.AddNode(ComponentRef.BusJoin(1, 2), CircuitPoint.Zero);
            NodeDefinition y = circuit.AddOutputBoundary("Y", new CircuitPoint(5, 0));
            circuit.Connect(Out(one), new PinRef(join.Id, BoundarySignatures.BitPin(0)));
            circuit.Connect(Out(zero), new PinRef(join.Id, BoundarySignatures.BitPin(1)));
            circuit.Connect(new PinRef(join.Id, BoundarySignatures.Pin), new PinRef(y.Id, BoundarySignatures.Pin));

            SimulationContext context = SimulationTestUtil.CreateContext(_env, circuit);
            Assert.That(context.ContentionCount, Is.GreaterThan(0), "Both bits are real drivers.");
        }

        [Test]
        public void Persistence_RoundTripsValueWidthAndLabel_AndUpgradesVersion6Files()
        {
            var circuit = new CircuitDefinition(DefinitionId.New(), "Program");
            NodeDefinition word = circuit.AddNode(ComponentRef.Constant(8, 0x1D), new CircuitPoint(2, 3));
            circuit.SetNodeLabel(word.Id, "0: LDI 13");
            var document = new ProjectDocument { Name = "Constants", RootCircuit = circuit };

            string json = ProjectSerializer.ToJson(document);
            Assert.That(json, Does.Contain("\"kind\": \"constant\""));
            Assert.That(json, Does.Contain("\"value\": 29"));
            ProjectLoadResult result = new ProjectDeserializer(null, PrimitiveRegistry.CreateDefault()).Read(json);
            Assert.That(result.Report.HasErrors, Is.False, result.Report.ToString());
            NodeDefinition loaded = result.Document.RootCircuit.GetNode(word.Id);
            Assert.That(loaded.Component, Is.EqualTo(ComponentRef.Constant(8, 0x1D)));
            Assert.That(loaded.Label, Is.EqualTo("0: LDI 13"));

            // A version 6 file (no constants yet) loads through the no-op v6 -> v7 step.
            JObject old = JObject.Parse(ProjectSerializer.ToJson(new ProjectDocument
            {
                Name = "Old", RootCircuit = new CircuitDefinition(DefinitionId.New(), "Main"),
            }));
            old["schemaVersion"] = 6;
            ProjectLoadResult migrated = new ProjectDeserializer(null, PrimitiveRegistry.CreateDefault()).Read(old.ToString());
            Assert.That(migrated.Report.HasErrors, Is.False, migrated.Report.ToString());
            Assert.That(migrated.AppliedMigrations[0], Does.Contain("v6 -> v7"));
        }

        [TestCase("29", 29)]
        [TestCase("0x1D", 29)]
        [TestCase("0b0001_1101", 29)]
        [TestCase(" 0B11 ", 3)]
        public void Inspector_ParsesDecimalHexAndBinary(string text, int expected)
        {
            Assert.That(InspectorPanel.TryParseConstant(text, out int value), Is.True);
            Assert.That(value, Is.EqualTo(expected));
        }

        [TestCase("")]
        [TestCase("0x")]
        [TestCase("0b102")]
        [TestCase("-1")]
        [TestCase("ten")]
        public void Inspector_RejectsMalformedValues(string text)
        {
            Assert.That(InspectorPanel.TryParseConstant(text, out _), Is.False);
        }
    }
}
