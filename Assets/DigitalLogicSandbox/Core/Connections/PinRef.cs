using System;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;

namespace DigitalLogicSandbox.Core.Connections
{
    /// <summary>Addresses one pin of one node within a circuit.</summary>
    public readonly struct PinRef : IEquatable<PinRef>
    {
        public PinRef(NodeId node, PortId port)
        {
            Node = node;
            Port = port;
        }

        public NodeId Node { get; }

        public PortId Port { get; }

        public bool Equals(PinRef other)
        {
            return Node == other.Node && Port == other.Port;
        }

        public override bool Equals(object obj)
        {
            return obj is PinRef other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Node.Value * 397) ^ Port.Value;
            }
        }

        public override string ToString()
        {
            return Node + "." + Port;
        }

        public static bool operator ==(PinRef left, PinRef right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(PinRef left, PinRef right)
        {
            return !left.Equals(right);
        }
    }
}
