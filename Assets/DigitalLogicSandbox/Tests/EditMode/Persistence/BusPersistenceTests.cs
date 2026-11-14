using DigitalLogicSandbox.Components.Abstractions;
using DigitalLogicSandbox.Components.BuiltIn;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.Persistence;
using DigitalLogicSandbox.Persistence.Deserialization;
using DigitalLogicSandbox.Persistence.Serialization;
using DigitalLogicSandbox.Simulation.Context;
using DigitalLogicSandbox.Tests.EditMode.Support;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static DigitalLogicSandbox.Tests.EditMode.Support.SimulationTestUtil;
using static DigitalLogicSandbox.Tests.EditMode.Support.TestCircuits;

namespace DigitalLogicSandbox.Tests.EditMode.Persistence
{
    /// <summary>Phase C: schema v3 persists bus widths, bus utilities and multi-bit input values.</summary>
    public sealed class BusPersistenceTests
    {
        private static CircuitDefinition BuildBusCircuit()
        {
            var circuit = new CircuitDefinition(DefinitionId.New(), "Bus");
            NodeDefinition a = circuit.AddInputBoundary("A", CircuitPoint.Zero, 4);
            NodeDefinition en = circuit.AddInputBoundary("EN", new CircuitPoint(0, 4));
            NodeDefinition split = circuit.AddNode(ComponentRef.Splitter(4), new CircuitPoint(2, 0));
            NodeDefinition merge = circuit.AddNode(ComponentRef.Merger(4), new CircuitPoint(6, 0));
            NodeDefinition tri = circuit.AddNode(ComponentRef.TriState(4), new CircuitPoint(8, 0));
            NodeDefinition join = circuit.AddNode(ComponentRef.BusJoin(4, 3), new CircuitPoint(10, 0));
            NodeDefinition y = circuit.AddOutputBoundary("Y", new CircuitPoint(12, 0), 4);
            circuit.Connect(BoundaryOut(a), new PinRef(split.Id, BoundarySignatures.Pin));
            for (int b = 0; b < 4; b++)
            {
                NodeDefinition not = circuit.AddNode(Not, new CircuitPoint(4, b));
                circuit.Connect(new PinRef(split.Id, BoundarySignatures.BitPin(b)), new PinRef(not.Id, NotGate.Input));
                circuit.Connect(new PinRef(not.Id, NotGate.Output), new PinRef(merge.Id, BoundarySignatures.BitPin(b)));
            }

            circuit.Connect(new PinRef(merge.Id, BoundarySignatures.Pin), new PinRef(tri.Id, BoundarySignatures.TriStateData));
            circuit.Connect(BoundaryOut(en), new PinRef(tri.Id, BoundarySignatures.TriStateEnable));
            circuit.Connect(new PinRef(tri.Id, BoundarySignatures.TriStateOutput), new PinRef(join.Id, BoundarySignatures.BitPin(1)));
            circuit.Connect(new PinRef(join.Id, BoundarySignatures.Pin), BoundaryIn(y));
            return circuit;
        }

        private static ProjectDocument RoundTrip(ProjectDocument document, out string json)
        {
            json = ProjectSerializer.ToJson(document);
            return new ProjectDeserializer(null, PrimitiveRegistry.CreateDefault()).Read(json).Document;
        }

        [Test]
        public void BusCircuit_RoundTripsWidthsKindsAndValues()
        {
            CircuitDefinition root = BuildBusCircuit();
            var document = new ProjectDocument { Name = "Bus", RootCircuit = root };
            document.InputValues[root.Inputs[0].Id] = 0b1010;
            document.InputValues[root.Inputs[1].Id] = 1;

            ProjectDocument loaded = RoundTrip(document, out string json);
            Assert.That(CircuitFingerprint.Of(loaded.RootCircuit), Is.EqualTo(CircuitFingerprint.Of(root)));
            Assert.That(loaded.RootCircuit.Inputs[0].Width, Is.EqualTo(4));
            Assert.That(loaded.RootCircuit.Outputs[0].Width, Is.EqualTo(4));
            Assert.That(loaded.InputValues[root.Inputs[0].Id], Is.EqualTo(10UL));
            Assert.That(loaded.InputValues[root.Inputs[1].Id], Is.EqualTo(1UL));

            var node = (JObject)((JArray)JObject.Parse(json)["rootCircuit"]["nodes"])[5];
            Assert.That((string)node["kind"], Is.EqualTo("busjoin"));
            Assert.That((int)node["width"], Is.EqualTo(4));
            Assert.That((int)node["arity"], Is.EqualTo(3));

            // The loaded circuit simulates like the original: EN=1 drives NOT(A) onto the bus.
            var env = new TestEnvironment();
            SimulationContext context = CreateContext(env, loaded.RootCircuit);
            context.SetInputValue(root.Inputs[0].Id, loaded.InputValues[root.Inputs[0].Id]);
            context.SetInputValue(root.Inputs[1].Id, loaded.InputValues[root.Inputs[1].Id]);
            context.Settle();
            Assert.That(context.TryGetOutputValue(root.Outputs[0].Id, out ulong value), Is.True);
            Assert.That(value, Is.EqualTo(0b0101UL));
        }

        [Test]
        public void SingleBitNodes_OmitWidth_AndValuesStayHighLow()
        {
            CircuitDefinition root = BuildNand("Main");
            var document = new ProjectDocument { Name = "Nand", RootCircuit = root };
            document.InputValues[root.Inputs[0].Id] = 1;
            string json = ProjectSerializer.ToJson(document);
            JObject parsed = JObject.Parse(json);

            foreach (JToken node in (JArray)parsed["rootCircuit"]["nodes"])
            {
                Assert.That(node["width"], Is.Null);
            }

            Assert.That((string)parsed["inputValues"][0]["value"], Is.EqualTo("High"));
            Assert.That((int)parsed["schemaVersion"], Is.EqualTo(3));
        }

        [Test]
        public void Version2File_MigratesToVersion3()
        {
            CircuitDefinition root = BuildNand("Main");
            var document = new ProjectDocument { Name = "Old", RootCircuit = root };
            JObject json = JObject.Parse(ProjectSerializer.ToJson(document));
            json["schemaVersion"] = 2;

            var result = new ProjectDeserializer(null, PrimitiveRegistry.CreateDefault()).Read(json.ToString());
            Assert.That(result.StoredSchemaVersion, Is.EqualTo(2));
            Assert.That(CircuitFingerprint.Of(result.Document.RootCircuit), Is.EqualTo(CircuitFingerprint.Of(root)));
        }
    }
}
