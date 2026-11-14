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
    /// <see cref="Arity"/> (number of drivers a bus join accepts).
    /// </para>
    /// </summary>
    public readonly struct ComponentRef : IEquatable<ComponentRef>
    {
        /// <summary>Most drivers a bus join accepts.</summary>
        public const int MaxBusJoinInputs = 8;

        // Stored as "value - 1" so default(ComponentRef) means width 1 / arity 0.
        private readonly byte _widthMinusOne;
        private readonly byte _arity;

        private ComponentRef(ComponentKind kind, string primitiveId, DefinitionRef definition, int width = 1, int arity = 0)
        {
            Kind = kind;
            PrimitiveId = primitiveId;
            Definition = definition;
            _widthMinusOne = (byte)(Math.Max(1, Math.Min(PortDescriptor.MaxWidth, width)) - 1);
            _arity = (byte)Math.Max(0, Math.Min(MaxBusJoinInputs, arity));
        }

        public ComponentKind Kind { get; }

        /// <summary>Component id of a primitive (e.g. "builtin.and"); null for other kinds.</summary>
        public string PrimitiveId { get; }

        /// <summary>Referenced definition version for <see cref="ComponentKind.Custom"/>; default otherwise.</summary>
        public DefinitionRef Definition { get; }

        /// <summary>Bit width of a parameterised utility (1 for everything else).</summary>
        public int Width => _widthMinusOne + 1;

        /// <summary>Number of driver inputs of a bus join (0 for everything else).</summary>
        public int Arity => _arity;

        public bool IsBoundary => Kind == ComponentKind.InputBoundary || Kind == ComponentKind.OutputBoundary;

        /// <summary>Splitter, merger or bus join: pure wiring, compiled away (no gates, no delay).</summary>
        public bool IsWiring => Kind == ComponentKind.Splitter || Kind == ComponentKind.Merger || Kind == ComponentKind.BusJoin;

        /// <summary>
        /// Circuit utilities (boundaries, clock, bus wiring, tri-state drivers): not logic gates from the gate
        /// library, no internal circuit to inspect, no truth table entry.
        /// </summary>
        public bool IsUtility => IsBoundary || Kind == ComponentKind.Clock || IsWiring || Kind == ComponentKind.TriState;

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
        /// Rebuilds a utility reference from persisted parameters (lenient: out-of-range values are clamped so a
        /// damaged file still loads).
        /// </summary>
        public static ComponentRef Utility(ComponentKind kind, int width, int arity = 0)
        {
            switch (kind)
            {
                case ComponentKind.InputBoundary:
                case ComponentKind.OutputBoundary:
                case ComponentKind.Clock:
                case ComponentKind.TriState:
                    return new ComponentRef(kind, null, default, kind == ComponentKind.Clock ? 1 : width);
                case ComponentKind.Splitter:
                case ComponentKind.Merger:
                    return new ComponentRef(kind, null, default, Math.Max(2, width));
                case ComponentKind.BusJoin:
                    return new ComponentRef(kind, null, default, width, Math.Max(2, arity));
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
                hash = (hash * 397) ^ (_widthMinusOne | (_arity << 8));
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
