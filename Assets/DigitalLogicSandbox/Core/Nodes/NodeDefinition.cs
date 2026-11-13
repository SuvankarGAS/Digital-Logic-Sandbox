using DigitalLogicSandbox.Core.Ports;

namespace DigitalLogicSandbox.Core.Nodes
{
    /// <summary>
    /// A placed component inside a circuit definition. Mutated only through its owning
    /// <see cref="Circuit.CircuitDefinition"/>, which keeps indexes consistent and raises change events.
    /// </summary>
    public sealed class NodeDefinition
    {
        internal NodeDefinition(NodeId id, ComponentRef component, CircuitPoint position, Rotation rotation,
            PortId interfacePort, string label)
        {
            Id = id;
            Component = component;
            Position = position;
            Rotation = rotation;
            InterfacePort = interfacePort;
            Label = label ?? string.Empty;
        }

        public NodeId Id { get; }

        public ComponentRef Component { get; }

        public CircuitPoint Position { get; internal set; }

        public Rotation Rotation { get; internal set; }

        /// <summary>Optional user label (node metadata). Empty when unset.</summary>
        public string Label { get; internal set; }

        /// <summary>For boundary nodes: the interface port this node implements. Meaningless otherwise.</summary>
        public PortId InterfacePort { get; }

        public bool IsBoundary => Component.IsBoundary;

        internal NodeDefinition Clone()
        {
            return new NodeDefinition(Id, Component, Position, Rotation, InterfacePort, Label);
        }

        public override string ToString()
        {
            return Id + ":" + Component;
        }
    }
}
