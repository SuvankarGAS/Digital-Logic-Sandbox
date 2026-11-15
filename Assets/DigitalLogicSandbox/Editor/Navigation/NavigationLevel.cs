using System;
using DigitalLogicSandbox.CircuitEditor.Workspace;
using DigitalLogicSandbox.Commands.UndoRedo;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Nodes;

namespace DigitalLogicSandbox.CircuitEditor.Navigation
{
    /// <summary>What a navigation level shows and what the user may do there.</summary>
    public enum NavigationLevelKind
    {
        /// <summary>The project's main circuit (editable, own simulation).</summary>
        Root = 0,

        /// <summary>
        /// The internals of a placed custom gate, read-only. Signals are those of that particular instance inside
        /// the owning session's simulation.
        /// </summary>
        Inspect = 1,

        /// <summary>
        /// An editable draft of a custom gate (the next version of its definition), with its own simulation and
        /// undo history. Saved explicitly as a new version, or discarded.
        /// </summary>
        Draft = 2,

        /// <summary>
        /// A published gate opened straight from the library, without an instance: read-only, simulated on its own
        /// session so its inputs can be toggled without touching the main circuit.
        /// </summary>
        Preview = 3,
    }

    /// <summary>One entry of the breadcrumb path (Main Circuit → ALU → Full Adder …).</summary>
    public sealed class NavigationLevel
    {
        private static readonly NodeId[] EmptyPath = new NodeId[0];

        internal NavigationLevel(NavigationLevelKind kind, CircuitDefinition circuit, CircuitSession session,
            CommandHistory history, NodeId[] path, NodeId nodeInParent, DefinitionRef draftSource)
        {
            Kind = kind;
            Circuit = circuit ?? throw new ArgumentNullException(nameof(circuit));
            Session = session ?? throw new ArgumentNullException(nameof(session));
            History = history;
            Path = path ?? EmptyPath;
            NodeInParent = nodeInParent;
            DraftSource = draftSource;
        }

        public NavigationLevelKind Kind { get; }

        /// <summary>The circuit shown at this level (a frozen definition for inspection levels).</summary>
        public CircuitDefinition Circuit { get; }

        /// <summary>The session whose simulation provides this level's signals.</summary>
        public CircuitSession Session { get; }

        /// <summary>Undo history for edits at this level; null for read-only levels.</summary>
        public CommandHistory History { get; }

        /// <summary>Instance path from the session's root circuit down to this level (empty for session roots).</summary>
        public NodeId[] Path { get; }

        /// <summary>The node that was opened in the parent level (invalid for the root).</summary>
        public NodeId NodeInParent { get; }

        /// <summary>For drafts: the published version the draft was created from.</summary>
        public DefinitionRef DraftSource { get; }

        public bool IsEditable => Kind == NavigationLevelKind.Root || Kind == NavigationLevelKind.Draft;

        /// <summary>True for a gate opened from the library (no instance in the main circuit).</summary>
        public bool IsPreview => Kind == NavigationLevelKind.Preview;

        /// <summary>Input switches can be flipped only where they are not driven by an enclosing circuit.</summary>
        public bool CanToggleInputs => Path.Length == 0;

        public bool IsDraft => Kind == NavigationLevelKind.Draft;

        /// <summary>True when a draft has edits that would be lost by discarding it.</summary>
        public bool HasUnsavedChanges => IsDraft && History != null && History.CanUndo;

        /// <summary>Breadcrumb text.</summary>
        public string Title
        {
            get
            {
                string name = string.IsNullOrEmpty(Circuit.Name) ? "Untitled" : Circuit.Name;
                return Kind == NavigationLevelKind.Draft ? name + " v" + Circuit.Version + " (draft)" : name;
            }
        }

        // Camera view remembered while the level is not shown (presentation state, not circuit data).
        public bool HasView { get; private set; }

        public float ViewX { get; private set; }

        public float ViewY { get; private set; }

        public float ViewSize { get; private set; }

        public void StoreView(float x, float y, float size)
        {
            ViewX = x;
            ViewY = y;
            ViewSize = size;
            HasView = true;
        }
    }
}
