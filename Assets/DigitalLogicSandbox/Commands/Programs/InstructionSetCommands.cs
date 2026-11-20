using System;
using System.Collections.Generic;
using DigitalLogicSandbox.Commands.UndoRedo;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.GateLibrary.Programs;

namespace DigitalLogicSandbox.Commands.Programs
{
    /// <summary>Adds a user instruction set to the project.</summary>
    public sealed class AddInstructionSetCommand : ICommand
    {
        private readonly InstructionSetLibrary _library;
        private readonly InstructionSetEntry _entry;

        public AddInstructionSetCommand(InstructionSetLibrary library, InstructionSetEntry entry)
        {
            _library = library ?? throw new ArgumentNullException(nameof(library));
            _entry = entry ?? throw new ArgumentNullException(nameof(entry));
            Description = "Add instruction set " + entry.Name;
        }

        public string Description { get; }

        public void Execute()
        {
            _library.Insert(_library.Sets.Count, _entry);
        }

        public void Undo()
        {
            _library.Remove(_entry.Id);
        }
    }

    /// <summary>Changes a user instruction set's name and spec.</summary>
    public sealed class EditInstructionSetCommand : ICommand
    {
        private readonly InstructionSetLibrary _library;
        private readonly InstructionSetEntry _entry;
        private InstructionSetEntry _previous;

        public EditInstructionSetCommand(InstructionSetLibrary library, InstructionSetEntry entry)
        {
            _library = library ?? throw new ArgumentNullException(nameof(library));
            _entry = entry ?? throw new ArgumentNullException(nameof(entry));
            Description = "Edit instruction set " + entry.Name;
        }

        public string Description { get; }

        public void Execute()
        {
            _previous = _library.Replace(_entry);
        }

        public void Undo()
        {
            _library.Replace(_previous);
        }
    }

    /// <summary>Removes a user instruction set and every binding to it (gates fall back to their default).</summary>
    public sealed class RemoveInstructionSetCommand : ICommand
    {
        private readonly InstructionSetLibrary _library;
        private readonly string _id;
        private readonly List<DefinitionId> _boundOwners = new List<DefinitionId>();
        private InstructionSetEntry _entry;
        private int _index;

        public RemoveInstructionSetCommand(InstructionSetLibrary library, string id)
        {
            _library = library ?? throw new ArgumentNullException(nameof(library));
            _id = id ?? throw new ArgumentNullException(nameof(id));
            Description = library.TryGet(id, out InstructionSetEntry entry)
                ? "Delete instruction set " + entry.Name
                : "Delete instruction set";
        }

        public string Description { get; }

        public void Execute()
        {
            _index = _library.IndexOf(_id);
            if (_index < 0)
            {
                throw new InvalidOperationException("No instruction set '" + _id + "'.");
            }

            _entry = _library.Sets[_index];
            _boundOwners.Clear();
            foreach (KeyValuePair<DefinitionId, string> binding in _library.Bindings)
            {
                if (binding.Value == _id)
                {
                    _boundOwners.Add(binding.Key);
                }
            }

            for (int i = 0; i < _boundOwners.Count; i++)
            {
                _library.SetBinding(_boundOwners[i], null);
            }

            _library.Remove(_id);
        }

        public void Undo()
        {
            _library.Insert(_index, _entry);
            for (int i = 0; i < _boundOwners.Count; i++)
            {
                _library.SetBinding(_boundOwners[i], _id);
            }
        }
    }

    /// <summary>Chooses the instruction set a program gate (by definition id) assembles with; null = default.</summary>
    public sealed class BindInstructionSetCommand : ICommand
    {
        private readonly InstructionSetLibrary _library;
        private readonly DefinitionId _owner;
        private readonly string _setId;
        private string _previous;

        public BindInstructionSetCommand(InstructionSetLibrary library, DefinitionId owner, string setId, string setName)
        {
            _library = library ?? throw new ArgumentNullException(nameof(library));
            _owner = owner;
            _setId = setId;
            Description = "Use instruction set " + (setName ?? setId ?? "default");
        }

        public string Description { get; }

        public void Execute()
        {
            _previous = _library.BindingOf(_owner);
            _library.SetBinding(_owner, _setId);
        }

        public void Undo()
        {
            _library.SetBinding(_owner, _previous);
        }
    }
}
