using System;
using DigitalLogicSandbox.Commands.UndoRedo;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Nodes;

namespace DigitalLogicSandbox.Commands.Circuit
{
    /// <summary>Moves and/or rotates a set of nodes between two recorded states.</summary>
    public sealed class TransformNodesCommand : ICommand
    {
        private readonly CircuitDefinition _circuit;
        private readonly NodeId[] _nodes;
        private readonly CircuitPoint[] _fromPositions;
        private readonly CircuitPoint[] _toPositions;
        private readonly Rotation[] _fromRotations;
        private readonly Rotation[] _toRotations;

        public TransformNodesCommand(string description, CircuitDefinition circuit, NodeId[] nodes,
            CircuitPoint[] fromPositions, CircuitPoint[] toPositions, Rotation[] fromRotations, Rotation[] toRotations)
        {
            _circuit = circuit ?? throw new ArgumentNullException(nameof(circuit));
            _nodes = (NodeId[])nodes.Clone();
            _fromPositions = (CircuitPoint[])fromPositions.Clone();
            _toPositions = (CircuitPoint[])toPositions.Clone();
            _fromRotations = (Rotation[])fromRotations.Clone();
            _toRotations = (Rotation[])toRotations.Clone();
            if (_fromPositions.Length != _nodes.Length || _toPositions.Length != _nodes.Length ||
                _fromRotations.Length != _nodes.Length || _toRotations.Length != _nodes.Length)
            {
                throw new ArgumentException("All arrays must have one entry per node.");
            }

            Description = description;
        }

        public string Description { get; }

        /// <summary>Captures the current transforms of <paramref name="nodes"/> as the "from" state.</summary>
        public static void CaptureTransforms(CircuitDefinition circuit, NodeId[] nodes, out CircuitPoint[] positions,
            out Rotation[] rotations)
        {
            positions = new CircuitPoint[nodes.Length];
            rotations = new Rotation[nodes.Length];
            for (int i = 0; i < nodes.Length; i++)
            {
                NodeDefinition node = circuit.GetNode(nodes[i]);
                positions[i] = node.Position;
                rotations[i] = node.Rotation;
            }
        }

        public void Execute()
        {
            Apply(_toPositions, _toRotations);
        }

        public void Undo()
        {
            Apply(_fromPositions, _fromRotations);
        }

        private void Apply(CircuitPoint[] positions, Rotation[] rotations)
        {
            for (int i = 0; i < _nodes.Length; i++)
            {
                if (!_circuit.ContainsNode(_nodes[i]))
                {
                    continue;
                }

                _circuit.MoveNode(_nodes[i], positions[i]);
                _circuit.RotateNode(_nodes[i], rotations[i]);
            }
        }
    }
}
