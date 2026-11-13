using System;

namespace DigitalLogicSandbox.Core.Ports
{
    /// <summary>
    /// Stable identity of a port on a component. Unique across all of a component's inputs and outputs, and
    /// independent of the port's display order, so reordering or renaming ports never breaks connections.
    /// </summary>
    public readonly struct PortId : IEquatable<PortId>, IComparable<PortId>
    {
        public PortId(int value)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Port ids are non-negative.");
            }

            Value = value;
        }

        public int Value { get; }

        public bool Equals(PortId other)
        {
            return Value == other.Value;
        }

        public override bool Equals(object obj)
        {
            return obj is PortId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Value;
        }

        public int CompareTo(PortId other)
        {
            return Value.CompareTo(other.Value);
        }

        public override string ToString()
        {
            return "P" + Value;
        }

        public static bool operator ==(PortId left, PortId right)
        {
            return left.Value == right.Value;
        }

        public static bool operator !=(PortId left, PortId right)
        {
            return left.Value != right.Value;
        }
    }
}
