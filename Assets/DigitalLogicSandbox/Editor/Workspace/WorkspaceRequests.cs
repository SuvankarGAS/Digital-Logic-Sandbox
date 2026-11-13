using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Validation;
using DigitalLogicSandbox.GateLibrary.Custom;
using UnityEngine;

namespace DigitalLogicSandbox.CircuitEditor.Workspace
{
    /// <summary>A right-click in the workspace: where, and what was under the pointer.</summary>
    public readonly struct ContextMenuRequest
    {
        public ContextMenuRequest(Vector2 screen, NodeId node, PinRef? wire)
        {
            Screen = screen;
            Node = node;
            Wire = wire;
        }

        /// <summary>Pointer position in screen pixels.</summary>
        public Vector2 Screen { get; }

        /// <summary>The node under the pointer (now selected), or <see cref="NodeId.None"/>.</summary>
        public NodeId Node { get; }

        /// <summary>The wire under the pointer (now selected), if no node was hit.</summary>
        public PinRef? Wire { get; }
    }

    /// <summary>Outcome of saving an edited custom gate as a new version.</summary>
    public sealed class DraftSaveResult
    {
        internal DraftSaveResult(ValidationReport report, string error, VersionPlan plan, int droppedConnections,
            int retargetedNodes)
        {
            Report = report ?? new ValidationReport();
            Error = error;
            Plan = plan;
            DroppedConnections = droppedConnections;
            RetargetedNodes = retargetedNodes;
        }

        public bool Succeeded => Plan != null;

        /// <summary>Validation of the draft (errors prevent saving).</summary>
        public ValidationReport Report { get; }

        /// <summary>Why saving was refused, or null.</summary>
        public string Error { get; }

        /// <summary>The versions that were published, or null when saving failed.</summary>
        public VersionPlan Plan { get; }

        /// <summary>The new version of the edited gate.</summary>
        public CircuitDefinition Saved => Plan?.Draft;

        /// <summary>Connections in the main circuit and cascaded gates lost because pins were removed.</summary>
        public int DroppedConnections { get; }

        /// <summary>Main-circuit instances switched to new versions.</summary>
        public int RetargetedNodes { get; }

        internal static DraftSaveResult Failed(string error, ValidationReport report = null)
        {
            return new DraftSaveResult(report, error, null, 0, 0);
        }
    }
}
