using System;
using System.Collections.Generic;
using System.IO;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.Core.Signals;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace DigitalLogicSandbox.Persistence.Serialization
{
    /// <summary>
    /// Writes a <see cref="ProjectDocument"/> as versioned, human-readable JSON. Only plain data is written: no
    /// Unity objects, no object graphs. Each custom gate version appears once in <c>definitions</c>; instances
    /// refer to it by (id, version).
    /// </summary>
    public static class ProjectSerializer
    {
        internal static JsonSerializerSettings Settings { get; } = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            Formatting = Formatting.Indented,
            DateTimeZoneHandling = DateTimeZoneHandling.Utc,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            NullValueHandling = NullValueHandling.Include,
        };

        public static string ToJson(ProjectDocument document)
        {
            var writer = new StringWriter();
            Write(document, writer);
            return writer.ToString();
        }

        public static void Write(ProjectDocument document, TextWriter writer)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            if (document.RootCircuit == null)
            {
                throw new ArgumentException("A project needs a main circuit.", nameof(document));
            }

            if (document.SavedUtc == default)
            {
                document.SavedUtc = DateTime.UtcNow;
            }

            var dto = new ProjectDto
            {
                SchemaVersion = ProjectFormat.CurrentSchemaVersion,
                Format = ProjectFormat.FormatName,
                App = new AppInfoDto { Name = ProjectFormat.AppName, Version = ProjectFormat.AppVersion },
                Name = document.Name,
                SavedUtc = document.SavedUtc,
                Settings = document.Settings ?? new ProjectSettingsData(),
                RootCircuit = ToDto(document.RootCircuit, published: false),
                Definitions = new List<CircuitDto>(document.Definitions.Count),
                InputValues = new List<InputValueDto>(document.InputValues.Count),
                InstructionSets = new List<InstructionSetData>(document.InstructionSets.Count),
                ProgramBindings = new List<ProgramBindingData>(document.ProgramBindings.Count),
            };

            for (int i = 0; i < document.InstructionSets.Count; i++)
            {
                InstructionSetData set = document.InstructionSets[i];
                if (set != null && !string.IsNullOrEmpty(set.Id))
                {
                    dto.InstructionSets.Add(new InstructionSetData { Id = set.Id, Name = set.Name, Source = set.Source ?? string.Empty });
                }
            }

            for (int i = 0; i < document.ProgramBindings.Count; i++)
            {
                ProgramBindingData binding = document.ProgramBindings[i];
                if (binding != null && !string.IsNullOrEmpty(binding.DefinitionId) && !string.IsNullOrEmpty(binding.InstructionSetId))
                {
                    dto.ProgramBindings.Add(new ProgramBindingData { DefinitionId = binding.DefinitionId, InstructionSetId = binding.InstructionSetId });
                }
            }

            var written = new HashSet<DefinitionRef>();
            for (int i = 0; i < document.Definitions.Count; i++)
            {
                CircuitDefinition definition = document.Definitions[i];
                if (definition != null && written.Add(definition.Reference))
                {
                    dto.Definitions.Add(ToDto(definition, published: true));
                }
            }

            var ports = new List<PortId>(document.InputValues.Keys);
            ports.Sort();
            for (int i = 0; i < ports.Count; i++)
            {
                dto.InputValues.Add(new InputValueDto { Port = ports[i].Value, Value = FormatInputValue(document.InputValues[ports[i]]) });
            }

            JsonSerializer.Create(Settings).Serialize(writer, dto);
        }

        internal static CircuitDto ToDto(CircuitDefinition circuit, bool published)
        {
            var dto = new CircuitDto
            {
                Id = circuit.Id.Value,
                Version = circuit.Version,
                Name = circuit.Name,
                Description = circuit.Description,
                Category = circuit.Category,
                Icon = circuit.Icon,
                CreatedUtc = circuit.CreatedUtc,
                ModifiedUtc = circuit.ModifiedUtc,
                Published = published,
                NextNodeId = circuit.NextNodeIdValue,
                NextPortId = circuit.NextPortIdValue,
                Inputs = Ports(circuit.Inputs),
                Outputs = Ports(circuit.Outputs),
                Nodes = new List<NodeDto>(circuit.Nodes.Count),
                Connections = new List<ConnectionDto>(circuit.Connections.Count),
            };

            IReadOnlyList<NodeDefinition> nodes = circuit.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                NodeDefinition node = nodes[i];
                var nodeDto = new NodeDto
                {
                    Id = node.Id.Value,
                    X = node.Position.X,
                    Y = node.Position.Y,
                    Rotation = node.Rotation.ToString(),
                    Label = string.IsNullOrEmpty(node.Label) ? null : node.Label,
                };

                switch (node.Component.Kind)
                {
                    case ComponentKind.Primitive:
                        nodeDto.Kind = NodeDto.KindPrimitive;
                        nodeDto.Primitive = node.Component.PrimitiveId;
                        break;
                    case ComponentKind.Custom:
                        nodeDto.Kind = NodeDto.KindCustom;
                        nodeDto.Definition = node.Component.Definition.Id.Value;
                        nodeDto.DefinitionVersion = node.Component.Definition.Version;
                        break;
                    case ComponentKind.InputBoundary:
                        nodeDto.Kind = NodeDto.KindInput;
                        nodeDto.Port = node.InterfacePort.Value;
                        break;
                    case ComponentKind.OutputBoundary:
                        nodeDto.Kind = NodeDto.KindOutput;
                        nodeDto.Port = node.InterfacePort.Value;
                        break;
                    case ComponentKind.Clock:
                        nodeDto.Kind = NodeDto.KindClock;
                        if (node.Component.ClockDivider > 1)
                        {
                            nodeDto.Divider = node.Component.ClockDivider;
                        }
                        break;
                    case ComponentKind.Splitter:
                        nodeDto.Kind = NodeDto.KindSplitter;
                        break;
                    case ComponentKind.Merger:
                        nodeDto.Kind = NodeDto.KindMerger;
                        break;
                    case ComponentKind.BusJoin:
                        nodeDto.Kind = NodeDto.KindBusJoin;
                        nodeDto.Arity = node.Component.Arity;
                        break;
                    case ComponentKind.TriState:
                        nodeDto.Kind = NodeDto.KindTriState;
                        break;
                    case ComponentKind.Display:
                        nodeDto.Kind = NodeDto.KindDisplay;
                        nodeDto.Style = DisplayStyles.ToId(node.Component.DisplayStyle);
                        if (node.Component.LedColor != LedColors.Signal)
                        {
                            nodeDto.Color = LedColors.ToId(node.Component.LedColor);
                        }
                        break;
                    case ComponentKind.Constant:
                        nodeDto.Kind = NodeDto.KindConstant;
                        nodeDto.Value = node.Component.ConstantValue;
                        break;
                    default:
                        throw new InvalidOperationException("Unknown component kind " + node.Component.Kind + ".");
                }

                if (node.Component.Width > 1)
                {
                    nodeDto.Width = node.Component.Width;
                }

                dto.Nodes.Add(nodeDto);
            }

            IReadOnlyList<ConnectionDefinition> connections = circuit.Connections;
            for (int i = 0; i < connections.Count; i++)
            {
                ConnectionDefinition connection = connections[i];
                dto.Connections.Add(new ConnectionDto
                {
                    FromNode = connection.Source.Node.Value,
                    FromPort = connection.Source.Port.Value,
                    ToNode = connection.Destination.Node.Value,
                    ToPort = connection.Destination.Port.Value,
                });
            }

            return dto;
        }

        private static List<PortDto> Ports(IReadOnlyList<InterfacePort> ports)
        {
            var list = new List<PortDto>(ports.Count);
            for (int i = 0; i < ports.Count; i++)
            {
                list.Add(new PortDto { Id = ports[i].Id.Value, Name = ports[i].Name });
            }

            return list;
        }

        /// <summary>Input switch value: "Low"/"High" for 0/1 (readable by older versions), else decimal.</summary>
        internal static string FormatInputValue(ulong value)
        {
            return value == 0 ? nameof(Signal.Low)
                : value == 1 ? nameof(Signal.High)
                : value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        internal static bool TryParseInputValue(string text, out ulong value)
        {
            if (TryParseSignal(text, out Signal signal))
            {
                value = signal == Signal.High ? 1UL : 0UL;
                return signal == Signal.High || signal == Signal.Low;
            }

            return ulong.TryParse(text, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        internal static bool TryParseSignal(string text, out Signal signal)
        {
            return Enum.TryParse(text, ignoreCase: true, out signal) && Enum.IsDefined(typeof(Signal), signal);
        }
    }
}
