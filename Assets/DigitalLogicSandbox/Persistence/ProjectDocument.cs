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

        /// <summary>Values of the main circuit's input switches by interface port.</summary>
        public Dictionary<PortId, Signal> InputValues { get; } = new Dictionary<PortId, Signal>();

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

        public TruthTableSettingsData TruthTable = new TruthTableSettingsData();
        public CameraSettingsData Camera = new CameraSettingsData();
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
