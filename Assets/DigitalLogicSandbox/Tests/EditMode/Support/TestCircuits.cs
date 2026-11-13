using DigitalLogicSandbox.Components.Abstractions;
using DigitalLogicSandbox.Components.BuiltIn;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.GateLibrary.Custom;

namespace DigitalLogicSandbox.Tests.EditMode.Support
{
    /// <summary>A library + catalog pair, as the application wires them.</summary>
    internal sealed class TestEnvironment
    {
        public TestEnvironment()
        {
            Library = new DefinitionLibrary();
            Catalog = new ComponentCatalog(PrimitiveRegistry.CreateDefault(), Library);
        }

        public DefinitionLibrary Library { get; }

        public ComponentCatalog Catalog { get; }

        /// <summary>Publishes a definition and returns the reference instances should use.</summary>
        public DefinitionRef Publish(CircuitDefinition definition)
        {
            Library.Add(definition);
            return definition.Reference;
        }
    }

    /// <summary>
    /// Builders for reference circuits used across tests. Every gate here is built from AND/NOT or from other
    /// packaged definitions — nothing is hardcoded by name.
    /// </summary>
    internal static class TestCircuits
    {
        public static ComponentRef And => ComponentRef.Primitive(BuiltInComponentIds.And);

        public static ComponentRef Not => ComponentRef.Primitive(BuiltInComponentIds.Not);

        public static PinRef Out(NodeDefinition boundaryOrGate, PortId port)
        {
            return new PinRef(boundaryOrGate.Id, port);
        }

        /// <summary>Output pin of an input boundary node.</summary>
        public static PinRef BoundaryOut(NodeDefinition inputBoundary)
        {
            return new PinRef(inputBoundary.Id, BoundarySignatures.Pin);
        }

        /// <summary>Input pin of an output boundary node.</summary>
        public static PinRef BoundaryIn(NodeDefinition outputBoundary)
        {
            return new PinRef(outputBoundary.Id, BoundarySignatures.Pin);
        }

        /// <summary>NAND(A, B) = NOT(AND(A, B)).</summary>
        public static CircuitDefinition BuildNand(string name = "NAND")
        {
            var circuit = new CircuitDefinition(DefinitionId.New(), name);
            NodeDefinition a = circuit.AddInputBoundary("A", new CircuitPoint(0, 1));
            NodeDefinition b = circuit.AddInputBoundary("B", new CircuitPoint(0, -1));
            NodeDefinition and = circuit.AddNode(And, new CircuitPoint(3, 0));
            NodeDefinition not = circuit.AddNode(Not, new CircuitPoint(6, 0));
            NodeDefinition output = circuit.AddOutputBoundary("OUT", new CircuitPoint(9, 0));

            circuit.Connect(BoundaryOut(a), new PinRef(and.Id, AndGate.InputA));
            circuit.Connect(BoundaryOut(b), new PinRef(and.Id, AndGate.InputB));
            circuit.Connect(new PinRef(and.Id, AndGate.Output), new PinRef(not.Id, NotGate.Input));
            circuit.Connect(new PinRef(not.Id, NotGate.Output), BoundaryIn(output));
            return circuit;
        }

        /// <summary>OR(A, B) = NOT(AND(NOT(A), NOT(B))) — three NOTs and one AND.</summary>
        public static CircuitDefinition BuildOrFromPrimitives(string name = "OR")
        {
            var circuit = new CircuitDefinition(DefinitionId.New(), name);
            NodeDefinition a = circuit.AddInputBoundary("A", new CircuitPoint(0, 1));
            NodeDefinition b = circuit.AddInputBoundary("B", new CircuitPoint(0, -1));
            NodeDefinition notA = circuit.AddNode(Not, new CircuitPoint(3, 1));
            NodeDefinition notB = circuit.AddNode(Not, new CircuitPoint(3, -1));
            NodeDefinition and = circuit.AddNode(And, new CircuitPoint(6, 0));
            NodeDefinition notOut = circuit.AddNode(Not, new CircuitPoint(9, 0));
            NodeDefinition output = circuit.AddOutputBoundary("OUT", new CircuitPoint(12, 0));

            circuit.Connect(BoundaryOut(a), new PinRef(notA.Id, NotGate.Input));
            circuit.Connect(BoundaryOut(b), new PinRef(notB.Id, NotGate.Input));
            circuit.Connect(new PinRef(notA.Id, NotGate.Output), new PinRef(and.Id, AndGate.InputA));
            circuit.Connect(new PinRef(notB.Id, NotGate.Output), new PinRef(and.Id, AndGate.InputB));
            circuit.Connect(new PinRef(and.Id, AndGate.Output), new PinRef(notOut.Id, NotGate.Input));
            circuit.Connect(new PinRef(notOut.Id, NotGate.Output), BoundaryIn(output));
            return circuit;
        }

        /// <summary>OR(A, B) = NAND(NAND(A, A), NAND(B, B)) built only from a packaged NAND.</summary>
        public static CircuitDefinition BuildOrFromNand(DefinitionRef nand, CircuitDefinition nandDefinition,
            string name = "OR")
        {
            PortId nandA = nandDefinition.Inputs[0].Id;
            PortId nandB = nandDefinition.Inputs[1].Id;
            PortId nandOut = nandDefinition.Outputs[0].Id;

            var circuit = new CircuitDefinition(DefinitionId.New(), name);
            NodeDefinition a = circuit.AddInputBoundary("A", new CircuitPoint(0, 1));
            NodeDefinition b = circuit.AddInputBoundary("B", new CircuitPoint(0, -1));
            NodeDefinition invA = circuit.AddNode(ComponentRef.Custom(nand), new CircuitPoint(3, 1));
            NodeDefinition invB = circuit.AddNode(ComponentRef.Custom(nand), new CircuitPoint(3, -1));
            NodeDefinition final = circuit.AddNode(ComponentRef.Custom(nand), new CircuitPoint(6, 0));
            NodeDefinition output = circuit.AddOutputBoundary("OUT", new CircuitPoint(9, 0));

            circuit.Connect(BoundaryOut(a), new PinRef(invA.Id, nandA));
            circuit.Connect(BoundaryOut(a), new PinRef(invA.Id, nandB));
            circuit.Connect(BoundaryOut(b), new PinRef(invB.Id, nandA));
            circuit.Connect(BoundaryOut(b), new PinRef(invB.Id, nandB));
            circuit.Connect(new PinRef(invA.Id, nandOut), new PinRef(final.Id, nandA));
            circuit.Connect(new PinRef(invB.Id, nandOut), new PinRef(final.Id, nandB));
            circuit.Connect(new PinRef(final.Id, nandOut), BoundaryIn(output));
            return circuit;
        }

        /// <summary>
        /// A circuit exposing the same interface as <paramref name="inner"/> that simply wraps one instance of it.
        /// Used to build nesting chains.
        /// </summary>
        public static CircuitDefinition Wrap(CircuitDefinition inner, string name)
        {
            var circuit = new CircuitDefinition(DefinitionId.New(), name);
            NodeDefinition instance = circuit.AddNode(ComponentRef.Custom(inner.Reference), new CircuitPoint(5, 0));
            for (int i = 0; i < inner.Inputs.Count; i++)
            {
                NodeDefinition input = circuit.AddInputBoundary(inner.Inputs[i].Name, new CircuitPoint(0, i));
                circuit.Connect(BoundaryOut(input), new PinRef(instance.Id, inner.Inputs[i].Id));
            }

            for (int i = 0; i < inner.Outputs.Count; i++)
            {
                NodeDefinition output = circuit.AddOutputBoundary(inner.Outputs[i].Name, new CircuitPoint(10, i));
                circuit.Connect(new PinRef(instance.Id, inner.Outputs[i].Id), BoundaryIn(output));
            }

            return circuit;
        }
    }
}
