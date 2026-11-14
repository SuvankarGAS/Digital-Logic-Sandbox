using System;
using System.Collections.Generic;
using DigitalLogicSandbox.Commands.UndoRedo;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.GateLibrary.Custom;

namespace DigitalLogicSandbox.Commands.Library
{
    /// <summary>
    /// Renames a custom gate: every published version of the definition gets the new display name. Names are
    /// presentation metadata (behaviour never depends on them), so no new version is created and every
    /// reference stays valid.
    /// </summary>
    public sealed class RenameCustomGateCommand : ICommand
    {
        private readonly DefinitionLibrary _library;
        private readonly DefinitionId _id;
        private readonly string _newName;
        private readonly List<CircuitDefinition> _versions = new List<CircuitDefinition>();
        private readonly List<string> _oldNames = new List<string>();

        public RenameCustomGateCommand(DefinitionLibrary library, DefinitionId id, string newName)
        {
            _library = library ?? throw new ArgumentNullException(nameof(library));
            if (string.IsNullOrWhiteSpace(newName))
            {
                throw new ArgumentException("A gate name cannot be empty.", nameof(newName));
            }

            _id = id;
            _newName = newName.Trim();
            Description = "Rename gate to '" + _newName + "'";
        }

        public string Description { get; }

        public void Execute()
        {
            _versions.Clear();
            _oldNames.Clear();
            _library.GetVersions(_id, _versions);
            if (_versions.Count == 0)
            {
                throw new InvalidOperationException("Custom gate " + _id + " is not in the library.");
            }

            for (int i = 0; i < _versions.Count; i++)
            {
                _oldNames.Add(_versions[i].Name);
                Apply(_versions[i], _newName);
            }
        }

        public void Undo()
        {
            for (int i = _versions.Count - 1; i >= 0; i--)
            {
                Apply(_versions[i], _oldNames[i]);
            }
        }

        private void Apply(CircuitDefinition definition, string name)
        {
            definition.UpdateMetadata(name, definition.Description, definition.Category, definition.Icon);
            _library.NotifyUpdated(definition);
        }
    }
}
