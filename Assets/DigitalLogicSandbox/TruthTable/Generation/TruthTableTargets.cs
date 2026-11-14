using System;
using DigitalLogicSandbox.Components.Abstractions;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;

namespace DigitalLogicSandbox.TruthTable.Generation
{
    /// <summary>Resolves what to tabulate when the user asks for the truth table of a placed component.</summary>
    public static class TruthTableTargets
    {
        /// <summary>
        /// Returns a circuit whose interface matches <paramref name="component"/>: the packaged definition itself
        /// for a custom gate, or a one-node test harness for a primitive (each pin wired to a boundary named after
        /// it), so built-in gates are also tabulated by the simulation engine rather than from stored data.
        /// Returns null when the component cannot be resolved or is a utility (boundary or clock).
        /// </summary>
        public static CircuitDefinition ForComponent(ComponentCatalog catalog, ComponentRef component, string name)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (component.IsUtility)
            {
                return null;
            }

            if (component.Kind == ComponentKind.Custom)
            {
                return catalog.TryGetDefinition(component.Definition, out CircuitDefinition definition) ? definition : null;
            }

            if (!catalog.TryGetSignature(component, out ComponentSignature signature))
            {
                return null;
            }

            var harness = new CircuitDefinition(DefinitionId.New(), string.IsNullOrEmpty(name) ? component.ToString() : name);
            NodeDefinition node = harness.AddNode(component, new CircuitPoint(4f, 0f));
            for (int i = 0; i < signature.InputCount; i++)
            {
                PortDescriptor pin = signature.Inputs[i];
                NodeDefinition boundary = harness.AddInputBoundary(pin.Name, new CircuitPoint(0f, -i * 2f), pin.Width);
                harness.Connect(new PinRef(boundary.Id, BoundarySignatures.Pin), new PinRef(node.Id, pin.Id));
            }

            for (int i = 0; i < signature.OutputCount; i++)
            {
                PortDescriptor pin = signature.Outputs[i];
                NodeDefinition boundary = harness.AddOutputBoundary(pin.Name, new CircuitPoint(8f, -i * 2f), pin.Width);
                harness.Connect(new PinRef(node.Id, pin.Id), new PinRef(boundary.Id, BoundarySignatures.Pin));
            }

            return harness;
        }
    }
}
