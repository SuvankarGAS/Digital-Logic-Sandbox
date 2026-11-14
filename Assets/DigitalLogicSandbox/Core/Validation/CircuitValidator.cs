using System;
using System.Collections.Generic;
using System.Text;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.Utilities;

namespace DigitalLogicSandbox.Core.Validation
{
    /// <summary>
    /// Validates a circuit definition against the component library: references, pins, interface consistency,
    /// usage warnings, feedback loops (allowed since Phase B: reported as warnings because they make a circuit
    /// sequential) and the custom-gate hierarchy. Not a hot path; runs on
    /// edit/packaging/load, never per simulation step.
    /// </summary>
    public sealed class CircuitValidator
    {
        private readonly IComponentResolver _resolver;
        private readonly ValidationOptions _options;
        private readonly Dictionary<NodeId, ComponentSignature> _signatures = new Dictionary<NodeId, ComponentSignature>();
        private readonly HashSet<NodeId> _nodesWithFanOut = new HashSet<NodeId>();
        private readonly HashSet<string> _names = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<int[]> _cycles = new List<int[]>();

        public CircuitValidator(IComponentResolver resolver, ValidationOptions options = null)
        {
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _options = options ?? ValidationOptions.Default;
        }

        public ValidationReport Validate(CircuitDefinition circuit)
        {
            var report = new ValidationReport();
            Validate(circuit, report);
            return report;
        }

        public void Validate(CircuitDefinition circuit, ValidationReport report)
        {
            if (circuit == null)
            {
                throw new ArgumentNullException(nameof(circuit));
            }

            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            _signatures.Clear();
            _nodesWithFanOut.Clear();

            ResolveComponents(circuit, report);
            ValidateInterface(circuit, report);
            ValidateConnections(circuit, report);
            ValidateUsage(circuit, report);
            ValidateCycles(circuit, report);

            if (_options.ValidateHierarchy)
            {
                new HierarchyValidator(_resolver, _options.MaxHierarchyDepth).Validate(circuit, report);
            }
        }

        private void ResolveComponents(CircuitDefinition circuit, ValidationReport report)
        {
            IReadOnlyList<NodeDefinition> nodes = circuit.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                NodeDefinition node = nodes[i];
                if (_resolver.TryGetSignature(node.Component, out ComponentSignature signature))
                {
                    _signatures.Add(node.Id, signature);
                    continue;
                }

                string message = node.Component.Kind == ComponentKind.Custom
                    ? "Custom gate " + node.Component.Definition + " used by node " + node.Id + " is missing from the library."
                    : "Component '" + node.Component + "' used by node " + node.Id + " is not registered.";
                report.Add(new Diagnostic(DiagnosticSeverity.Error, DiagnosticCode.MissingComponent, message, node.Id,
                    default, node.Component.Definition));
            }
        }

        private void ValidateInterface(CircuitDefinition circuit, ValidationReport report)
        {
            ValidatePorts(circuit, circuit.Inputs, ComponentKind.InputBoundary, "input", report);
            ValidatePorts(circuit, circuit.Outputs, ComponentKind.OutputBoundary, "output", report);

            IReadOnlyList<NodeDefinition> nodes = circuit.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                NodeDefinition node = nodes[i];
                if (!node.IsBoundary)
                {
                    continue;
                }

                bool matches = node.Component.Kind == ComponentKind.InputBoundary
                    ? circuit.IndexOfInput(node.InterfacePort) >= 0
                    : circuit.IndexOfOutput(node.InterfacePort) >= 0;
                if (!matches)
                {
                    report.Add(new Diagnostic(DiagnosticSeverity.Error, DiagnosticCode.BoundaryPortMismatch,
                        "Boundary node " + node.Id + " refers to interface port " + node.InterfacePort +
                        " which does not exist.", node.Id));
                }
            }

            if (_options.RequireOutputs && circuit.Outputs.Count == 0)
            {
                report.Add(new Diagnostic(DiagnosticSeverity.Error, DiagnosticCode.NoOutputs,
                    "The circuit has no outputs. Add at least one output before packaging it as a gate."));
            }
        }

        private void ValidatePorts(CircuitDefinition circuit, IReadOnlyList<InterfacePort> ports, ComponentKind expected,
            string label, ValidationReport report)
        {
            _names.Clear();
            for (int i = 0; i < ports.Count; i++)
            {
                InterfacePort port = ports[i];
                bool hasNode = circuit.TryGetBoundaryNode(port.Id, out NodeId nodeId);
                if (!hasNode || !circuit.TryGetNode(nodeId, out NodeDefinition node) || node.Component.Kind != expected)
                {
                    report.Add(new Diagnostic(DiagnosticSeverity.Error, DiagnosticCode.BoundaryPortMismatch,
                        "Interface " + label + " '" + port.Name + "' has no matching boundary node.", nodeId));
                    continue;
                }

                string name = port.Name.Trim();
                if (name.Length == 0)
                {
                    report.Add(new Diagnostic(DiagnosticSeverity.Warning, DiagnosticCode.EmptyPortName,
                        "Interface " + label + " #" + (i + 1) + " has no name.", nodeId));
                }
                else if (!_names.Add(name))
                {
                    report.Add(new Diagnostic(DiagnosticSeverity.Error, DiagnosticCode.DuplicatePortName,
                        "Duplicate " + label + " name '" + name + "'. Interface port names must be unique.", nodeId));
                }
            }
        }

        private void ValidateConnections(CircuitDefinition circuit, ValidationReport report)
        {
            IReadOnlyList<ConnectionDefinition> connections = circuit.Connections;
            for (int i = 0; i < connections.Count; i++)
            {
                ConnectionDefinition connection = connections[i];
                bool hasSource = circuit.ContainsNode(connection.Source.Node);
                bool hasDestination = circuit.ContainsNode(connection.Destination.Node);
                if (!hasSource || !hasDestination)
                {
                    report.Add(new Diagnostic(DiagnosticSeverity.Error, DiagnosticCode.DanglingConnection,
                        "Connection " + connection + " references a node that does not exist.",
                        hasSource ? connection.Destination.Node : connection.Source.Node));
                    continue;
                }

                _nodesWithFanOut.Add(connection.Source.Node);

                if (_signatures.TryGetValue(connection.Source.Node, out ComponentSignature sourceSignature) &&
                    !sourceSignature.HasOutput(connection.Source.Port))
                {
                    report.Add(new Diagnostic(DiagnosticSeverity.Error, DiagnosticCode.InvalidSourcePort,
                        "Connection " + connection + " starts at " + connection.Source.Port +
                        ", which is not an output of node " + connection.Source.Node + ".",
                        connection.Source.Node, connection.Source));
                }

                if (_signatures.TryGetValue(connection.Destination.Node, out ComponentSignature destinationSignature) &&
                    !destinationSignature.HasInput(connection.Destination.Port))
                {
                    report.Add(new Diagnostic(DiagnosticSeverity.Error, DiagnosticCode.InvalidDestinationPort,
                        "Connection " + connection + " ends at " + connection.Destination.Port +
                        ", which is not an input of node " + connection.Destination.Node + ".",
                        connection.Destination.Node, connection.Destination));
                }
                else if (sourceSignature != null && destinationSignature != null &&
                         sourceSignature.HasOutput(connection.Source.Port))
                {
                    int sourceWidth = ConnectionRules.WidthOf(sourceSignature.Outputs, connection.Source.Port);
                    int destinationWidth = ConnectionRules.WidthOf(destinationSignature.Inputs, connection.Destination.Port);
                    if (sourceWidth != destinationWidth)
                    {
                        report.Add(new Diagnostic(DiagnosticSeverity.Error, DiagnosticCode.WidthMismatch,
                            "Connection " + connection + " joins a " + sourceWidth + "-bit output to a " +
                            destinationWidth + "-bit input. Use a splitter or merger to change width.",
                            connection.Destination.Node, connection.Destination));
                    }
                }
            }
        }

        private void ValidateUsage(CircuitDefinition circuit, ValidationReport report)
        {
            IReadOnlyList<NodeDefinition> nodes = circuit.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                NodeDefinition node = nodes[i];
                if (node.Component.Kind == ComponentKind.InputBoundary)
                {
                    if (_options.WarnUnusedInterfaceInputs && !_nodesWithFanOut.Contains(node.Id))
                    {
                        report.Add(new Diagnostic(DiagnosticSeverity.Warning, DiagnosticCode.UnusedInterfaceInput,
                            "Input '" + PortName(circuit, node.InterfacePort) + "' is not connected to anything.", node.Id));
                    }

                    continue;
                }

                if (!_signatures.TryGetValue(node.Id, out ComponentSignature signature))
                {
                    continue;
                }

                IReadOnlyList<PortDescriptor> inputs = signature.Inputs;
                for (int p = 0; p < inputs.Count; p++)
                {
                    var pin = new PinRef(node.Id, inputs[p].Id);
                    if (circuit.IsDriven(pin))
                    {
                        continue;
                    }

                    if (node.Component.Kind == ComponentKind.OutputBoundary)
                    {
                        report.Add(new Diagnostic(DiagnosticSeverity.Warning, DiagnosticCode.UndrivenInterfaceOutput,
                            "Output '" + PortName(circuit, node.InterfacePort) + "' is not driven and will read LOW.",
                            node.Id, pin));
                    }
                    else if (_options.WarnUnconnectedInputs)
                    {
                        report.Add(new Diagnostic(DiagnosticSeverity.Warning, DiagnosticCode.UnconnectedInput,
                            "Input '" + inputs[p].Name + "' of node " + node.Id + " (" + node.Component +
                            ") is unconnected and will read LOW.", node.Id, pin));
                    }
                }
            }
        }

        /// <summary>
        /// Detects feedback loops at this circuit level. Custom gates are treated conservatively as if every
        /// output depends on every input; boundaries cannot participate in loops.
        /// </summary>
        private void ValidateCycles(CircuitDefinition circuit, ValidationReport report)
        {
            IReadOnlyList<NodeDefinition> nodes = circuit.Nodes;
            IReadOnlyList<ConnectionDefinition> connections = circuit.Connections;
            int count = nodes.Count;
            if (count == 0 || connections.Count == 0)
            {
                return;
            }

            var vertexOf = new Dictionary<NodeId, int>(count);
            for (int i = 0; i < count; i++)
            {
                vertexOf[nodes[i].Id] = i;
            }

            var offsets = new int[count + 1];
            for (int i = 0; i < connections.Count; i++)
            {
                if (vertexOf.TryGetValue(connections[i].Source.Node, out int source) &&
                    vertexOf.ContainsKey(connections[i].Destination.Node))
                {
                    offsets[source + 1]++;
                }
            }

            for (int i = 0; i < count; i++)
            {
                offsets[i + 1] += offsets[i];
            }

            var targets = new int[offsets[count]];
            var fill = new int[count];
            for (int i = 0; i < connections.Count; i++)
            {
                if (vertexOf.TryGetValue(connections[i].Source.Node, out int source) &&
                    vertexOf.TryGetValue(connections[i].Destination.Node, out int destination))
                {
                    targets[offsets[source] + fill[source]++] = destination;
                }
            }

            _cycles.Clear();
            CycleFinder.FindCyclicComponents(count, offsets, targets, _cycles);
            for (int c = 0; c < _cycles.Count; c++)
            {
                int[] cycle = _cycles[c];
                var builder = new StringBuilder();
                builder.Append("Feedback loop through ").Append(cycle.Length).Append(" node(s): ");
                for (int i = 0; i < cycle.Length; i++)
                {
                    if (i > 0)
                    {
                        builder.Append(", ");
                    }

                    builder.Append(nodes[cycle[i]].Id).Append(" (").Append(nodes[cycle[i]].Component).Append(')');
                }

                builder.Append(". The circuit stores state (sequential logic), so it has no combinational truth table.");
                report.Add(new Diagnostic(DiagnosticSeverity.Warning, DiagnosticCode.FeedbackLoop,
                    builder.ToString(), nodes[cycle[0]].Id));
            }
        }

        private static string PortName(CircuitDefinition circuit, PortId port)
        {
            return circuit.TryGetInterfacePort(port, out InterfacePort interfacePort) ? interfacePort.Name : port.ToString();
        }
    }
}
