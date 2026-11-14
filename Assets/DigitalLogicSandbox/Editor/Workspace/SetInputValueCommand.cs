using System;
using DigitalLogicSandbox.Commands.UndoRedo;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Signals;

namespace DigitalLogicSandbox.CircuitEditor.Workspace
{
    /// <summary>
    /// Changes the value of an input switch as an undoable step. Input values are simulation state rather than
    /// circuit structure, so the command acts on the level's <see cref="CircuitSession"/>; the circuit model is
    /// untouched. If the switch no longer exists (e.g. it was deleted by a later step that has since been
    /// undone in a different order) the command does nothing.
    /// </summary>
    public sealed class SetInputValueCommand : ICommand
    {
        private readonly CircuitSession _session;
        private readonly NodeId _boundary;
        private readonly Signal _newValue;
        private Signal _oldValue;

        public SetInputValueCommand(CircuitSession session, NodeId boundary, Signal newValue, string inputName)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _boundary = boundary;
            _newValue = newValue;
            Description = "Set input " + (string.IsNullOrEmpty(inputName) ? boundary.ToString() : "'" + inputName + "'") +
                          " to " + (newValue == Signal.High ? "1" : "0");
        }

        public string Description { get; }

        public void Execute()
        {
            _session.RebuildIfDirty();
            _oldValue = _session.GetInputValue(_boundary);
            _session.SetInput(_boundary, _newValue);
        }

        public void Undo()
        {
            _session.RebuildIfDirty();
            _session.SetInput(_boundary, _oldValue);
        }
    }
}
