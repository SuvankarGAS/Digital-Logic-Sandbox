using System;

namespace DigitalLogicSandbox.Core.Nodes
{
    /// <summary>Identity of a node, unique within its owning circuit definition. Zero means "none".</summary>
    public readonly struct NodeId : IEquatable<NodeId>, IComparable<NodeId>
    {
        public static readonly NodeId None = default;

        public NodeId(int value)
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Node ids are positive.");
            }

            Value = value;
        }

        public int Value { get; }

        public bool IsValid => Value > 0;

        public bool Equals(NodeId other)
        {
            return Value == other.Value;
        }

        public override bool Equals(object obj)
        {
            return obj is NodeId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Value;
        }

        public int CompareTo(NodeId other)
        {
            return Value.CompareTo(other.Value);
        }

        public override string ToString()
        {
            return IsValid ? "N" + Value : "N<none>";
        }

        public static bool operator ==(NodeId left, NodeId right)
        {
            return left.Value == right.Value;
        }

        public static bool operator !=(NodeId left, NodeId right)
        {
            return left.Value != right.Value;
        }
    }
}
