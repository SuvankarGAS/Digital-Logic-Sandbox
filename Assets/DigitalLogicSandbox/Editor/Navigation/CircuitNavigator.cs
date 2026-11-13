using System;
using System.Collections.Generic;
using DigitalLogicSandbox.CircuitEditor.Workspace;
using DigitalLogicSandbox.Commands.UndoRedo;
using DigitalLogicSandbox.Components.Abstractions;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Validation;
using DigitalLogicSandbox.GateLibrary.Custom;
using DigitalLogicSandbox.Simulation.Engine;

namespace DigitalLogicSandbox.CircuitEditor.Navigation
{
    /// <summary>
    /// The breadcrumb stack of circuit levels: the main circuit at the bottom, then opened custom gates. Each level
    /// knows which session simulates it and its instance path inside that session, so an inspected gate shows the
    /// live signals of exactly that instance. Edit mode turns the top inspection level into a draft (the next version
    /// of the gate) with its own session and undo history; at most one draft exists at a time.
    /// <para>
    /// Safety: opening is refused for missing definitions, for any definition already on the path (circular
    /// navigation) and beyond the hierarchy depth limit. Contains no Unity objects.
    /// </para>
    /// </summary>
    public sealed class CircuitNavigator : IDisposable
    {
        private readonly List<NavigationLevel> _levels = new List<NavigationLevel>();
        private readonly ComponentCatalog _catalog;
        private readonly DefinitionLibrary _library;
        private readonly SimulationOptions _options;
        private readonly CircuitSession _rootSession;
        private readonly CommandHistory _rootHistory;
        private CircuitSession _draftSession;
        private CommandHistory _draftHistory;
        private NavigationLevel _draftReplaced;

        public CircuitNavigator(ComponentCatalog catalog, DefinitionLibrary library, SimulationOptions options = null,
            int maxDepth = ValidationOptions.DefaultMaxHierarchyDepth)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _library = library ?? throw new ArgumentNullException(nameof(library));
            _options = options;
            MaxDepth = maxDepth;
            _rootSession = new CircuitSession(catalog, options);
            _rootHistory = new CommandHistory();
            _rootSession.Rebuilt += RaiseActivity;
            _rootSession.Runner.StateChanged += OnRunnerStateChanged;
            _rootHistory.Changed += RaiseHistoryChanged;
        }

        /// <summary>Raised whenever the level stack changes (open, back, edit, discard, load).</summary>
        public event Action LevelsChanged;

        /// <summary>Raised when any level's session recompiles or its runner changes state.</summary>
        public event Action SessionActivity;

        /// <summary>Raised when any level's undo history changes.</summary>
        public event Action HistoryChanged;

        public int MaxDepth { get; }

        public IReadOnlyList<NavigationLevel> Levels => _levels;

        public NavigationLevel Root => _levels.Count > 0 ? _levels[0] : null;

        public NavigationLevel Current => _levels.Count > 0 ? _levels[_levels.Count - 1] : null;

        public int CurrentIndex => _levels.Count - 1;

        public CircuitSession RootSession => _rootSession;

        public CommandHistory RootHistory => _rootHistory;

        /// <summary>The draft level, or null when no gate is being edited.</summary>
        public NavigationLevel Draft
        {
            get
            {
                for (int i = 0; i < _levels.Count; i++)
                {
                    if (_levels[i].IsDraft)
                    {
                        return _levels[i];
                    }
                }

                return null;
            }
        }

        /// <summary>Breadcrumb text, e.g. "Main Circuit > OR > NAND".</summary>
        public string PathText
        {
            get
            {
                var text = new System.Text.StringBuilder();
                for (int i = 0; i < _levels.Count; i++)
                {
                    if (i > 0)
                    {
                        text.Append(" > ");
                    }

                    text.Append(_levels[i].Title);
                }

                return text.ToString();
            }
        }

        /// <summary>Shows <paramref name="circuit"/> as the main circuit, closing every level and clearing undo.</summary>
        public void LoadRoot(CircuitDefinition circuit)
        {
            CloseDraft();
            _levels.Clear();
            _rootHistory.Clear();
            _rootSession.Bind(circuit);
            if (circuit != null)
            {
                _levels.Add(new NavigationLevel(NavigationLevelKind.Root, circuit, _rootSession, _rootHistory, null,
                    NodeId.None, default));
            }

            LevelsChanged?.Invoke();
        }

        /// <summary>Checks whether <paramref name="node"/> of the current level can be opened.</summary>
        public bool CanOpen(NodeId node, out CircuitDefinition definition, out string error)
        {
            definition = null;
            NavigationLevel current = Current;
            if (current == null)
            {
                error = "No circuit is loaded.";
                return false;
            }

            if (!current.Circuit.TryGetNode(node, out NodeDefinition target))
            {
                error = "The gate no longer exists.";
                return false;
            }

            if (target.Component.Kind != ComponentKind.Custom)
            {
                error = "Only custom gates have an internal circuit to open.";
                return false;
            }

            if (!_catalog.TryGetDefinition(target.Component.Definition, out definition))
            {
                error = "The definition of this gate (" + target.Component.Definition + ") is missing from the library.";
                definition = null;
                return false;
            }

            for (int i = 0; i < _levels.Count; i++)
            {
                if (_levels[i].Circuit.Id == definition.Id)
                {
                    error = "'" + definition.Name + "' is already open further up the path (circular reference).";
                    definition = null;
                    return false;
                }
            }

            if (_levels.Count > MaxDepth)
            {
                error = "The hierarchy is deeper than the limit of " + MaxDepth + " levels.";
                definition = null;
                return false;
            }

            error = null;
            return true;
        }

        /// <summary>Opens the internals of a custom gate of the current level (read-only inspection).</summary>
        public bool TryOpen(NodeId node, out string error)
        {
            if (!CanOpen(node, out CircuitDefinition definition, out error))
            {
                return false;
            }

            NavigationLevel current = Current;
            var path = new NodeId[current.Path.Length + 1];
            Array.Copy(current.Path, path, current.Path.Length);
            path[path.Length - 1] = node;
            _levels.Add(new NavigationLevel(NavigationLevelKind.Inspect, definition, current.Session, null, path, node,
                default));
            LevelsChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Returns to the level at <paramref name="index"/> (a breadcrumb click). Leaving a draft with unsaved
        /// changes is refused unless <paramref name="discardChanges"/>.
        /// </summary>
        public bool TryNavigateTo(int index, out string error, bool discardChanges = false)
        {
            if (index < 0 || index >= _levels.Count)
            {
                error = "There is no such level.";
                return false;
            }

            if (index == CurrentIndex)
            {
                error = null;
                return true;
            }

            for (int i = index + 1; i < _levels.Count; i++)
            {
                if (_levels[i].HasUnsavedChanges && !discardChanges)
                {
                    error = "Save or discard the changes to '" + _levels[i].Circuit.Name + "' first.";
                    return false;
                }
            }

            Truncate(index + 1);
            error = null;
            LevelsChanged?.Invoke();
            return true;
        }

        /// <summary>Goes up one level (same rules as <see cref="TryNavigateTo"/>).</summary>
        public bool TryNavigateUp(out string error)
        {
            if (_levels.Count <= 1)
            {
                error = "Already at the main circuit.";
                return false;
            }

            return TryNavigateTo(CurrentIndex - 1, out error);
        }

        /// <summary>Whether the current level can switch to edit mode.</summary>
        public bool CanBeginEdit(out string error)
        {
            NavigationLevel current = Current;
            if (current == null || current.Kind != NavigationLevelKind.Inspect)
            {
                error = current != null && current.IsDraft
                    ? "This gate is already being edited."
                    : "Open a custom gate to edit its implementation.";
                return false;
            }

            NavigationLevel draft = Draft;
            if (draft != null)
            {
                error = "Finish editing '" + draft.Circuit.Name + "' before editing another gate.";
                return false;
            }

            error = null;
            return true;
        }

        /// <summary>
        /// Switches the current inspection level to edit mode: it is replaced by a mutable draft of the next version
        /// of the gate, simulated on its own. Published versions are never modified.
        /// </summary>
        public bool TryBeginEdit(out string error)
        {
            if (!CanBeginEdit(out error))
            {
                return false;
            }

            NavigationLevel inspected = Current;
            CircuitDefinition draft = DefinitionVersioning.CreateDraft(_library, inspected.Circuit);
            _draftSession = new CircuitSession(_catalog, _options);
            _draftSession.Rebuilt += RaiseActivity;
            _draftSession.Runner.StateChanged += OnRunnerStateChanged;
            _draftSession.Bind(draft);
            _draftHistory = new CommandHistory();
            _draftHistory.Changed += RaiseHistoryChanged;

            var level = new NavigationLevel(NavigationLevelKind.Draft, draft, _draftSession, _draftHistory, null,
                inspected.NodeInParent, inspected.Circuit.Reference);
            if (inspected.HasView)
            {
                level.StoreView(inspected.ViewX, inspected.ViewY, inspected.ViewSize);
            }

            _draftReplaced = inspected;
            _levels[_levels.Count - 1] = level;
            LevelsChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Drops the draft (and anything opened inside it) and returns to inspecting the published version it was
        /// created from.
        /// </summary>
        public bool DiscardDraft()
        {
            for (int i = 0; i < _levels.Count; i++)
            {
                if (_levels[i].IsDraft)
                {
                    NavigationLevel inspected = _draftReplaced;
                    Truncate(i);
                    if (inspected != null)
                    {
                        _levels.Add(inspected);
                    }

                    LevelsChanged?.Invoke();
                    ValidateStack();
                    return true;
                }
            }

            return false;
        }

        /// <summary>Closes every level above the main circuit (drafts are discarded).</summary>
        public void ReturnToRoot()
        {
            if (_levels.Count <= 1)
            {
                return;
            }

            Truncate(1);
            LevelsChanged?.Invoke();
        }

        /// <summary>
        /// The compiled instance whose nets carry <paramref name="level"/>'s signals, or null when that session is
        /// not simulating or the path no longer resolves to the level's definition.
        /// </summary>
        public CompiledInstance ResolveScope(NavigationLevel level)
        {
            if (level == null || !level.Session.IsSimulating)
            {
                return null;
            }

            CompiledInstance instance = level.Session.Compiled.Root;
            NodeId[] path = level.Path;
            for (int i = 0; i < path.Length; i++)
            {
                if (!instance.TryGetChild(path[i], out instance))
                {
                    return null;
                }
            }

            return instance.Definition.Reference == level.Circuit.Reference ? instance : null;
        }

        /// <summary>
        /// Drops levels whose opened node no longer exists or no longer references the shown definition (e.g. the
        /// library changed). Returns true when the stack changed.
        /// </summary>
        public bool ValidateStack()
        {
            for (int i = 1; i < _levels.Count; i++)
            {
                NavigationLevel level = _levels[i];
                if (level.Kind != NavigationLevelKind.Inspect)
                {
                    continue; // drafts own their circuit; only their parent link matters for saving
                }

                NavigationLevel parent = _levels[i - 1];
                if (!parent.Circuit.TryGetNode(level.NodeInParent, out NodeDefinition node) ||
                    node.Component.Kind != ComponentKind.Custom ||
                    node.Component.Definition != level.Circuit.Reference ||
                    !_library.Contains(level.Circuit.Reference))
                {
                    Truncate(i);
                    LevelsChanged?.Invoke();
                    return true;
                }
            }

            return false;
        }

        /// <summary>Marks every session for recompilation (e.g. the gate library changed).</summary>
        public void MarkAllDirty()
        {
            _rootSession.MarkDirty();
            _draftSession?.MarkDirty();
        }

        /// <summary>Recompiles dirty sessions. Returns true when the session of the current level was rebuilt.</summary>
        public bool RebuildDirtySessions()
        {
            bool rootRebuilt = _rootSession.RebuildIfDirty();
            bool draftRebuilt = _draftSession != null && _draftSession.RebuildIfDirty();
            CircuitSession current = Current?.Session;
            return (rootRebuilt && current == _rootSession) || (draftRebuilt && current == _draftSession);
        }

        public void Dispose()
        {
            CloseDraft();
            _levels.Clear();
            _rootSession.Rebuilt -= RaiseActivity;
            _rootSession.Runner.StateChanged -= OnRunnerStateChanged;
            _rootHistory.Changed -= RaiseHistoryChanged;
            _rootSession.Dispose();
        }

        private void Truncate(int count)
        {
            bool draftRemoved = false;
            for (int i = count; i < _levels.Count; i++)
            {
                draftRemoved |= _levels[i].IsDraft;
            }

            _levels.RemoveRange(count, _levels.Count - count);
            if (draftRemoved)
            {
                CloseDraft();
            }
        }

        private void CloseDraft()
        {
            _draftReplaced = null;
            if (_draftSession != null)
            {
                _draftSession.Rebuilt -= RaiseActivity;
                _draftSession.Runner.StateChanged -= OnRunnerStateChanged;
                _draftSession.Dispose();
                _draftSession = null;
            }

            if (_draftHistory != null)
            {
                _draftHistory.Changed -= RaiseHistoryChanged;
                _draftHistory = null;
            }
        }

        private void OnRunnerStateChanged(SimulationState state)
        {
            RaiseActivity();
        }

        private void RaiseActivity()
        {
            SessionActivity?.Invoke();
        }

        private void RaiseHistoryChanged()
        {
            HistoryChanged?.Invoke();
        }
    }
}
