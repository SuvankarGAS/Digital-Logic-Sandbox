using System;
using DigitalLogicSandbox.Commands.UndoRedo;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Nodes;

namespace DigitalLogicSandbox.Commands.Circuit
{
    /// <summary>Changes a node's free-text label (for example the mnemonic shown on a program constant).</summary>
    public sealed class SetNodeLabelCommand : ICommand
    {
        private readonly CircuitDefinition _circuit;
        private readonly NodeId _node;
        private readonly string _label;
        private string _oldLabel;

        public SetNodeLabelCommand(CircuitDefinition circuit, NodeId node, string label)
        {
            _circuit = circuit ?? throw new ArgumentNullException(nameof(circuit));
            _node = node;
            _label = label ?? string.Empty;
            Description = "Change label";
        }

        public string Description { get; }

        public void Execute()
        {
            _oldLabel = _circuit.GetNode(_node).Label;
            _circuit.SetNodeLabel(_node, _label);
        }

        public void Undo()
        {
            _circuit.SetNodeLabel(_node, _oldLabel);
        }
    }
}
