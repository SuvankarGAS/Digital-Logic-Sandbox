using System;
using DigitalLogicSandbox.CircuitEditor.Interaction;
using DigitalLogicSandbox.Components.Abstractions;
using DigitalLogicSandbox.GateLibrary.Custom;
using DigitalLogicSandbox.Rendering;
using DigitalLogicSandbox.Simulation.Engine;
using UnityEngine;

namespace DigitalLogicSandbox.CircuitEditor.Workspace
{
    /// <summary>Dependencies handed to <see cref="WorkspaceController.Initialize"/> by the composition root.</summary>
    public sealed class WorkspaceSetup
    {
        public ComponentCatalog Catalog { get; set; }

        /// <summary>Library that packaged gates are published to (must be the one behind <see cref="Catalog"/>).</summary>
        public DefinitionLibrary Library { get; set; }

        public VisualTheme Theme { get; set; }

        public Camera Camera { get; set; }

        /// <summary>Pointer and keyboard actions. Leave null for headless use (tests), where only the public API drives the workspace.</summary>
        public WorkspaceControls Controls { get; set; }

        /// <summary>True while the pointer is over UI. Workspace pointer input is ignored then.</summary>
        public Func<bool> IsPointerOverUi { get; set; }

        /// <summary>True while a text field has keyboard focus. Workspace shortcuts are ignored then.</summary>
        public Func<bool> IsTextInputFocused { get; set; }

        public SimulationOptions SimulationOptions { get; set; }

        public bool ShowGrid { get; set; } = true;
    }
}
