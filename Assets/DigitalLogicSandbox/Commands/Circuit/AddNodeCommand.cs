using System;
using DigitalLogicSandbox.Commands.UndoRedo;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Nodes;

namespace DigitalLogicSandbox.Commands.Circuit
{
    /// <summary>Places a gate, custom gate, or boundary (with a new interface port). Redo restores identical ids.</summary>
    public sealed class AddNodeCommand : ICommand
    {
        private readonly CircuitDefinition _circuit;
        private readonly ComponentRef _component;
        private readonly CircuitPoint _position;
        private readonly Rotation _rotation;
        private readonly string _boundaryName;
        private NodeSnapshot _snapshot;
        private bool _hasSnapshot;

        public AddNodeCommand(CircuitDefinition circuit, ComponentRef component, CircuitPoint position,
            string displayName, string boundaryName = null, Rotation rotation = Rotation.R0)
        {
            _circuit = circuit ?? throw new ArgumentNullException(nameof(circuit));
            _component = component;
            _position = position;
            _rotation = rotation;
            _boundaryName = boundaryName;
            Description = "Add " + (displayName ?? component.ToString());
        }

        public string Description { get; }

        /// <summary>The created node (valid after the first Execute).</summary>
        public NodeId CreatedNode => _hasSnapshot ? _snapshot.Id : NodeId.None;

        public void Execute()
        {
            if (_hasSnapshot)
            {
                _snapshot.Restore(_circuit);
                return;
            }

            NodeDefinition node;
            switch (_component.Kind)
            {
                case ComponentKind.InputBoundary:
                    node = _circuit.AddInputBoundary(_boundaryName ?? BoundaryNames.NextInputName(_circuit), _position);
                    break;
                case ComponentKind.OutputBoundary:
                    node = _circuit.AddOutputBoundary(_boundaryName ?? BoundaryNames.NextOutputName(_circuit), _position);
                    break;
                default:
                    node = _circuit.AddNode(_component, _position, _rotation);
                    break;
            }

            if (node.Rotation != _rotation)
            {
                _circuit.RotateNode(node.Id, _rotation);
            }

            _snapshot = NodeSnapshot.Capture(_circuit, node.Id);
            _hasSnapshot = true;
        }

        public void Undo()
        {
            _circuit.RemoveNode(_snapshot.Id);
        }
    }
}
