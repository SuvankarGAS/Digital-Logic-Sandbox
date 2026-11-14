using System.Collections.Generic;
using DigitalLogicSandbox.Core.Nodes;

namespace DigitalLogicSandbox.Core.Ports
{
    /// <summary>
    /// Pin layouts of the circuit utility components: interface boundaries, the clock and bus wiring.
    /// <list type="bullet">
    /// <item>An input boundary drives the circuit through one output pin; an output boundary observes one input
    /// pin. Either may be a bus (width &gt; 1).</item>
    /// <item>A splitter has the bus input <see cref="Pin"/> and one single-bit output per bit
    /// (<see cref="BitPin"/>: id <c>bit + 1</c>, bit 0 = least significant).</item>
    /// <item>A merger has single-bit inputs (<see cref="BitPin"/>) and the bus output <see cref="Pin"/>.</item>
    /// <item>A bus join has driver inputs <see cref="BitPin"/>(0..n-1) of the bus width and the joined output
    /// <see cref="Pin"/>.</item>
    /// </list>
    /// Signatures are immutable and cached per parameter set.
    /// </summary>
    public static class BoundarySignatures
    {
        /// <summary>The single pin of every boundary node, and the bus pin of splitters, mergers and bus joins.</summary>
        public static readonly PortId Pin = new PortId(0);

        public static readonly ComponentSignature Input = InputOf(1);

        public static readonly ComponentSignature Output = OutputOf(1);

        /// <summary>Clock source: a single output pin carrying the global clock.</summary>
        public static readonly ComponentSignature Clock =
            new ComponentSignature(null, new[] { new PortDescriptor(Pin, "CLK") });

        private static Dictionary<int, ComponentSignature> _cache;

        /// <summary>Per-bit (or per-driver) pin id of splitters, mergers and bus joins.</summary>
        public static PortId BitPin(int index)
        {
            return new PortId(index + 1);
        }

        /// <summary>Inverse of <see cref="BitPin"/>; -1 for <see cref="Pin"/>.</summary>
        public static int IndexOfBitPin(PortId pin)
        {
            return pin.Value - 1;
        }

        public static ComponentSignature InputOf(int width)
        {
            return Cached(0, width, 0, () => new ComponentSignature(null, new[] { new PortDescriptor(Pin, "OUT", width) }));
        }

        public static ComponentSignature OutputOf(int width)
        {
            return Cached(1, width, 0, () => new ComponentSignature(new[] { new PortDescriptor(Pin, "IN", width) }, null));
        }

        public static ComponentSignature Splitter(int width)
        {
            return Cached(2, width, 0, () =>
            {
                var bits = new PortDescriptor[width];
                for (int i = 0; i < width; i++)
                {
                    bits[i] = new PortDescriptor(BitPin(i), i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }

                return new ComponentSignature(new[] { new PortDescriptor(Pin, "IN", width) }, bits);
            });
        }

        public static ComponentSignature Merger(int width)
        {
            return Cached(3, width, 0, () =>
            {
                var bits = new PortDescriptor[width];
                for (int i = 0; i < width; i++)
                {
                    bits[i] = new PortDescriptor(BitPin(i), i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }

                return new ComponentSignature(bits, new[] { new PortDescriptor(Pin, "OUT", width) });
            });
        }

        public static ComponentSignature BusJoin(int width, int inputs)
        {
            return Cached(4, width, inputs, () =>
            {
                var drivers = new PortDescriptor[inputs];
                for (int i = 0; i < inputs; i++)
                {
                    drivers[i] = new PortDescriptor(BitPin(i),
                        "D" + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), width);
                }

                return new ComponentSignature(drivers, new[] { new PortDescriptor(Pin, "BUS", width) });
            });
        }

        /// <summary>Tri-state driver pins.</summary>
        public static readonly PortId TriStateData = new PortId(0);

        public static readonly PortId TriStateEnable = new PortId(1);

        public static readonly PortId TriStateOutput = new PortId(2);

        public static ComponentSignature TriState(int width)
        {
            return Cached(5, width, 0, () => new ComponentSignature(
                new[] { new PortDescriptor(TriStateData, "D", width), new PortDescriptor(TriStateEnable, "EN") },
                new[] { new PortDescriptor(TriStateOutput, "OUT", width) }));
        }

        /// <summary>Segment input names of a seven-segment display, in pin order (pin id = index + 1).</summary>
        public static readonly string[] SegmentNames = { "a", "b", "c", "d", "e", "f", "g", "dp" };

        /// <summary>
        /// Display pins: a seven-segment display has one input per segment (<see cref="BitPin"/>(0..7) = a–g, dp);
        /// every other style has the single input <see cref="Pin"/> of the display width. Displays have no outputs.
        /// </summary>
        public static ComponentSignature Display(DisplayStyle style, int width)
        {
            return Cached(6, width, (int)style, () =>
            {
                if (style != DisplayStyle.SevenSegment)
                {
                    return new ComponentSignature(new[] { new PortDescriptor(Pin, "IN", width) }, null);
                }

                var segments = new PortDescriptor[SegmentNames.Length];
                for (int i = 0; i < segments.Length; i++)
                {
                    segments[i] = new PortDescriptor(BitPin(i), SegmentNames[i]);
                }

                return new ComponentSignature(segments, null);
            });
        }

        /// <summary>The signature of any utility reference, or null for primitives and custom gates.</summary>
        public static ComponentSignature ForUtility(ComponentRef component)
        {
            switch (component.Kind)
            {
                case ComponentKind.InputBoundary:
                    return InputOf(component.Width);
                case ComponentKind.OutputBoundary:
                    return OutputOf(component.Width);
                case ComponentKind.Clock:
                    return Clock;
                case ComponentKind.Splitter:
                    return Splitter(component.Width);
                case ComponentKind.Merger:
                    return Merger(component.Width);
                case ComponentKind.BusJoin:
                    return BusJoin(component.Width, component.Arity);
                case ComponentKind.TriState:
                    return TriState(component.Width);
                case ComponentKind.Display:
                    return Display(component.DisplayStyle, component.Width);
                default:
                    return null;
            }
        }

        private static ComponentSignature Cached(int kind, int width, int arity, System.Func<ComponentSignature> create)
        {
            _cache ??= new Dictionary<int, ComponentSignature>();
            int key = (kind << 16) | (width << 8) | arity;
            if (!_cache.TryGetValue(key, out ComponentSignature signature))
            {
                signature = create();
                _cache.Add(key, signature);
            }

            return signature;
        }
    }
}
