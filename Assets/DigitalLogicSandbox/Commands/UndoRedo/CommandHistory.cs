using System;
using System.Collections.Generic;

namespace DigitalLogicSandbox.Commands.UndoRedo
{
    /// <summary>Linear undo/redo history with a bounded depth.</summary>
    public sealed class CommandHistory
    {
        public const int DefaultMaxDepth = 500;

        private readonly List<ICommand> _undo = new List<ICommand>();
        private readonly List<ICommand> _redo = new List<ICommand>();
        private bool _busy;

        public CommandHistory(int maxDepth = DefaultMaxDepth)
        {
            if (maxDepth < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxDepth));
            }

            MaxDepth = maxDepth;
        }

        /// <summary>Raised after any change to the undo/redo stacks.</summary>
        public event Action Changed;

        public int MaxDepth { get; }

        public bool CanUndo => _undo.Count > 0;

        public bool CanRedo => _redo.Count > 0;

        public int UndoCount => _undo.Count;

        public int RedoCount => _redo.Count;

        public string UndoDescription => CanUndo ? _undo[_undo.Count - 1].Description : null;

        public string RedoDescription => CanRedo ? _redo[_redo.Count - 1].Description : null;

        /// <summary>Executes a command and records it. If it throws, nothing is recorded.</summary>
        public void Execute(ICommand command)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            EnsureNotBusy();
            _busy = true;
            try
            {
                command.Execute();
            }
            finally
            {
                _busy = false;
            }

            Push(command);
        }

        /// <summary>
        /// Records a command whose effect has already been applied (e.g. a drag-move applied live while dragging).
        /// </summary>
        public void Record(ICommand alreadyExecuted)
        {
            if (alreadyExecuted == null)
            {
                throw new ArgumentNullException(nameof(alreadyExecuted));
            }

            EnsureNotBusy();
            Push(alreadyExecuted);
        }

        public bool Undo()
        {
            EnsureNotBusy();
            if (_undo.Count == 0)
            {
                return false;
            }

            ICommand command = _undo[_undo.Count - 1];
            _busy = true;
            try
            {
                command.Undo();
            }
            finally
            {
                _busy = false;
            }

            _undo.RemoveAt(_undo.Count - 1);
            _redo.Add(command);
            Changed?.Invoke();
            return true;
        }

        public bool Redo()
        {
            EnsureNotBusy();
            if (_redo.Count == 0)
            {
                return false;
            }

            ICommand command = _redo[_redo.Count - 1];
            _busy = true;
            try
            {
                command.Execute();
            }
            finally
            {
                _busy = false;
            }

            _redo.RemoveAt(_redo.Count - 1);
            _undo.Add(command);
            Changed?.Invoke();
            return true;
        }

        public void Clear()
        {
            _undo.Clear();
            _redo.Clear();
            Changed?.Invoke();
        }

        private void Push(ICommand command)
        {
            _undo.Add(command);
            if (_undo.Count > MaxDepth)
            {
                _undo.RemoveAt(0);
            }

            _redo.Clear();
            Changed?.Invoke();
        }

        private void EnsureNotBusy()
        {
            if (_busy)
            {
                throw new InvalidOperationException("Commands must not modify the history while executing.");
            }
        }
    }
}
