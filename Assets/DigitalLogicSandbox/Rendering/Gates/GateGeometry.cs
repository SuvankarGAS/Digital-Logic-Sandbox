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
            string[] inputNames = null, string[] outputNames = null, Vector2[] bitCells = null, int busWidth = 1,
            DisplayStyle displayStyle = DisplayStyle.Led, Rect displayArea = default, int ledColor = 0)
        {
            DisplayStyle = displayStyle;
            LedColor = ledColor;
            DisplayArea = displayArea;
            BitCells = bitCells ?? System.Array.Empty<Vector2>();
            BusWidth = busWidth;
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

        /// <summary>
        /// Centre offsets of the per-bit cells of a bus boundary, indexed by bit (0 = least significant); empty for
        /// everything else.
        /// </summary>
        public Vector2[] BitCells { get; }

        /// <summary>True for bus input/output boundaries (drawn with bit cells and a value readout).</summary>
        public bool IsBusBoundary => BitCells.Length > 0;

        /// <summary>True for visual display nodes (Phase D).</summary>
        public bool IsDisplay => Kind == ComponentKind.Display;

        /// <summary>What a display node shows (meaningless for other kinds).</summary>
        public DisplayStyle DisplayStyle { get; }

        /// <summary>Colour index of an LED display (see <see cref="LedColors"/>).</summary>
        public int LedColor { get; }

        /// <summary>Node-local area (rotation applied) holding a display's lamps or digits.</summary>
        public Rect DisplayArea { get; }

        /// <summary>Widest pin of the node in bits (1 for single-bit nodes).</summary>
        public int BusWidth { get; }

        /// <summary>Bit cell under a node-local point, or -1.</summary>
        public int BitAt(Vector2 local)
        {
            float best = GateLayout.BitCellPitch * 0.6f;
            int bit = -1;
            for (int i = 0; i < BitCells.Length; i++)
            {
                float distance = Mathf.Max(Mathf.Abs(local.x - BitCells[i].x), Mathf.Abs(local.y - BitCells[i].y));
                if (distance <= best)
                {
                    best = distance;
                    bit = i;
                }
            }

            return bit;
        }

        public string Title { get; }

        public string Subtitle { get; }

        /// <summary>The node's label drawn above a gate body ("PC", "Data RAM"), or empty.</summary>
        public string Caption { get; internal set; } = string.Empty;

        public bool HasCaption => Caption.Length > 0;

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
