using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;
using UnityEngine;

namespace DigitalLogicSandbox.Rendering.Gates
{
    /// <summary>
    /// Resolved visual shape of one node: body size and pin offsets relative to the node position, with rotation
    /// already applied. Shared by rendering, hit-testing and wire routing so they can never disagree.
    /// </summary>
    public sealed class GateGeometry
    {
        public GateGeometry(ComponentKind kind, string title, string subtitle, Vector2 size, Rotation rotation,
            PortId[] inputPorts, Vector2[] inputPins, PortId[] outputPorts, Vector2[] outputPins,
            string[] inputNames = null, string[] outputNames = null)
        {
            InputNames = inputNames ?? System.Array.Empty<string>();
            OutputNames = outputNames ?? System.Array.Empty<string>();
            Kind = kind;
            Title = title ?? string.Empty;
            Subtitle = subtitle ?? string.Empty;
            Size = size;
            Rotation = rotation;
            InputPorts = inputPorts;
            InputPins = inputPins;
            OutputPorts = outputPorts;
            OutputPins = outputPins;
        }

        public ComponentKind Kind { get; }

        /// <summary>Pin labels drawn inside the body (custom gates only; empty for primitives and boundaries).</summary>
        public string[] InputNames { get; }

        public string[] OutputNames { get; }

        public bool HasPinLabels => InputNames.Length > 0 || OutputNames.Length > 0;

        public string Title { get; }

        public string Subtitle { get; }

        /// <summary>Axis-aligned body size after rotation.</summary>
        public Vector2 Size { get; }

        public Rotation Rotation { get; }

        public PortId[] InputPorts { get; }

        /// <summary>Input pin offsets from the node position.</summary>
        public Vector2[] InputPins { get; }

        public PortId[] OutputPorts { get; }

        /// <summary>Output pin offsets from the node position.</summary>
        public Vector2[] OutputPins { get; }

        public int IndexOfInput(PortId port)
        {
            for (int i = 0; i < InputPorts.Length; i++)
            {
                if (InputPorts[i] == port)
                {
                    return i;
                }
            }

            return -1;
        }

        public int IndexOfOutput(PortId port)
        {
            for (int i = 0; i < OutputPorts.Length; i++)
            {
                if (OutputPorts[i] == port)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
