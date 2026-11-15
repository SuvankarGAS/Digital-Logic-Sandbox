using System;
using System.Collections.Generic;
using DigitalLogicSandbox.Commands.UndoRedo;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Validation;

namespace DigitalLogicSandbox.Commands.Circuit
{
    /// <summary>
    /// Swaps the component of a placed node for another of the same kind (a different bus width, bus-join driver
    /// count, display style or width), keeping its id, position, rotation, label and interface port. Connections
    /// whose pins still exist and still fit are kept; the others are dropped and remembered, so undo restores the
    /// node and every original connection exactly.
    /// </summary>
    public sealed class ChangeComponentCommand : ICommand
    {
        private readonly CircuitDefinition _circuit;
        private readonly IComponentResolver _resolver;
        private readonly NodeId _node;
        private readonly ComponentRef _component;
        private readonly List<ConnectionDefinition> _original = new List<ConnectionDefinition>();
        private readonly List<ConnectionDefinition> _kept = new List<ConnectionDefinition>();
        private NodeSnapshot _before;

        public ChangeComponentCommand(CircuitDefinition circuit, IComponentResolver resolver, NodeId node,
            ComponentRef component, string description = null)
        {
            _circuit = circuit ?? throw new ArgumentNullException(nameof(circuit));
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            if (!circuit.TryGetNode(node, out NodeDefinition existing))
            {
                throw new ArgumentException("Node " + node + " is not in the circuit.", nameof(node));
            }

            if (existing.Component.Kind != component.Kind)
            {
                throw new ArgumentException("A node can only change to a component of the same kind.",
                    nameof(component));
            }

            _node = node;
            _component = component;
            Description = description ?? "Change component";
        }

        public string Description { get; }

        /// <summary>Connections that could not be kept by the last execution.</summary>
        public int DroppedConnections => _original.Count - _kept.Count;

        public void Execute()
        {
            _before = NodeSnapshot.Capture(_circuit, _node);
            _original.Clear();
            _kept.Clear();
            _circuit.GetConnectionsFrom(_node, _original);
            _circuit.GetConnectionsTo(_node, _original);
            RemoveDuplicateSelfConnections(_original);

            _circuit.RemoveNode(_node); // removes the node's connections too
            _before.WithComponent(_component).Restore(_circuit);
            for (int i = 0; i < _original.Count; i++)
            {
                ConnectionDefinition connection = _original[i];
                // Full rules: the pin must still exist on the new component and the widths must still match.
                if (ConnectionRules.TryConnect(_circuit, _resolver, connection.Source, connection.Destination) ==
                    ConnectionError.None)
                {
                    _kept.Add(connection);
                }
            }
        }

        public void Undo()
        {
            _circuit.RemoveNode(_node);
            _before.Restore(_circuit);
            for (int i = 0; i < _original.Count; i++)
            {
                _circuit.Connect(_original[i].Source, _original[i].Destination);
            }
        }

        /// <summary>A connection from the node to itself is reported both as outgoing and incoming.</summary>
        private static void RemoveDuplicateSelfConnections(List<ConnectionDefinition> connections)
        {
            for (int i = connections.Count - 1; i > 0; i--)
            {
                for (int j = 0; j < i; j++)
                {
                    if (connections[j].Destination == connections[i].Destination)
                    {
                        connections.RemoveAt(i);
                        break;
                    }
                }
            }
        }
    }
}
