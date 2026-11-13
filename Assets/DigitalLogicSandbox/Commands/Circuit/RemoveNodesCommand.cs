using System;
using System.Collections.Generic;
using DigitalLogicSandbox.Commands.UndoRedo;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;

namespace DigitalLogicSandbox.Commands.Circuit
{
    /// <summary>
    /// Deletes nodes and optionally standalone connections. Undo restores nodes with their original ids, interface
    /// ports at their original positions, and every removed connection.
    /// </summary>
    public sealed class RemoveNodesCommand : ICommand
    {
        private readonly CircuitDefinition _circuit;
        private readonly List<NodeId> _nodes;
        private readonly List<PinRef> _connectionsToRemove;
        private readonly List<NodeSnapshot> _snapshots = new List<NodeSnapshot>();
        private readonly List<ConnectionDefinition> _removedConnections = new List<ConnectionDefinition>();

        public RemoveNodesCommand(CircuitDefinition circuit, IEnumerable<NodeId> nodes,
            IEnumerable<PinRef> connectionDestinations = null)
        {
            _circuit = circuit ?? throw new ArgumentNullException(nameof(circuit));
            _nodes = new List<NodeId>(nodes ?? Array.Empty<NodeId>());
            _connectionsToRemove = new List<PinRef>(connectionDestinations ?? Array.Empty<PinRef>());
            int count = _nodes.Count + _connectionsToRemove.Count;
            Description = count == 1
                ? (_nodes.Count == 1 ? "Delete node" : "Delete wire")
                : "Delete " + count + " items";
        }

        public string Description { get; }

        public void Execute()
        {
            _snapshots.Clear();
            _removedConnections.Clear();
            var removing = new HashSet<NodeId>(_nodes);

            IReadOnlyList<ConnectionDefinition> connections = _circuit.Connections;
            for (int i = 0; i < connections.Count; i++)
            {
                ConnectionDefinition connection = connections[i];
                if (removing.Contains(connection.Source.Node) || removing.Contains(connection.Destination.Node) ||
                    _connectionsToRemove.Contains(connection.Destination))
                {
                    _removedConnections.Add(connection);
                }
            }

            for (int i = 0; i < _nodes.Count; i++)
            {
                if (_circuit.ContainsNode(_nodes[i]))
                {
                    _snapshots.Add(NodeSnapshot.Capture(_circuit, _nodes[i]));
                }
            }

            // Restoring boundaries in ascending interface order puts every port back at its original index.
            _snapshots.Sort((a, b) => a.PortIndex.CompareTo(b.PortIndex));

            for (int i = 0; i < _removedConnections.Count; i++)
            {
                _circuit.Disconnect(_removedConnections[i].Destination);
            }

            for (int i = 0; i < _snapshots.Count; i++)
            {
                _circuit.RemoveNode(_snapshots[i].Id);
            }
        }

        public void Undo()
        {
            for (int i = 0; i < _snapshots.Count; i++)
            {
                _snapshots[i].Restore(_circuit);
            }

            for (int i = 0; i < _removedConnections.Count; i++)
            {
                _circuit.Connect(_removedConnections[i].Source, _removedConnections[i].Destination);
            }
        }
    }
}
