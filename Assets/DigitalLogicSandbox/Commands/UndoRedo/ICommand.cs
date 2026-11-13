namespace DigitalLogicSandbox.Commands.UndoRedo
{
    /// <summary>
    /// A reversible model operation. Commands mutate only the model; views re-sync from model change events,
    /// so undo/redo restores the visual state automatically.
    /// <para>
    /// Contract: <see cref="Execute"/> may be called again after <see cref="Undo"/> (redo) and must reproduce
    /// the identical model state, including node and port ids.
    /// </para>
    /// </summary>
    public interface ICommand
    {
        /// <summary>Short user-facing label, e.g. "Add AND" or "Delete 3 items".</summary>
        string Description { get; }

        void Execute();

        void Undo();
    }
}
