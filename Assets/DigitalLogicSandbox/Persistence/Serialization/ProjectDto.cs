using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace DigitalLogicSandbox.Persistence.Serialization
{
    // Data transfer objects for the versioned JSON format. They mirror the domain model with plain values only
    // (no Unity types, no object references), so the file format can evolve independently through migrations.
    // Field names are the JSON property names (camelCase via the serializer settings).

    internal sealed class ProjectDto
    {
        public int SchemaVersion;
        public string Format;
        public AppInfoDto App;
        public string Name;
        public DateTime SavedUtc;
        public ProjectSettingsData Settings;
        public CircuitDto RootCircuit;
        public List<CircuitDto> Definitions;
        public List<InputValueDto> InputValues;
    }

    internal sealed class AppInfoDto
    {
        public string Name;
        public string Version;
    }

    internal sealed class InputValueDto
    {
        public int Port;

        /// <summary>"Low"/"High" for 0/1 (as in schema 1-2), otherwise the decimal value of a bus input.</summary>
        public string Value;
    }

    internal sealed class CircuitDto
    {
        public string Id;
        public int Version;
        public string Name;
        public string Description;
        public string Category;
        public string Icon;
        public DateTime CreatedUtc;
        public DateTime ModifiedUtc;

        /// <summary>True for library definitions (frozen on load).</summary>
        public bool Published;

        public int NextNodeId;
        public int NextPortId;
        public List<PortDto> Inputs;
        public List<PortDto> Outputs;
        public List<NodeDto> Nodes;
        public List<ConnectionDto> Connections;
    }

    internal sealed class PortDto
    {
        public int Id;
        public string Name;
    }

    internal sealed class NodeDto
    {
        public const string KindPrimitive = "primitive";
        public const string KindCustom = "custom";
        public const string KindInput = "input";
        public const string KindOutput = "output";
        public const string KindClock = "clock";
        public const string KindSplitter = "splitter";
        public const string KindMerger = "merger";
        public const string KindBusJoin = "busjoin";
        public const string KindTriState = "tristate";
        public const string KindDisplay = "display";

        public int Id;
        public string Kind;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string Primitive;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string Definition;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int? DefinitionVersion;

        /// <summary>Interface port of a boundary node.</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int? Port;

        /// <summary>Bit width of a bus boundary, splitter, merger, bus join or tri-state driver (omitted when 1).</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int? Width;

        /// <summary>Number of driver inputs of a bus join.</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int? Arity;

        /// <summary>What a display node shows (see <c>DisplayStyles.ToId</c>).</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string Style;

        public float X;
        public float Y;
        public string Rotation;

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string Label;
    }

    internal sealed class ConnectionDto
    {
        public int FromNode;
        public int FromPort;
        public int ToNode;
        public int ToPort;
    }
}
