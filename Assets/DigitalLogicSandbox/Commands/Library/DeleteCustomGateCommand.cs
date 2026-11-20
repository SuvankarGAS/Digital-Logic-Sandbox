using System;
using System.Collections.Generic;
using DigitalLogicSandbox.Commands.UndoRedo;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.GateLibrary.Custom;

namespace DigitalLogicSandbox.Commands.Library
{
    /// <summary>
    /// Removes a custom gate (every published version) from the library. Undo publishes the same definition
    /// objects again, so ids, versions and internal layouts come back unchanged. The command refuses, before removing
    /// anything, when another gate in the library uses any version. Instances in the main circuit are the caller's
    /// responsibility (see <see cref="DefinitionUsage"/>).
    /// </summary>
    public sealed class DeleteCustomGateCommand : ICommand
    {
        private readonly DefinitionLibrary _library;
        private readonly DefinitionId _id;
        private readonly List<CircuitDefinition> _versions = new List<CircuitDefinition>();

        public DeleteCustomGateCommand(DefinitionLibrary library, DefinitionId id, string name = null)
        {
            _library = library ?? throw new ArgumentNullException(nameof(library));
            _id = id;
            Description = "Delete gate" + (string.IsNullOrEmpty(name) ? string.Empty : " '" + name + "'");
        }

        public string Description { get; }

        /// <summary>The removed versions (oldest first) while the command is executed.</summary>
        public IReadOnlyList<CircuitDefinition> Versions => _versions;

        public void Execute()
        {
            _versions.Clear();
            _library.GetVersions(_id, _versions);
            if (_versions.Count == 0)
            {
                throw new InvalidOperationException("Custom gate " + _id + " is not in the library.");
            }

            for (int i = 0; i < _versions.Count; i++)
            {
                CircuitDefinition user = _library.FindReferencingDefinition(_versions[i].Reference);
                if (user != null && user.Id != _id)
                {
                    throw new InvalidOperationException("'" + _versions[i].Name + "' is used inside '" + user.Name +
                        "' and cannot be deleted.");
                }
            }

            for (int i = _versions.Count - 1; i >= 0; i--)
            {
                _library.Remove(_versions[i].Reference);
            }
        }

        public void Undo()
        {
            for (int i = 0; i < _versions.Count; i++)
            {
                if (!_library.Contains(_versions[i].Reference))
                {
                    _library.Add(_versions[i]);
                }
            }
        }
    }
}
