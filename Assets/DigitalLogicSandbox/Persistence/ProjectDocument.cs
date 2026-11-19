using System;
using System.Collections.Generic;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.Core.Signals;

namespace DigitalLogicSandbox.Persistence
{
    /// <summary>
    /// Everything a project file contains, as domain objects: the main circuit, every custom gate definition
    /// version (each stored once, however many instances exist), the main circuit's input switch values and the
    /// project settings. Produced by loading and consumed by saving; the application maps it to and from the
    /// live editor state.
    /// </summary>
    public sealed class ProjectDocument
    {
        /// <summary>Display name (usually the file name without extension).</summary>
        public string Name { get; set; } = "Untitled";

        public CircuitDefinition RootCircuit { get; set; }

        /// <summary>Published custom gate versions, in publication order (dependencies before dependents).</summary>
        public List<CircuitDefinition> Definitions { get; } = new List<CircuitDefinition>();

        /// <summary>
        /// Values of the main circuit's input switches by interface port: 0/1 for single-bit inputs, the unsigned
        /// value (bit 0 = least significant) for bus inputs.
        /// </summary>
        public Dictionary<PortId, ulong> InputValues { get; } = new Dictionary<PortId, ulong>();

        public ProjectSettingsData Settings { get; set; } = new ProjectSettingsData();

        /// <summary>When the file was written (UTC). Set by the serializer.</summary>
        public DateTime SavedUtc { get; set; }
    }

    /// <summary>
    /// Project-level settings in a plain, engine-free form. The application maps these to the workspace, signal
    /// animation and truth table components (the persistence layer does not reference them).
    /// </summary>
    [Serializable]
    public sealed class ProjectSettingsData
    {
        public bool GridVisible = true;
        public bool SnapToGrid = true;
        public AnimationSettingsData Animation = new AnimationSettingsData();

        /// <summary>Signal visualization mode name ("Standard" or "HighContrast").</summary>
        public string VisualizationMode = "Standard";

        /// <summary>How wire crossings are drawn ("Hop", "Gap" or "Plain"). Optional; missing means "Hop".</summary>
        public string WireCrossings = "Hop";

        /// <summary>
        /// Whether custom gate names show their version ("NAND v2"). Display only. Optional; missing means true.
        /// </summary>
        public bool ShowGateVersions = true;

        public TruthTableSettingsData TruthTable = new TruthTableSettingsData();
        public CameraSettingsData Camera = new CameraSettingsData();
        public SimulationSettingsData Simulation = new SimulationSettingsData();
    }

    /// <summary>Clock and power-on settings of the live simulation (schema v2).</summary>
    [Serializable]
    public sealed class SimulationSettingsData
    {
        /// <summary>Full clock cycles per second.</summary>
        public float ClockFrequency = 2f;

        /// <summary>Whether the clock ticks in real time while the simulation is playing.</summary>
        public bool ClockEnabled = true;

        /// <summary>Power-on state name ("Resolved" or "Unknown").</summary>
        public string PowerOnState = "Resolved";
    }

    [Serializable]
    public sealed class AnimationSettingsData
    {
        public bool Enabled = true;
        public float Speed = 1f;
        public bool ShowParticles = true;
        public float MaxBatchSeconds = 3f;
    }

    [Serializable]
    public sealed class TruthTableSettingsData
    {
        public int MaxInputs = 16;
        public int MaxRows = 1 << 16;
        public int ConfirmRows = 1 << 10;
    }

    [Serializable]
    public sealed class CameraSettingsData
    {
        /// <summary>False until a camera position has been saved (new projects frame the default view).</summary>
        public bool HasValue;

        public float X;
        public float Y;
        public float Size = 10f;
    }
}
