using System;
using System.Collections.Generic;
using DigitalLogicSandbox.Commands.UndoRedo;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.GateLibrary.Custom;

namespace DigitalLogicSandbox.Commands.Library
{
    /// <summary>Switches custom-gate instances in a circuit to other definition versions (undoable).</summary>
    public sealed class RetargetReferencesCommand : ICommand
    {
        private readonly CircuitDefinition _circuit;
        private readonly Dictionary<DefinitionRef, DefinitionRef> _map;
        private readonly Func<DefinitionRef, ComponentSignature> _signatureOf;
        private readonly List<RetargetRecord> _records = new List<RetargetRecord>();

        public RetargetReferencesCommand(CircuitDefinition circuit, IReadOnlyDictionary<DefinitionRef, DefinitionRef> map,
            Func<DefinitionRef, ComponentSignature> signatureOf)
        {
            _circuit = circuit ?? throw new ArgumentNullException(nameof(circuit));
            _map = new Dictionary<DefinitionRef, DefinitionRef>();
            foreach (KeyValuePair<DefinitionRef, DefinitionRef> pair in map)
            {
                _map.Add(pair.Key, pair.Value);
            }

            _signatureOf = signatureOf ?? throw new ArgumentNullException(nameof(signatureOf));
            Description = "Update gate references";
        }

        public string Description { get; }

        /// <summary>Connections dropped by the last execution (pins that no longer exist).</summary>
        public int DroppedConnections { get; private set; }

        public int RetargetedNodes => _records.Count;

        public void Execute()
        {
            _records.Clear();
            DroppedConnections = ComponentRetargeting.Retarget(_circuit, _map, _signatureOf, _records);
        }

        public void Undo()
        {
            ComponentRetargeting.Revert(_circuit, _records);
            _records.Clear();
        }
    }
}
