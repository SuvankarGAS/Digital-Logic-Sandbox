using System;
using DigitalLogicSandbox.Commands.UndoRedo;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;

namespace DigitalLogicSandbox.Commands.Circuit
{
    /// <summary>Adds a connection. Callers validate with <c>ConnectionRules.Check</c> first.</summary>
    public sealed class ConnectCommand : ICommand
    {
        private readonly CircuitDefinition _circuit;
        private readonly PinRef _source;
        private readonly PinRef _destination;

        public ConnectCommand(CircuitDefinition circuit, PinRef source, PinRef destination)
        {
            _circuit = circuit ?? throw new ArgumentNullException(nameof(circuit));
            _source = source;
            _destination = destination;
        }

        public string Description => "Connect wire";

        public void Execute()
        {
            _circuit.Connect(_source, _destination);
        }

        public void Undo()
        {
            _circuit.Disconnect(_destination);
        }
    }
}
