using System.Collections.Generic;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;
using UnityEngine;

namespace DigitalLogicSandbox.Rendering.Gates
{
    /// <summary>
    /// Computes gate shapes from component signatures only: inputs on the left, outputs on the right, one grid unit
    /// apart. Nothing here depends on which gate it is, so custom gates get the same treatment as primitives.
    /// </summary>
    public static class GateLayout
    {
        public const float PinSpacing = 1f;
        public const float MinGateWidth = 3f;
        public const float MinGateHeight = 2f;
        public static readonly Vector2 BoundarySize = new Vector2(2.4f, 1.2f);

        /// <summary>Distance between bit cells of a bus boundary.</summary>
        public const float BitCellPitch = 0.42f;

        /// <summary>Side of one bit cell.</summary>
        public const float BitCellSize = 0.34f;

        /// <summary>Body width of splitters and mergers.</summary>
        public const float WiringWidth = 1.2f;

        /// <param name="icon">Optional short user icon of a custom gate, shown before the pin summary.</param>
        public static GateGeometry Compute(ComponentKind kind, ComponentSignature signature, Rotation rotation,
            string title, string icon = null)
        {
            IReadOnlyList<PortDescriptor> inputs = signature.Inputs;
            IReadOnlyList<PortDescriptor> outputs = signature.Outputs;
            Vector2 size;
            var inputPins = new Vector2[inputs.Count];
            var outputPins = new Vector2[outputs.Count];
            Vector2[] bitCells = null;
            int busWidth = Mathf.Max(WidestPin(inputs), WidestPin(outputs));
            bool boundary = kind == ComponentKind.InputBoundary || kind == ComponentKind.OutputBoundary;

            if (boundary && busWidth > 1)
            {
                // Bus boundary: title row above a row of bit cells (most significant bit on the left).
                size = new Vector2(Mathf.Max(BoundarySize.x, busWidth * BitCellPitch + 1.0f), 1.6f);
                float edge = size.x * 0.5f;
                for (int i = 0; i < inputPins.Length; i++)
                {
                    inputPins[i] = new Vector2(-edge, 0f);
                }

                for (int i = 0; i < outputPins.Length; i++)
                {
                    outputPins[i] = new Vector2(edge, 0f);
                }

                bitCells = new Vector2[busWidth];
                float left = -(busWidth - 1) * 0.5f * BitCellPitch;
                for (int bit = 0; bit < busWidth; bit++)
                {
                    bitCells[bit] = new Vector2(left + (busWidth - 1 - bit) * BitCellPitch, -0.3f);
                }
            }
            else if (kind == ComponentKind.Splitter || kind == ComponentKind.Merger)
            {
                // A narrow bar: one bus pin on one side, one pin per bit on the other (bit 0 on top).
                int bits = Mathf.Max(inputs.Count, outputs.Count);
                size = new Vector2(WiringWidth, Mathf.Max(MinGateHeight, bits * PinSpacing));
                float edge = size.x * 0.5f;
                PlacePins(inputPins, -edge);
                PlacePins(outputPins, edge);
            }
            else if (boundary || kind == ComponentKind.Clock)
            {
                // Boundaries and the clock are compact utility boxes with a state indicator.
                size = BoundarySize;
                float edge = size.x * 0.5f;
                for (int i = 0; i < inputPins.Length; i++)
                {
                    inputPins[i] = new Vector2(-edge, 0f);
                }

                for (int i = 0; i < outputPins.Length; i++)
                {
                    outputPins[i] = new Vector2(edge, 0f);
                }
            }
            else
            {
                int rows = Mathf.Max(inputs.Count, outputs.Count);
                float width = Mathf.Max(MinGateWidth, Mathf.Ceil((title?.Length ?? 0) * 0.32f + 1.6f));
                if (HasPinLabels(kind))
                {
                    // Room for pin labels on both sides of the title.
                    float labels = (LongestName(inputs) + LongestName(outputs)) * PinLabelCharWidth;
                    width = Mathf.Max(width, Mathf.Ceil((title?.Length ?? 0) * 0.32f + labels + 1.2f));
                }
                float height = Mathf.Max(MinGateHeight, rows * PinSpacing);
                size = new Vector2(width, height);
                float edge = width * 0.5f;
                PlacePins(inputPins, -edge);
                PlacePins(outputPins, edge);
            }

            for (int i = 0; i < inputPins.Length; i++)
            {
                inputPins[i] = Rotate(inputPins[i], rotation);
            }

            for (int i = 0; i < outputPins.Length; i++)
            {
                outputPins[i] = Rotate(outputPins[i], rotation);
            }

            if (rotation == Rotation.R90 || rotation == Rotation.R270)
            {
                size = new Vector2(size.y, size.x);
            }

            var inputPorts = new PortId[inputs.Count];
            for (int i = 0; i < inputPorts.Length; i++)
            {
                inputPorts[i] = inputs[i].Id;
            }

            var outputPorts = new PortId[outputs.Count];
            for (int i = 0; i < outputPorts.Length; i++)
            {
                outputPorts[i] = outputs[i].Id;
            }

            string subtitle = kind == ComponentKind.Custom || kind == ComponentKind.Primitive
                ? inputs.Count + " in / " + outputs.Count + " out"
                : string.Empty;
            if (bitCells != null)
            {
                for (int i = 0; i < bitCells.Length; i++)
                {
                    bitCells[i] = Rotate(bitCells[i], rotation);
                }
            }

            if (kind == ComponentKind.Custom && !string.IsNullOrWhiteSpace(icon))
            {
                subtitle = icon.Trim() + "  ·  " + subtitle;
            }
            string[] inputNames = null;
            string[] outputNames = null;
            if (HasPinLabels(kind))
            {
                inputNames = Names(inputs);
                outputNames = Names(outputs);
                if (kind == ComponentKind.Splitter)
                {
                    inputNames = new string[inputs.Count]; // the bus pin needs no label
                }
                else if (kind == ComponentKind.Merger)
                {
                    outputNames = new string[outputs.Count];
                }
            }

            return new GateGeometry(kind, title, subtitle, size, rotation, inputPorts, inputPins, outputPorts, outputPins,
                inputNames, outputNames, bitCells, busWidth);
        }

        /// <summary>Kinds whose pins are labelled inside the body (their pins differ in meaning).</summary>
        public static bool HasPinLabels(ComponentKind kind)
        {
            return kind == ComponentKind.Custom || kind == ComponentKind.Splitter || kind == ComponentKind.Merger ||
                kind == ComponentKind.BusJoin || kind == ComponentKind.TriState;
        }

        private static int WidestPin(IReadOnlyList<PortDescriptor> ports)
        {
            int widest = 1;
            for (int i = 0; i < ports.Count; i++)
            {
                widest = Mathf.Max(widest, ports[i].Width);
            }

            return widest;
        }

        /// <summary>Rotates an offset counter-clockwise in 90° steps.</summary>
        public static Vector2 Rotate(Vector2 offset, Rotation rotation)
        {
            switch (rotation)
            {
                case Rotation.R90:
                    return new Vector2(-offset.y, offset.x);
                case Rotation.R180:
                    return new Vector2(-offset.x, -offset.y);
                case Rotation.R270:
                    return new Vector2(offset.y, -offset.x);
                default:
                    return offset;
            }
        }

        /// <summary>Approximate world width of one pin-label character.</summary>
        public const float PinLabelCharWidth = 0.24f;

        private static int LongestName(IReadOnlyList<PortDescriptor> ports)
        {
            int longest = 0;
            for (int i = 0; i < ports.Count; i++)
            {
                longest = Mathf.Max(longest, ports[i].Name?.Length ?? 0);
            }

            return longest;
        }

        private static string[] Names(IReadOnlyList<PortDescriptor> ports)
        {
            var names = new string[ports.Count];
            for (int i = 0; i < names.Length; i++)
            {
                names[i] = ports[i].Width > 1 && !string.IsNullOrEmpty(ports[i].Name)
                    ? ports[i].Name + "[" + ports[i].Width + "]"
                    : ports[i].Name ?? string.Empty;
            }

            return names;
        }

        public static Rotation Next(Rotation rotation)
        {
            return (Rotation)(((int)rotation + 1) % 4);
        }

        private static void PlacePins(Vector2[] pins, float x)
        {
            float top = (pins.Length - 1) * 0.5f * PinSpacing;
            for (int i = 0; i < pins.Length; i++)
            {
                pins[i] = new Vector2(x, top - i * PinSpacing);
            }
        }
    }
}
