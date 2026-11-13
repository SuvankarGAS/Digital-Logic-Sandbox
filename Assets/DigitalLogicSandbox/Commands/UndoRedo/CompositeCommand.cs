using System;
using System.Collections.Generic;

namespace DigitalLogicSandbox.Commands.UndoRedo
{
    /// <summary>Runs child commands in order and undoes them in reverse, as one history entry.</summary>
    public sealed class CompositeCommand : ICommand
    {
        private readonly List<ICommand> _commands;

        public CompositeCommand(string description, IEnumerable<ICommand> commands)
        {
            Description = description ?? string.Empty;
            _commands = new List<ICommand>(commands ?? throw new ArgumentNullException(nameof(commands)));
        }

        public string Description { get; }

        public int Count => _commands.Count;

        public void Execute()
        {
            int executed = 0;
            try
            {
                for (; executed < _commands.Count; executed++)
                {
                    _commands[executed].Execute();
                }
            }
            catch
            {
                for (int i = executed - 1; i >= 0; i--)
                {
                    _commands[i].Undo();
                }

                throw;
            }
        }

        public void Undo()
        {
            for (int i = _commands.Count - 1; i >= 0; i--)
            {
                _commands[i].Undo();
            }
        }
    }
}
