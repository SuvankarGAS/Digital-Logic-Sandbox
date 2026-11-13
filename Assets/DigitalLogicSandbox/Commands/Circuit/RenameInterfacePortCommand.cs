using System;
using DigitalLogicSandbox.Commands.UndoRedo;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Ports;

namespace DigitalLogicSandbox.Commands.Circuit
{
    /// <summary>Renames an external input/output of a circuit (the label shown on its boundary node).</summary>
    public sealed class RenameInterfacePortCommand : ICommand
    {
        private readonly CircuitDefinition _circuit;
        private readonly PortId _port;
        private readonly string _newName;
        private string _oldName;

        public RenameInterfacePortCommand(CircuitDefinition circuit, PortId port, string newName)
        {
            _circuit = circuit ?? throw new ArgumentNullException(nameof(circuit));
            _port = port;
            _newName = newName ?? string.Empty;
            Description = "Rename port to '" + _newName + "'";
        }

        public string Description { get; }

        public void Execute()
        {
            if (!_circuit.TryGetInterfacePort(_port, out InterfacePort port))
            {
                throw new InvalidOperationException("Port " + _port + " does not exist in circuit '" + _circuit.Name + "'.");
            }

            _oldName = port.Name;
            _circuit.RenameInterfacePort(_port, _newName);
        }

        public void Undo()
        {
            _circuit.RenameInterfacePort(_port, _oldName);
        }
    }

    /// <summary>Moves an external port to another position in the interface order.</summary>
    public sealed class MoveInterfacePortCommand : ICommand
    {
        private readonly CircuitDefinition _circuit;
        private readonly PortId _port;
        private readonly int _newIndex;
        private int _oldIndex;

        public MoveInterfacePortCommand(CircuitDefinition circuit, PortId port, int newIndex)
        {
            _circuit = circuit ?? throw new ArgumentNullException(nameof(circuit));
            _port = port;
            _newIndex = newIndex;
            Description = "Reorder port";
        }

        public string Description { get; }

        public void Execute()
        {
            _oldIndex = IndexOf(_circuit, _port);
            if (_oldIndex < 0)
            {
                throw new InvalidOperationException("Port " + _port + " does not exist in circuit '" + _circuit.Name + "'.");
            }

            _circuit.MoveInterfacePort(_port, _newIndex);
        }

        public void Undo()
        {
            _circuit.MoveInterfacePort(_port, _oldIndex);
        }

        private static int IndexOf(CircuitDefinition circuit, PortId port)
        {
            int index = circuit.IndexOfInput(port);
            return index >= 0 ? index : circuit.IndexOfOutput(port);
        }
    }

    /// <summary>Changes the circuit's name, description and category in one undoable step.</summary>
    public sealed class SetCircuitPropertiesCommand : ICommand
    {
        private readonly CircuitDefinition _circuit;
        private readonly string _name;
        private readonly string _description;
        private readonly string _category;
        private string _oldName;
        private string _oldDescription;
        private string _oldCategory;

        public SetCircuitPropertiesCommand(CircuitDefinition circuit, string name, string description, string category)
        {
            _circuit = circuit ?? throw new ArgumentNullException(nameof(circuit));
            _name = name ?? string.Empty;
            _description = description ?? string.Empty;
            _category = category ?? string.Empty;
            Description = "Change circuit properties";
        }

        public string Description { get; }

        public void Execute()
        {
            _oldName = _circuit.Name;
            _oldDescription = _circuit.Description;
            _oldCategory = _circuit.Category;
            _circuit.Name = _name;
            _circuit.Description = _description;
            _circuit.Category = _category;
        }

        public void Undo()
        {
            _circuit.Name = _oldName;
            _circuit.Description = _oldDescription;
            _circuit.Category = _oldCategory;
        }
    }
}
