using System;
using System.Collections.Generic;
using System.IO;
using DigitalLogicSandbox.Components.Abstractions;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.Core.Signals;
using DigitalLogicSandbox.Core.Validation;
using DigitalLogicSandbox.Persistence.Migration;
using DigitalLogicSandbox.Persistence.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DigitalLogicSandbox.Persistence.Deserialization
{
    /// <summary>Outcome of reading a project: the document plus everything worth telling the user.</summary>
    public sealed class ProjectLoadResult
    {
        internal ProjectLoadResult(ProjectDocument document, ValidationReport report, int storedSchemaVersion,
            List<string> migrations)
        {
            Document = document;
            Report = report;
            StoredSchemaVersion = storedSchemaVersion;
            AppliedMigrations = migrations;
        }

        public ProjectDocument Document { get; }

        /// <summary>Warnings found while binding (missing references, skipped connections, ...).</summary>
        public ValidationReport Report { get; }

        public int StoredSchemaVersion { get; }

        public IReadOnlyList<string> AppliedMigrations { get; }

        public bool WasMigrated => AppliedMigrations.Count > 0;
    }

    /// <summary>
    /// Reads project JSON: parses, migrates older schemas, binds DTOs to domain objects with their persisted ids,
    /// and validates references. Unreadable files throw <see cref="ProjectFormatException"/>; recoverable
    /// problems (a missing custom gate, an invalid connection) become warnings so the user keeps their work.
    /// </summary>
    public sealed class ProjectDeserializer
    {
        private readonly ProjectMigrator _migrator;
        private readonly PrimitiveRegistry _primitives;

        public ProjectDeserializer(ProjectMigrator migrator = null, PrimitiveRegistry primitives = null)
        {
            _migrator = migrator ?? ProjectMigrator.CreateDefault();
            _primitives = primitives;
        }

        public ProjectLoadResult Read(string json)
        {
            if (json == null)
            {
                throw new ArgumentNullException(nameof(json));
            }

            JObject root;
            try
            {
                using (var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None })
                {
                    root = JObject.Load(reader);
                }
            }
            catch (JsonException exception)
            {
                throw new ProjectFormatException("The project file is damaged and cannot be read (" + exception.Message + ").", exception);
            }

            string format = root.Value<string>("format");
            if (format != null && !string.Equals(format, ProjectFormat.FormatName, StringComparison.Ordinal))
            {
                throw new ProjectFormatException("The file is not a Digital Logic Sandbox project (format '" + format + "').");
            }

            var migrations = new List<string>();
            int stored = _migrator.Migrate(root, migrations);

            ProjectDto dto;
            try
            {
                dto = root.ToObject<ProjectDto>(JsonSerializer.Create(ProjectSerializer.Settings));
            }
            catch (JsonException exception)
            {
                throw new ProjectFormatException("The project file contains invalid data (" + exception.Message + ").", exception);
            }

            if (dto?.RootCircuit == null)
            {
                throw new ProjectFormatException("The project file has no main circuit.");
            }

            var report = new ValidationReport();
            var document = new ProjectDocument
            {
                Name = string.IsNullOrEmpty(dto.Name) ? "Untitled" : dto.Name,
                SavedUtc = dto.SavedUtc,
                Settings = dto.Settings ?? new ProjectSettingsData(),
            };
            Normalize(document.Settings);

            var available = new HashSet<DefinitionRef>();
            if (dto.Definitions != null)
            {
                for (int i = 0; i < dto.Definitions.Count; i++)
                {
                    CircuitDto definitionDto = dto.Definitions[i];
                    if (definitionDto == null)
                    {
                        continue;
                    }

                    CircuitDefinition definition = Bind(definitionDto, report);
                    if (!available.Add(definition.Reference))
                    {
                        report.Add(new Diagnostic(DiagnosticSeverity.Warning, DiagnosticCode.MissingDefinition,
                            "Custom gate '" + definition.Name + "' " + definition.Reference + " is stored twice; the duplicate was ignored."));
                        continue;
                    }

                    document.Definitions.Add(definition);
                }
            }

            document.RootCircuit = Bind(dto.RootCircuit, report);
            ValidateReferences(document.RootCircuit, available, report);
            for (int i = 0; i < document.Definitions.Count; i++)
            {
                ValidateReferences(document.Definitions[i], available, report);
            }

            if (dto.InputValues != null)
            {
                for (int i = 0; i < dto.InputValues.Count; i++)
                {
                    InputValueDto value = dto.InputValues[i];
                    if (value == null || value.Port < 0)
                    {
                        continue;
                    }

                    var port = new PortId(value.Port);
                    if (document.RootCircuit.IndexOfInput(port) < 0 ||
                        !ProjectSerializer.TryParseSignal(value.Value, out Signal signal))
                    {
                        continue; // stale or invalid switch values are simply dropped
                    }

                    document.InputValues[port] = signal;
                }
            }

            // Freeze library definitions last so binding could mutate them.
            for (int i = 0; i < document.Definitions.Count; i++)
            {
                document.Definitions[i].Freeze();
            }

            return new ProjectLoadResult(document, report, stored, migrations);
        }

        private CircuitDefinition Bind(CircuitDto dto, ValidationReport report)
        {
            if (string.IsNullOrEmpty(dto.Id))
            {
                throw new ProjectFormatException("A circuit in the project has no id.");
            }

            string name = dto.Name ?? string.Empty;
            CircuitDefinition circuit;
            try
            {
                circuit = new CircuitDefinition(new DefinitionId(dto.Id), name, Math.Max(1, dto.Version));
            }
            catch (ArgumentException exception)
            {
                throw new ProjectFormatException("Circuit '" + name + "' has invalid identity data: " + exception.Message, exception);
            }

            circuit.Description = dto.Description ?? string.Empty;
            circuit.Category = dto.Category ?? string.Empty;
            circuit.Icon = dto.Icon ?? string.Empty;

            Dictionary<int, string> inputNames = Names(dto.Inputs);
            Dictionary<int, string> outputNames = Names(dto.Outputs);

            if (dto.Nodes != null)
            {
                for (int i = 0; i < dto.Nodes.Count; i++)
                {
                    NodeDto node = dto.Nodes[i];
                    if (node == null)
                    {
                        continue;
                    }

                    try
                    {
                        BindNode(circuit, node, inputNames, outputNames, report);
                    }
                    catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException)
                    {
                        throw new ProjectFormatException("Node " + node.Id + " of circuit '" + name + "' is invalid: " +
                            exception.Message, exception);
                    }
                }
            }

            RestoreInterfaceOrder(circuit, dto.Inputs, PortDirection.Input, name);
            RestoreInterfaceOrder(circuit, dto.Outputs, PortDirection.Output, name);

            if (dto.Connections != null)
            {
                for (int i = 0; i < dto.Connections.Count; i++)
                {
                    ConnectionDto connection = dto.Connections[i];
                    if (connection == null)
                    {
                        continue;
                    }

                    ConnectionError error = ConnectionError.MissingSourceNode;
                    if (connection.FromNode > 0 && connection.ToNode > 0 && connection.FromPort >= 0 &&
                        connection.ToPort >= 0)
                    {
                        var source = new PinRef(new NodeId(connection.FromNode), new PortId(connection.FromPort));
                        var destination = new PinRef(new NodeId(connection.ToNode), new PortId(connection.ToPort));
                        error = circuit.TryConnect(source, destination);
                    }

                    if (error != ConnectionError.None)
                    {
                        report.Add(new Diagnostic(DiagnosticSeverity.Warning, DiagnosticCode.DanglingConnection,
                            "A connection " + connection.FromNode + "." + connection.FromPort + " -> " + connection.ToNode +
                            "." + connection.ToPort + " in '" + name + "' was skipped (" + error + ")."));
                    }
                }
            }

            circuit.RestoreIdAllocators(dto.NextNodeId, dto.NextPortId);
            if (dto.CreatedUtc != default)
            {
                circuit.RestoreTimestamps(dto.CreatedUtc, dto.ModifiedUtc == default ? dto.CreatedUtc : dto.ModifiedUtc);
            }

            return circuit;
        }

        private void BindNode(CircuitDefinition circuit, NodeDto node, Dictionary<int, string> inputNames,
            Dictionary<int, string> outputNames, ValidationReport report)
        {
            var id = new NodeId(node.Id);
            var position = new CircuitPoint(node.X, node.Y);
            Rotation rotation = Enum.TryParse(node.Rotation, out Rotation parsed) && Enum.IsDefined(typeof(Rotation), parsed)
                ? parsed
                : Rotation.R0;

            switch (node.Kind)
            {
                case NodeDto.KindPrimitive:
                    if (string.IsNullOrEmpty(node.Primitive))
                    {
                        throw new ArgumentException("A primitive node has no component id.");
                    }

                    if (_primitives != null && !_primitives.TryGet(node.Primitive, out _))
                    {
                        report.Add(new Diagnostic(DiagnosticSeverity.Warning, DiagnosticCode.MissingComponent,
                            "Built-in component '" + node.Primitive + "' used in '" + circuit.Name + "' is not available.", id));
                    }

                    circuit.RestoreNode(id, ComponentRef.Primitive(node.Primitive), position, rotation, node.Label);
                    break;
                case NodeDto.KindCustom:
                    if (string.IsNullOrEmpty(node.Definition))
                    {
                        throw new ArgumentException("A custom gate node has no definition id.");
                    }

                    var reference = new DefinitionRef(new DefinitionId(node.Definition), Math.Max(1, node.DefinitionVersion ?? 1));
                    circuit.RestoreNode(id, ComponentRef.Custom(reference), position, rotation, node.Label);
                    break;
                case NodeDto.KindInput:
                case NodeDto.KindOutput:
                {
                    if (node.Port == null)
                    {
                        throw new ArgumentException("A boundary node has no interface port.");
                    }

                    bool input = node.Kind == NodeDto.KindInput;
                    int port = node.Port.Value;
                    Dictionary<int, string> names = input ? inputNames : outputNames;
                    if (!names.TryGetValue(port, out string portName))
                    {
                        throw new ArgumentException("Interface port " + port + " is not declared.");
                    }

                    circuit.RestoreBoundary(id, new PortId(port), input ? PortDirection.Input : PortDirection.Output,
                        portName, position, rotation, node.Label);
                    break;
                }

                default:
                    throw new ArgumentException("Unknown node kind '" + node.Kind + "'.");
            }
        }

        private static void RestoreInterfaceOrder(CircuitDefinition circuit, List<PortDto> ports, PortDirection direction,
            string circuitName)
        {
            if (ports == null)
            {
                return;
            }

            IReadOnlyList<InterfacePort> actual = direction == PortDirection.Input ? circuit.Inputs : circuit.Outputs;
            if (actual.Count != ports.Count)
            {
                throw new ProjectFormatException("Circuit '" + circuitName + "' declares " + ports.Count + " " +
                    (direction == PortDirection.Input ? "inputs" : "outputs") + " but has " + actual.Count + " boundary nodes.");
            }

            for (int i = 0; i < ports.Count; i++)
            {
                try
                {
                    circuit.MoveInterfacePort(new PortId(ports[i]?.Id ?? -1), i);
                }
                catch (Exception exception) when (exception is ArgumentException || exception is KeyNotFoundException)
                {
                    throw new ProjectFormatException("Circuit '" + circuitName + "' has an invalid interface port list: " +
                        exception.Message, exception);
                }
            }
        }

        private static void ValidateReferences(CircuitDefinition circuit, HashSet<DefinitionRef> available,
            ValidationReport report)
        {
            IReadOnlyList<NodeDefinition> nodes = circuit.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                ComponentRef component = nodes[i].Component;
                if (component.Kind == ComponentKind.Custom && !available.Contains(component.Definition))
                {
                    report.Add(new Diagnostic(DiagnosticSeverity.Warning, DiagnosticCode.MissingDefinition,
                        "'" + circuit.Name + "' uses custom gate " + component.Definition +
                        ", which is not in the project. The instance is kept but cannot be simulated.",
                        nodes[i].Id, default, component.Definition));
                }
            }
        }

        private static Dictionary<int, string> Names(List<PortDto> ports)
        {
            var names = new Dictionary<int, string>();
            if (ports == null)
            {
                return names;
            }

            for (int i = 0; i < ports.Count; i++)
            {
                if (ports[i] != null && !names.ContainsKey(ports[i].Id))
                {
                    names.Add(ports[i].Id, ports[i].Name ?? string.Empty);
                }
            }

            return names;
        }

        private static void Normalize(ProjectSettingsData settings)
        {
            settings.Animation ??= new AnimationSettingsData();
            settings.TruthTable ??= new TruthTableSettingsData();
            settings.Camera ??= new CameraSettingsData();
            settings.VisualizationMode ??= "Standard";
        }
    }
}
