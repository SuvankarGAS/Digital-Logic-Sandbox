using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;

namespace DigitalLogicSandbox.Core.Circuit
{
    public enum CircuitChangeKind : byte
    {
        NodeAdded,
        NodeRemoved,
        NodeMoved,
        NodeRotated,
        NodeLabelChanged,
        Connected,
        Disconnected,
        InterfacePortAdded,
        InterfacePortRemoved,
        InterfacePortRenamed,
        InterfacePortMoved,
        PropertiesChanged,
    }

    /// <summary>
    /// Allocation-free notification describing a single model mutation. Views, simulation and the inspector
    /// subscribe to <see cref="CircuitDefinition.Changed"/> and update only what is affected.
    /// </summary>
    public readonly struct CircuitChange
    {
        public CircuitChange(CircuitChangeKind kind, NodeId node = default, PortId port = default,
            ConnectionDefinition connection = default)
        {
            Kind = kind;
            Node = node;
            Port = port;
            Connection = connection;
        }

        public CircuitChangeKind Kind { get; }

        /// <summary>Affected node for node changes (and the boundary node for interface port changes).</summary>
        public NodeId Node { get; }

        /// <summary>Affected interface port for interface changes.</summary>
        public PortId Port { get; }

        /// <summary>Affected connection for Connected/Disconnected.</summary>
        public ConnectionDefinition Connection { get; }

        public override string ToString()
        {
            return Kind + " node=" + Node + " port=" + Port + " conn=" + Connection;
        }
    }
}
