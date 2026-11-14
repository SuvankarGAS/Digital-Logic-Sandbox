using System;
using System.Collections.Generic;
using DigitalLogicSandbox.Commands.UndoRedo;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;

namespace DigitalLogicSandbox.Commands.Circuit
{
    /// <summary>
    /// Copies nodes (with an offset) and the connections between them. Duplicated boundaries become new interface
    /// ports with fresh unique names. Redo restores identical ids.
    /// </summary>
    public sealed class DuplicateNodesCommand : ICommand
    {
        private readonly CircuitDefinition _circuit;
        private readonly List<NodeId> _sources;
        private readonly float _offsetX;
        private readonly float _offsetY;
        private readonly List<NodeSnapshot> _created = new List<NodeSnapshot>();
        private readonly List<ConnectionDefinition> _createdConnections = new List<ConnectionDefinition>();
        private bool _executedOnce;

        public DuplicateNodesCommand(CircuitDefinition circuit, IEnumerable<NodeId> sources, float offsetX, float offsetY)
        {
            _circuit = circuit ?? throw new ArgumentNullException(nameof(circuit));
            _sources = new List<NodeId>(sources ?? throw new ArgumentNullException(nameof(sources)));
            _offsetX = offsetX;
            _offsetY = offsetY;
            Description = _sources.Count == 1 ? "Duplicate node" : "Duplicate " + _sources.Count + " nodes";
        }

        public string Description { get; }

        /// <summary>Ids of the copies, in the same order as the sources (valid after Execute).</summary>
        public IReadOnlyList<NodeSnapshot> Created => _created;

        public void Execute()
        {
            if (_executedOnce)
            {
                for (int i = 0; i < _created.Count; i++)
                {
                    _created[i].Restore(_circuit);
                }

                for (int i = 0; i < _createdConnections.Count; i++)
                {
                    _circuit.Connect(_createdConnections[i].Source, _createdConnections[i].Destination);
                }

                return;
            }

            var map = new Dictionary<NodeId, NodeId>();
            for (int i = 0; i < _sources.Count; i++)
            {
                if (!_circuit.TryGetNode(_sources[i], out NodeDefinition source))
                {
                    continue;
                }

                CircuitPoint position = source.Position.Offset(_offsetX, _offsetY);
                NodeDefinition copy;
                switch (source.Component.Kind)
                {
                    case ComponentKind.InputBoundary:
                        copy = _circuit.AddInputBoundary(BoundaryNames.NextName(_circuit, PortDirection.Input), position,
                            source.Component.Width);
                        break;
                    case ComponentKind.OutputBoundary:
                        copy = _circuit.AddOutputBoundary(BoundaryNames.NextName(_circuit, PortDirection.Output), position,
                            source.Component.Width);
                        break;
                    default:
                        copy = _circuit.AddNode(source.Component, position, source.Rotation, source.Label);
                        break;
                }

                map[source.Id] = copy.Id;
            }

            IReadOnlyList<ConnectionDefinition> connections = _circuit.Connections;
            var toCreate = new List<ConnectionDefinition>();
            for (int i = 0; i < connections.Count; i++)
            {
                ConnectionDefinition connection = connections[i];
                if (map.TryGetValue(connection.Source.Node, out NodeId newSource) &&
                    map.TryGetValue(connection.Destination.Node, out NodeId newDestination))
                {
                    toCreate.Add(new ConnectionDefinition(new PinRef(newSource, connection.Source.Port),
                        new PinRef(newDestination, connection.Destination.Port)));
                }
            }

            for (int i = 0; i < toCreate.Count; i++)
            {
                _circuit.Connect(toCreate[i].Source, toCreate[i].Destination);
                _createdConnections.Add(toCreate[i]);
            }

            for (int i = 0; i < _sources.Count; i++)
            {
                if (map.TryGetValue(_sources[i], out NodeId created))
                {
                    _created.Add(NodeSnapshot.Capture(_circuit, created));
                }
            }

            _created.Sort((a, b) => a.PortIndex.CompareTo(b.PortIndex));
            _executedOnce = true;
        }

        public void Undo()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
            {
                _circuit.RemoveNode(_created[i].Id);
            }
        }
    }
}
