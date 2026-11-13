using System;
using DigitalLogicSandbox.Commands.UndoRedo;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.GateLibrary.Custom;

namespace DigitalLogicSandbox.Commands.Library
{
    /// <summary>
    /// Publishes a packaged gate definition to the library. Undo withdraws it again. That is safe because any
    /// instances placed after packaging sit later in the history and are undone first.
    /// </summary>
    public sealed class PublishDefinitionCommand : ICommand
    {
        private readonly DefinitionLibrary _library;
        private readonly CircuitDefinition _definition;

        public PublishDefinitionCommand(DefinitionLibrary library, CircuitDefinition definition, string description = null)
        {
            _library = library ?? throw new ArgumentNullException(nameof(library));
            _definition = definition ?? throw new ArgumentNullException(nameof(definition));
            Description = description ?? "Package gate '" + definition.Name + "'";
        }

        public string Description { get; }

        public CircuitDefinition Definition => _definition;

        public void Execute()
        {
            if (!_library.Contains(_definition.Reference))
            {
                _library.Add(_definition);
            }
        }

        public void Undo()
        {
            _library.Remove(_definition.Reference);
        }
    }
}
