using System;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Ports;

namespace DigitalLogicSandbox.Core.Nodes
{
    /// <summary>
    /// What a node instantiates: a primitive (by component id), a specific version of a packaged definition,
    /// or a circuit utility (boundaries, clock, bus wiring). Behaviour is always resolved through this reference,
    /// never through a name.
    /// <para>
    /// Parameterised utilities carry their parameters here, so a component's signature is a pure function of its
    /// reference: <see cref="Width"/> (bits of a bus boundary, splitter, merger or bus join) and
    /// <see cref="Arity"/> (number of drivers a bus join accepts). Displays also carry a presentational
    /// <see cref="LedColor"/>, which never affects simulation. Clocks carry a <see cref="ClockDivider"/> (stored as
    /// its base-2 exponent): clocks with the same divider share one clock domain.
    /// </para>
    /// </summary>
    public readonly struct ComponentRef : IEquatable<ComponentRef>
    {
        /// <summary>Most drivers a bus join accepts.</summary>
        public const int MaxBusJoinInputs = 8;

        /// <summary>Largest clock divider exponent (÷2^6 = ÷64).</summary>
        public const int MaxClockExponent = 6;

        /// <summary>Widest constant (its value is stored in one byte).</summary>
        public const int MaxConstantWidth = 8;

        // Stored as "value - 1" so default(ComponentRef) means width 1 / arity 0.
        private readonly byte _widthMinusOne;
        private readonly byte _arity;
        private readonly byte _color;

        private ComponentRef(ComponentKind kind, string primitiveId, DefinitionRef definition, int width = 1, int arity = 0,
            int color = 0)
        {
            _color = (byte)(LedColors.IsValid(color) ? color : LedColors.Signal);
            Kind = kind;
            PrimitiveId = primitiveId;
            Definition = definition;
            _widthMinusOne = (byte)(Math.Max(1, Math.Min(PortDescriptor.MaxWidth, width)) - 1);
            _arity = (byte)Math.Max(0, Math.Min(byte.MaxValue, arity));
        }

        public ComponentKind Kind { get; }

        /// <summary>Component id of a primitive (e.g. "builtin.and"); null for other kinds.</summary>
        public string PrimitiveId { get; }

        /// <summary>Referenced definition version for <see cref="ComponentKind.Custom"/>; default otherwise.</summary>
        public DefinitionRef Definition { get; }

        /// <summary>Bit width of a parameterised utility (1 for everything else).</summary>
        public int Width => _widthMinusOne + 1;

        /// <summary>Number of driver inputs of a bus join (0 for everything else).</summary>
        public int Arity => Kind == ComponentKind.BusJoin ? _arity : 0;

        /// <summary>What a <see cref="ComponentKind.Display"/> node shows (LED for every other kind).</summary>
        public DisplayStyle DisplayStyle => Kind == ComponentKind.Display ? (DisplayStyle)_arity : DisplayStyle.Led;

        /// <summary>Lit colour of a display, any style (see <see cref="LedColors"/>); 0 for everything else.</summary>
        public int LedColor => Kind == ComponentKind.Display ? _color : 0;

        /// <summary>Base-2 exponent of a clock's divider (0 = ÷1); 0 for every other kind.</summary>
        public int ClockExponent => Kind == ComponentKind.Clock ? _arity : 0;

        /// <summary>How much slower than the base clock frequency a clock runs (1, 2, 4 … 64); 1 for other kinds.</summary>
        public int ClockDivider => 1 << ClockExponent;

        /// <summary>Fixed value of a <see cref="ComponentKind.Constant"/> (bit 0 = least significant); 0 for other kinds.</summary>
        public int ConstantValue => Kind == ComponentKind.Constant ? _arity : 0;

        /// <summary>
        /// A source with a fixed <paramref name="value"/> of <paramref name="width"/> bits (1 to
        /// <see cref="MaxConstantWidth"/>). Bits above the width are dropped.
        /// </summary>
        public static ComponentRef Constant(int width, int value)
        {
            if (width < 1 || width > MaxConstantWidth)
            {
                throw new ArgumentOutOfRangeException(nameof(width), width,
                    "Constant width must be between 1 and " + MaxConstantWidth + ".");
            }

            return new ComponentRef(ComponentKind.Constant, null, default, width, value & ConstantMask(width));
        }

        /// <summary>The same constant with another value (masked to its width).</summary>
        public ComponentRef WithConstantValue(int value)
        {
            if (Kind != ComponentKind.Constant)
            {
                throw new InvalidOperationException("Only constants have a value.");
            }

            return Constant(Width, value);
        }

        /// <summary>All bits set for a constant of <paramref name="width"/> bits.</summary>
        public static int ConstantMask(int width)
        {
            return (1 << Math.Max(1, Math.Min(MaxConstantWidth, width))) - 1;
        }

        /// <summary>True for a power of two between 1 and 2^<see cref="MaxClockExponent"/>.</summary>
        public static bool IsValidClockDivider(int divider)
        {
            return divider >= 1 && divider <= 1 << MaxClockExponent && (divider & (divider - 1)) == 0;
        }

        /// <summary>A clock running at the base frequency divided by <paramref name="divider"/>.</summary>
        public static ComponentRef ClockDivided(int divider)
        {
            return new ComponentRef(ComponentKind.Clock, null, default, 1, ExponentOf(divider));
        }

        /// <summary>The same clock with another divider.</summary>
        public ComponentRef WithClockDivider(int divider)
        {
            if (Kind != ComponentKind.Clock)
            {
                throw new InvalidOperationException("Only clocks have a divider.");
            }

            return ClockDivided(divider);
        }

        private static int ExponentOf(int divider)
        {
            if (!IsValidClockDivider(divider))
            {
                throw new ArgumentOutOfRangeException(nameof(divider), divider,
                    "Clock divider must be a power of two between 1 and " + (1 << MaxClockExponent) + ".");
            }

            int exponent = 0;
            while (1 << exponent < divider)
            {
                exponent++;
            }

            return exponent;
        }

        /// <summary>The same display lit in another colour.</summary>
        public ComponentRef WithLedColor(int color)
        {
            if (Kind != ComponentKind.Display)
            {
                throw new InvalidOperationException("Only displays have a colour.");
            }

            if (!LedColors.IsValid(color))
            {
                throw new ArgumentOutOfRangeException(nameof(color), color,
                    "LED colour must be between 0 and " + (LedColors.Count - 1) + ".");
            }

            return new ComponentRef(Kind, null, default, Width, _arity, color);
        }

        public bool IsBoundary => Kind == ComponentKind.InputBoundary || Kind == ComponentKind.OutputBoundary;

        /// <summary>Splitter, merger or bus join: pure wiring, compiled away (no gates, no delay).</summary>
        public bool IsWiring => Kind == ComponentKind.Splitter || Kind == ComponentKind.Merger || Kind == ComponentKind.BusJoin;

        /// <summary>
        /// Circuit utilities (boundaries, clock, bus wiring, tri-state drivers): not logic gates from the gate
        /// library, no internal circuit to inspect, no truth table entry.
        /// </summary>
        public bool IsUtility => IsBoundary || Kind == ComponentKind.Clock || IsWiring || Kind == ComponentKind.TriState ||
            Kind == ComponentKind.Display || Kind == ComponentKind.Constant;

        public static ComponentRef Primitive(string primitiveId)
        {
            if (string.IsNullOrWhiteSpace(primitiveId))
            {
                throw new ArgumentException("Primitive id must not be empty.", nameof(primitiveId));
            }

            return new ComponentRef(ComponentKind.Primitive, primitiveId, default);
        }

        public static ComponentRef Custom(DefinitionRef definition)
        {
            if (!definition.IsValid)
            {
                throw new ArgumentException("Custom component requires a valid definition reference.", nameof(definition));
            }

            return new ComponentRef(ComponentKind.Custom, null, definition);
        }

        public static ComponentRef InputBoundary => new ComponentRef(ComponentKind.InputBoundary, null, default);

        public static ComponentRef OutputBoundary => new ComponentRef(ComponentKind.OutputBoundary, null, default);

        public static ComponentRef Clock => new ComponentRef(ComponentKind.Clock, null, default);

        /// <summary>Input boundary carrying <paramref name="width"/> bits (a bus input when width &gt; 1).</summary>
        public static ComponentRef InputBus(int width)
        {
            return new ComponentRef(ComponentKind.InputBoundary, null, default, CheckWidth(width, 1));
        }

        /// <summary>Output boundary observing <paramref name="width"/> bits (a bus output when width &gt; 1).</summary>
        public static ComponentRef OutputBus(int width)
        {
            return new ComponentRef(ComponentKind.OutputBoundary, null, default, CheckWidth(width, 1));
        }

        /// <summary>Splits a <paramref name="width"/>-bit bus into single bits (bit 0 is the least significant).</summary>
        public static ComponentRef Splitter(int width)
        {
            return new ComponentRef(ComponentKind.Splitter, null, default, CheckWidth(width, 2));
        }

        /// <summary>Merges <paramref name="width"/> single bits into one bus.</summary>
        public static ComponentRef Merger(int width)
        {
            return new ComponentRef(ComponentKind.Merger, null, default, CheckWidth(width, 2));
        }

        /// <summary>Joins <paramref name="inputs"/> drivers of <paramref name="width"/> bits onto one shared bus.</summary>
        public static ComponentRef BusJoin(int width, int inputs)
        {
            if (inputs < 2 || inputs > MaxBusJoinInputs)
            {
                throw new ArgumentOutOfRangeException(nameof(inputs), inputs,
                    "A bus join takes 2 to " + MaxBusJoinInputs + " inputs.");
            }

            return new ComponentRef(ComponentKind.BusJoin, null, default, CheckWidth(width, 1), inputs);
        }

        /// <summary>Tri-state driver for a <paramref name="width"/>-bit bus with one enable input.</summary>
        public static ComponentRef TriState(int width = 1)
        {
            return new ComponentRef(ComponentKind.TriState, null, default, CheckWidth(width, 1));
        }

        /// <summary>
        /// A visual display. LED and seven-segment displays have fixed single-bit inputs (width 1); the bus styles
        /// read one <paramref name="width"/>-bit input (signed decimal needs at least 2 bits).
        /// </summary>
        public static ComponentRef Display(DisplayStyle style, int width = 1)
        {
            if (style > DisplayStyles.Last)
            {
                throw new ArgumentOutOfRangeException(nameof(style), style, "Unknown display style.");
            }

            int min = DisplayStyles.MinWidth(style);
            int max = DisplayStyles.MaxWidth(style);
            if (width < min || width > max)
            {
                throw new ArgumentOutOfRangeException(nameof(width), width,
                    DisplayStyles.DisplayName(style) + " width must be between " + min + " and " + max + ".");
            }

            return new ComponentRef(ComponentKind.Display, null, default, width, (int)style);
        }

        /// <summary>
        /// Rebuilds a utility reference from persisted parameters (lenient: out-of-range values are clamped so a
        /// damaged file still loads).
        /// </summary>
        public static ComponentRef Utility(ComponentKind kind, int width, int arity = 0, int color = 0)
        {
            switch (kind)
            {
                case ComponentKind.InputBoundary:
                case ComponentKind.OutputBoundary:
                case ComponentKind.TriState:
                    return new ComponentRef(kind, null, default, width);
                case ComponentKind.Clock:
                    // Arity carries the divider exponent.
                    return new ComponentRef(kind, null, default, 1, Math.Max(0, Math.Min(MaxClockExponent, arity)));
                case ComponentKind.Splitter:
                case ComponentKind.Merger:
                    return new ComponentRef(kind, null, default, Math.Max(2, width));
                case ComponentKind.BusJoin:
                    return new ComponentRef(kind, null, default, width, Math.Max(2, Math.Min(MaxBusJoinInputs, arity)));
                case ComponentKind.Display:
                {
                    DisplayStyle style = arity >= 0 && arity <= (int)DisplayStyles.Last ? (DisplayStyle)arity : DisplayStyle.Led;
                    int clamped = Math.Max(DisplayStyles.MinWidth(style), Math.Min(DisplayStyles.MaxWidth(style), width));
                    // Every display style keeps its colour.
                    return new ComponentRef(kind, null, default, clamped, (int)style, color);
                }
                case ComponentKind.Constant:
                {
                    int clamped = Math.Max(1, Math.Min(MaxConstantWidth, width));
                    return new ComponentRef(kind, null, default, clamped, arity & ConstantMask(clamped));
                }
                default:
                    throw new ArgumentException("Not a utility kind: " + kind + ".", nameof(kind));
            }
        }

        private static int CheckWidth(int width, int minimum)
        {
            if (width < minimum || width > PortDescriptor.MaxWidth)
            {
                throw new ArgumentOutOfRangeException(nameof(width), width,
                    "Width must be between " + minimum + " and " + PortDescriptor.MaxWidth + ".");
            }

            return width;
        }

        public bool Equals(ComponentRef other)
        {
            return Kind == other.Kind
                && _widthMinusOne == other._widthMinusOne
                && _arity == other._arity
                && _color == other._color
                && string.Equals(PrimitiveId, other.PrimitiveId, StringComparison.Ordinal)
                && Definition.Equals(other.Definition);
        }

        public override bool Equals(object obj)
        {
            return obj is ComponentRef other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Kind;
                hash = (hash * 397) ^ (PrimitiveId == null ? 0 : StringComparer.Ordinal.GetHashCode(PrimitiveId));
                hash = (hash * 397) ^ Definition.GetHashCode();
                hash = (hash * 397) ^ (_widthMinusOne | (_arity << 8) | (_color << 16));
                return hash;
            }
        }

        public override string ToString()
        {
            switch (Kind)
            {
                case ComponentKind.Primitive:
                    return PrimitiveId;
                case ComponentKind.Custom:
                    return "custom:" + Definition;
                case ComponentKind.BusJoin:
                    return "BusJoin[" + Width + "x" + Arity + "]";
                case ComponentKind.Clock:
                    return ClockDivider > 1 ? "Clock/" + ClockDivider : "Clock";
                case ComponentKind.Constant:
                    return "Constant[" + Width + "]=" + ConstantValue;
                case ComponentKind.Display:
                    return "Display:" + DisplayStyles.ToId(DisplayStyle) + (Width > 1 ? "[" + Width + "]" : string.Empty) +
                        (LedColor != 0 ? ":" + LedColors.ToId(LedColor) : string.Empty);
                default:
                    return Width > 1 ? Kind + "[" + Width + "]" : Kind.ToString();
            }
        }

        public static bool operator ==(ComponentRef left, ComponentRef right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(ComponentRef left, ComponentRef right)
        {
            return !left.Equals(right);
        }
    }
}
