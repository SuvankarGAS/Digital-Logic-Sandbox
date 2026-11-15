using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;

namespace DigitalLogicSandbox.Commands.Circuit
{
    /// <summary>
    /// Everything needed to re-create a node exactly, including its id and, for boundaries, its interface port id,
    /// name and position in the interface order.
    /// </summary>
    public readonly struct NodeSnapshot
    {
        private NodeSnapshot(NodeId id, ComponentRef component, CircuitPoint position, Rotation rotation, string label,
            PortId port, PortDirection direction, string portName, int portIndex)
        {
            Id = id;
            Component = component;
            Position = position;
            Rotation = rotation;
            Label = label;
            Port = port;
            Direction = direction;
            PortName = portName;
            PortIndex = portIndex;
        }

        public NodeId Id { get; }

        public ComponentRef Component { get; }

        public CircuitPoint Position { get; }

        public Rotation Rotation { get; }

        public string Label { get; }

        public PortId Port { get; }

        public PortDirection Direction { get; }

        public string PortName { get; }

        /// <summary>Index in the interface order (boundaries only).</summary>
        public int PortIndex { get; }

        public static NodeSnapshot Capture(CircuitDefinition circuit, NodeId id)
        {
            NodeDefinition node = circuit.GetNode(id);
            if (!node.IsBoundary)
            {
                return new NodeSnapshot(node.Id, node.Component, node.Position, node.Rotation, node.Label, default,
                    PortDirection.Input, null, -1);
            }

            bool isInput = node.Component.Kind == ComponentKind.InputBoundary;
            circuit.TryGetInterfacePort(node.InterfacePort, out InterfacePort port);
            int index = isInput ? circuit.IndexOfInput(node.InterfacePort) : circuit.IndexOfOutput(node.InterfacePort);
            return new NodeSnapshot(node.Id, node.Component, node.Position, node.Rotation, node.Label, node.InterfacePort,
                isInput ? PortDirection.Input : PortDirection.Output, port?.Name, index);
        }

        /// <summary>
        /// The same node (id, position, rotation, label, interface port) with another component of the same kind,
        /// e.g. a different width or display style.
        /// </summary>
        public NodeSnapshot WithComponent(ComponentRef component)
        {
            return new NodeSnapshot(Id, component, Position, Rotation, Label, Port, Direction, PortName, PortIndex);
        }

        public void Restore(CircuitDefinition circuit)
        {
            if (Component.IsBoundary)
            {
                circuit.RestoreBoundary(Id, Port, Direction, PortName, Position, Rotation, Label, PortIndex,
                    Component.Width);
            }
            else
            {
                circuit.RestoreNode(Id, Component, Position, Rotation, Label);
            }
        }
    }
}
