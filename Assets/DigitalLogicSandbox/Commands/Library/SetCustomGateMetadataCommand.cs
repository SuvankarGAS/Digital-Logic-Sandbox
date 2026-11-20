using System;
using System.Collections.Generic;
using DigitalLogicSandbox.Commands.UndoRedo;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.GateLibrary.Custom;

namespace DigitalLogicSandbox.Commands.Library
{
    /// <summary>
    /// Changes the presentation metadata (name, description, category, icon) of every published version of a custom
    /// gate. Metadata never affects behaviour, so no new version is created and every reference stays valid. A null
    /// argument keeps that field unchanged.
    /// </summary>
    public sealed class SetCustomGateMetadataCommand : ICommand
    {
        private readonly DefinitionLibrary _library;
        private readonly DefinitionId _id;
        private readonly string _name;
        private readonly string _description;
        private readonly string _category;
        private readonly string _icon;
        private readonly List<CircuitDefinition> _versions = new List<CircuitDefinition>();
        private readonly List<Metadata> _old = new List<Metadata>();

        public SetCustomGateMetadataCommand(DefinitionLibrary library, DefinitionId id, string name, string description,
            string category, string icon, string commandDescription = null)
        {
            _library = library ?? throw new ArgumentNullException(nameof(library));
            if (name != null && string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("A gate name cannot be empty.", nameof(name));
            }

            _id = id;
            _name = name?.Trim();
            _description = description?.Trim();
            _category = category?.Trim();
            _icon = icon?.Trim();
            Description = commandDescription ?? "Edit gate details";
        }

        public string Description { get; }

        public void Execute()
        {
            _versions.Clear();
            _old.Clear();
            _library.GetVersions(_id, _versions);
            if (_versions.Count == 0)
            {
                throw new InvalidOperationException("Custom gate " + _id + " is not in the library.");
            }

            for (int i = 0; i < _versions.Count; i++)
            {
                CircuitDefinition definition = _versions[i];
                _old.Add(new Metadata(definition.Name, definition.Description, definition.Category, definition.Icon));
                Apply(definition, new Metadata(_name ?? definition.Name, _description ?? definition.Description,
                    _category ?? definition.Category, _icon ?? definition.Icon));
            }
        }

        public void Undo()
        {
            for (int i = _versions.Count - 1; i >= 0; i--)
            {
                Apply(_versions[i], _old[i]);
            }
        }

        private void Apply(CircuitDefinition definition, Metadata metadata)
        {
            definition.UpdateMetadata(metadata.Name, metadata.Description, metadata.Category, metadata.Icon);
            _library.NotifyUpdated(definition);
        }

        private readonly struct Metadata
        {
            public Metadata(string name, string description, string category, string icon)
            {
                Name = name;
                Description = description;
                Category = category;
                Icon = icon;
            }

            public string Name { get; }

            public string Description { get; }

            public string Category { get; }

            public string Icon { get; }
        }
    }
}
