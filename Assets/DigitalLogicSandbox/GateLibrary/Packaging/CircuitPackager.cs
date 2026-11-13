using System;
using System.Collections.Generic;
using DigitalLogicSandbox.Components.Abstractions;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.Core.Validation;
using DigitalLogicSandbox.GateLibrary.Custom;

namespace DigitalLogicSandbox.GateLibrary.Packaging
{
    /// <summary>Outcome of <see cref="CircuitPackager.Package"/>: the unpublished definition plus every finding.</summary>
    public sealed class PackagingResult
    {
        public PackagingResult(CircuitDefinition definition, ValidationReport report)
        {
            Definition = definition;
            Report = report;
        }

        /// <summary>The new gate definition (unpublished, mutable), or null when packaging failed.</summary>
        public CircuitDefinition Definition { get; }

        public ValidationReport Report { get; }

        public bool Succeeded => Definition != null;
    }

    /// <summary>
    /// Turns a circuit (or a selected part of it) into a new reusable gate definition. The source circuit is never
    /// modified: nodes, connections and layout are copied with their ids, and the external interface is built
    /// from the spec in the requested order and with the requested names (port ids are kept, so the mapping to
    /// internal boundaries is stable). Behaviour comes entirely from the copied graph, never from the gate name.
    /// </summary>
    public sealed class CircuitPackager
    {
        private readonly ComponentCatalog _catalog;
        private readonly DefinitionLibrary _library;
        private readonly ValidationOptions _options;
        private readonly List<ConnectionDefinition> _scratch = new List<ConnectionDefinition>();

        public CircuitPackager(ComponentCatalog catalog, DefinitionLibrary library, ValidationOptions options = null)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _library = library ?? throw new ArgumentNullException(nameof(library));
            _options = options ?? new ValidationOptions();
            _options.RequireOutputs = true;
        }

        /// <summary>
        /// Builds and validates the gate described by <paramref name="spec"/>. Nothing is published: callers publish
        /// <see cref="PackagingResult.Definition"/> (normally through an undoable command).
        /// </summary>
        public PackagingResult Package(CircuitDefinition source, PackagingSpec spec)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (spec == null)
            {
                throw new ArgumentNullException(nameof(spec));
            }

            var report = new ValidationReport();
            ValidateMetadata(spec, report);
            ValidateInterfaceSpec(source, spec, report);
            if (report.HasErrors)
            {
                return new PackagingResult(null, report);
            }

            CircuitDefinition definition = Build(source, spec, report);
            new CircuitValidator(_catalog, _options).Validate(definition, report);
            ValidateDrivenOutputs(definition, report);
            if (definition.Inputs.Count == 0)
            {
                report.Add(new Diagnostic(DiagnosticSeverity.Warning, DiagnosticCode.NoInputs,
                    "The gate has no inputs, so its outputs are constant."));
            }

            return new PackagingResult(report.HasErrors ? null : definition, report);
        }

        /// <summary>
        /// Validates an existing gate definition with the same rules used for packaging (e.g. an edited draft before
        /// it is saved as a new version): name, structure, at least one connected output, hierarchy.
        /// </summary>
        public ValidationReport ValidateDefinition(CircuitDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            var report = new ValidationReport();
            if (string.IsNullOrWhiteSpace(definition.Name))
            {
                report.Add(new Diagnostic(DiagnosticSeverity.Error, DiagnosticCode.MissingName,
                    "The gate needs a name."));
            }

            new CircuitValidator(_catalog, _options).Validate(definition, report);
            ValidateDrivenOutputs(definition, report);
            ValidatePortNames(definition.Inputs, report);
            ValidatePortNames(definition.Outputs, report);
            return report;
        }

        private static void ValidatePortNames(IReadOnlyList<InterfacePort> ports, ValidationReport report)
        {
            for (int i = 0; i < ports.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(ports[i].Name))
                {
                    report.Add(new Diagnostic(DiagnosticSeverity.Error, DiagnosticCode.EmptyPortName,
                        "Every external port needs a name."));
                    return;
                }

                for (int j = 0; j < i; j++)
                {
                    if (string.Equals(ports[i].Name.Trim(), ports[j].Name.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        report.Add(new Diagnostic(DiagnosticSeverity.Error, DiagnosticCode.DuplicatePortName,
                            "Two external ports are both named '" + ports[i].Name + "'."));
                        return;
                    }
                }
            }
        }

        private void ValidateMetadata(PackagingSpec spec, ValidationReport report)
        {
            if (string.IsNullOrWhiteSpace(spec.Name))
            {
                report.Add(new Diagnostic(DiagnosticSeverity.Error, DiagnosticCode.MissingName,
                    "Give the gate a name."));
            }
            else if (_library.ContainsName(spec.Name) || IsPrimitiveName(spec.Name))
            {
                report.Add(new Diagnostic(DiagnosticSeverity.Warning, DiagnosticCode.DuplicateGateName,
                    "A gate called '" + spec.Name.Trim() + "' already exists. Both will be kept; " +
                    "consider a distinct name to tell them apart."));
            }
        }

        private static void ValidateInterfaceSpec(CircuitDefinition source, PackagingSpec spec, ValidationReport report)
        {
            var seen = new HashSet<PortId>();
            CheckPorts(source, spec, spec.Inputs, ComponentKind.InputBoundary, "input", seen, report);
            CheckPorts(source, spec, spec.Outputs, ComponentKind.OutputBoundary, "output", seen, report);

            // Every boundary in scope must be mapped exactly once.
            IReadOnlyList<NodeDefinition> nodes = source.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                NodeDefinition node = nodes[i];
                if (node.IsBoundary && spec.Contains(node.Id) && !seen.Contains(node.InterfacePort))
                {
                    string name = source.TryGetInterfacePort(node.InterfacePort, out InterfacePort port) ? port.Name : "?";
                    report.Add(new Diagnostic(DiagnosticSeverity.Error, DiagnosticCode.InterfaceMismatch,
                        "Boundary '" + name + "' is part of the circuit but not mapped to an external port.", node.Id));
                }
            }
        }

        private static void CheckPorts(CircuitDefinition source, PackagingSpec spec, List<PortSpec> ports,
            ComponentKind expected, string label, HashSet<PortId> seen, ValidationReport report)
        {
            for (int i = 0; i < ports.Count; i++)
            {
                PortId port = ports[i].SourcePort;
                if (string.IsNullOrWhiteSpace(ports[i].Name))
                {
                    report.Add(new Diagnostic(DiagnosticSeverity.Error, DiagnosticCode.EmptyPortName,
                        "Every external " + label + " needs a name (" + label + " #" + (i + 1) + " is empty)."));
                }

                if (!seen.Add(port))
                {
                    report.Add(new Diagnostic(DiagnosticSeverity.Error, DiagnosticCode.InterfaceMismatch,
                        "External " + label + " '" + ports[i].Name + "' is mapped more than once."));
                    continue;
                }

                if (!source.TryGetBoundaryNode(port, out NodeId node) || !source.TryGetNode(node, out NodeDefinition boundary) ||
                    boundary.Component.Kind != expected)
                {
                    report.Add(new Diagnostic(DiagnosticSeverity.Error, DiagnosticCode.InterfaceMismatch,
                        "External " + label + " '" + ports[i].Name + "' does not refer to a circuit " + label + "."));
                    continue;
                }

                if (!spec.Contains(node))
                {
                    report.Add(new Diagnostic(DiagnosticSeverity.Error, DiagnosticCode.InterfaceMismatch,
                        "External " + label + " '" + ports[i].Name + "' refers to a boundary outside the selection.", node));
                }
            }
        }

        private CircuitDefinition Build(CircuitDefinition source, PackagingSpec spec, ValidationReport report)
        {
            var definition = new CircuitDefinition(DefinitionId.New(), spec.Name.Trim())
            {
                Description = spec.Description?.Trim() ?? string.Empty,
                Category = spec.Category?.Trim() ?? string.Empty,
                Icon = spec.Icon?.Trim() ?? string.Empty,
            };

            // Boundaries first, in the requested interface order (this defines the external port order).
            AddBoundaries(source, spec.Inputs, PortDirection.Input, definition);
            AddBoundaries(source, spec.Outputs, PortDirection.Output, definition);

            IReadOnlyList<NodeDefinition> nodes = source.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                NodeDefinition node = nodes[i];
                if (!node.IsBoundary && spec.Contains(node.Id))
                {
                    definition.RestoreNode(node.Id, node.Component, node.Position, node.Rotation, node.Label);
                }
            }

            IReadOnlyList<ConnectionDefinition> connections = source.Connections;
            for (int i = 0; i < connections.Count; i++)
            {
                ConnectionDefinition connection = connections[i];
                bool sourceInside = spec.Contains(connection.Source.Node);
                bool destinationInside = spec.Contains(connection.Destination.Node);
                if (sourceInside && destinationInside)
                {
                    definition.Connect(connection.Source, connection.Destination);
                }
                else if (sourceInside || destinationInside)
                {
                    NodeId inside = sourceInside ? connection.Source.Node : connection.Destination.Node;
                    report.Add(new Diagnostic(DiagnosticSeverity.Warning, DiagnosticCode.ExternalConnection,
                        "A wire to a gate outside the selection is not included" +
                        (destinationInside ? "; that input will be unconnected" : string.Empty) +
                        ". Add Input/Output boundaries to expose it.", inside));
                }
            }

            return definition;
        }

        private static void AddBoundaries(CircuitDefinition source, List<PortSpec> ports, PortDirection direction,
            CircuitDefinition definition)
        {
            for (int i = 0; i < ports.Count; i++)
            {
                source.TryGetBoundaryNode(ports[i].SourcePort, out NodeId nodeId);
                NodeDefinition node = source.GetNode(nodeId);
                definition.RestoreBoundary(node.Id, ports[i].SourcePort, direction, ports[i].Name?.Trim() ?? string.Empty,
                    node.Position, node.Rotation, node.Label);
            }
        }

        private static void ValidateDrivenOutputs(CircuitDefinition definition, ValidationReport report)
        {
            if (definition.Outputs.Count == 0)
            {
                return; // already reported as NoOutputs
            }

            for (int i = 0; i < definition.Outputs.Count; i++)
            {
                if (definition.TryGetBoundaryNode(definition.Outputs[i].Id, out NodeId node) &&
                    definition.IsDriven(new PinRef(node, BoundarySignatures.Pin)))
                {
                    return;
                }
            }

            report.Add(new Diagnostic(DiagnosticSeverity.Error, DiagnosticCode.NoOutputs,
                "None of the outputs is connected. Connect at least one output before packaging."));
        }

        private bool IsPrimitiveName(string name)
        {
            IReadOnlyList<IPrimitiveComponent> primitives = _catalog.Primitives.All;
            for (int i = 0; i < primitives.Count; i++)
            {
                if (string.Equals(primitives[i].DisplayName, name.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
