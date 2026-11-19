using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DigitalLogicSandbox.Animation.Signals;
using DigitalLogicSandbox.CircuitEditor.Navigation;
using DigitalLogicSandbox.CircuitEditor.Workspace;
using DigitalLogicSandbox.Components.Abstractions;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.Core.Signals;
using DigitalLogicSandbox.Core.Validation;
using DigitalLogicSandbox.GateLibrary.Custom;
using DigitalLogicSandbox.GateLibrary.Examples;
using DigitalLogicSandbox.Persistence;
using DigitalLogicSandbox.Persistence.Deserialization;
using DigitalLogicSandbox.Persistence.Migration;
using DigitalLogicSandbox.Persistence.Recovery;
using DigitalLogicSandbox.Rendering;
using DigitalLogicSandbox.Rendering.Wires;
using DigitalLogicSandbox.Simulation.Engine;
using DigitalLogicSandbox.TruthTable.Generation;
using DigitalLogicSandbox.UI;
using UnityEngine;

namespace DigitalLogicSandbox.App
{
    /// <summary>
    /// Application-level project management: maps the live editor state (main circuit, custom gate library,
    /// input switches, settings) to and from a <see cref="ProjectDocument"/>, and implements New / Open / Save /
    /// Save As, dirty tracking, periodic autosave, recovery of interrupted sessions and reopening the last
    /// project on start. It is the only component that knows both the persistence layer and the editor.
    /// </summary>
    public sealed class ProjectController : MonoBehaviour, IProjectService
    {
        public const string LastProjectKey = "DigitalLogicSandbox.LastProject";
        public const string UntitledAutosaveName = "Untitled" + ProjectFormat.FileExtension + ProjectFileStore.AutosaveSuffix;

        private WorkspaceController _workspace;
        private DefinitionLibrary _library;
        private SignalAnimationSettings _animation;
        private TruthTableLimits _truthTableLimits;
        private ProjectFileStore _store;
        private int _suppressDirty;
        private float _nextAutosave;
        private bool _changedSinceAutosave;
        private bool _starting;

        public event Action Changed;

        /// <summary>Raised after a project was loaded, created or recovered (tests and diagnostics).</summary>
        public event Action ProjectReplaced;

        public string CurrentPath { get; private set; }

        public string DisplayName => CurrentPath != null
            ? Path.GetFileNameWithoutExtension(CurrentPath)
            : _unsavedName ?? "Untitled";

        /// <summary>Title of a project that has no file yet (e.g. "CPU demo"); null means "Untitled".</summary>
        private string _unsavedName;

        public bool IsDirty { get; private set; }

        public string ProjectsDirectory { get; set; }

        public string AutosaveDirectory { get; set; }

        /// <summary>Seconds between autosaves while there are unsaved changes (0 disables periodic autosave).</summary>
        public float AutosaveIntervalSeconds { get; set; } = 60f;

        /// <summary>Whether the last opened project is remembered for the next start.</summary>
        public bool RememberLastProject { get; set; } = true;

        public string UntitledAutosavePath => Path.Combine(AutosaveDirectory, UntitledAutosaveName);

        /// <summary>The autosave file used for the current project.</summary>
        public string CurrentAutosavePath =>
            CurrentPath != null ? ProjectFileStore.AutosavePathFor(CurrentPath) : UntitledAutosavePath;

        /// <summary>Messages from the last load (warnings, recovery notes).</summary>
        public string LastLoadMessage { get; private set; }

        public void Initialize(WorkspaceController workspace, DefinitionLibrary library, PrimitiveRegistry primitives,
            SignalAnimationSettings animation, TruthTableLimits truthTableLimits)
        {
            _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            _library = library ?? throw new ArgumentNullException(nameof(library));
            _animation = animation;
            _truthTableLimits = truthTableLimits;
            _store = new ProjectFileStore(new ProjectDeserializer(ProjectMigrator.CreateDefault(), primitives));
            ProjectsDirectory ??= Path.Combine(Application.persistentDataPath, "Projects");
            AutosaveDirectory ??= Path.Combine(Application.persistentDataPath, "Autosave");

            _workspace.Navigator.HistoryChanged += MarkDirty;
            _workspace.SettingsChanged += MarkDirty;
            _library.DefinitionAdded += OnLibraryChanged;
            _library.DefinitionRemoved += OnLibraryChanged;
            _library.DefinitionUpdated += OnLibraryChanged;
            if (_animation != null)
            {
                _animation.Changed += MarkDirty;
            }

            ScheduleAutosave();
        }

        // ------------------------------------------------------------------------------------------------
        // IProjectService
        // ------------------------------------------------------------------------------------------------

        public IReadOnlyList<string> ListProjects()
        {
            return ProjectFileStore.ListProjects(ProjectsDirectory);
        }

        public string ResolvePath(string nameOrPath)
        {
            if (string.IsNullOrWhiteSpace(nameOrPath))
            {
                throw new ArgumentException("Enter a project name.");
            }

            string text = nameOrPath.Trim();
            if (text.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                throw new ArgumentException("The name contains characters that cannot be used in a file name.");
            }

            bool hasDirectory = Path.IsPathRooted(text) || text.IndexOf(Path.DirectorySeparatorChar) >= 0 ||
                                text.IndexOf(Path.AltDirectorySeparatorChar) >= 0;
            if (!hasDirectory && Path.GetFileName(text).IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new ArgumentException("The name contains characters that cannot be used in a file name.");
            }

            string path = hasDirectory ? text : Path.Combine(ProjectsDirectory, text);
            if (!path.EndsWith(ProjectFormat.FileExtension, StringComparison.OrdinalIgnoreCase))
            {
                path += ProjectFormat.FileExtension;
            }

            return Path.GetFullPath(path);
        }

        public void NewProject()
        {
            ProjectFileStore.DeleteIfExists(CurrentAutosavePath); // the user chose to discard this work
            Apply(CreateEmptyDocument());
            SetPath(null);
            SetDirty(false);
            _workspace.ResetCamera();
            ProjectReplaced?.Invoke();
        }

        /// <summary>
        /// Discards the current project (the UI asks first) and opens the CPU demo as a new, unsaved project: the
        /// example pack plus a main circuit running a program on the CPU 4-bit (see <see cref="CpuDemo"/>).
        /// </summary>
        public void OpenCpuDemo()
        {
            ProjectFileStore.DeleteIfExists(CurrentAutosavePath); // the user chose to discard this work
            CpuDemoProject demo = CpuDemo.Build(_workspace.Catalog.Primitives, new GeometryNodeSizer().Measure);
            ProjectDocument document = CreateEmptyDocument();
            document.Name = CpuDemo.MainCircuitName;
            document.RootCircuit = demo.Main;
            document.Definitions.AddRange(demo.Definitions);
            document.Settings.Simulation.ClockFrequency = demo.ClockFrequency;
            Apply(document);
            SetPath(null);
            _unsavedName = CpuDemo.MainCircuitName;
            SetDirty(false);
            Changed?.Invoke(); // the title changed even when the dirty flag did not
            ProjectReplaced?.Invoke();
            _workspace.FrameContent();
            _workspace.PostNotification(CpuDemo.OpenHint);
        }

        public bool Open(string path, out string message)
        {
            ProjectOpenResult result;
            try
            {
                result = _store.Load(path);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException ||
                                              exception is ProjectFormatException)
            {
                message = exception is FileNotFoundException
                    ? "Project not found: " + path
                    : "Could not open the project: " + exception.Message;
                return false;
            }

            if (!_starting)
            {
                // The user chose to leave the current project (the UI asked about unsaved changes first).
                ProjectFileStore.DeleteIfExists(CurrentAutosavePath);
            }

            Apply(result.Document);
            SetPath(Path.GetFullPath(path));
            SetDirty(result.UsedFallback);
            message = DescribeLoad(result.Load, result.RecoveryNote, "Opened '" + DisplayName + "'.");
            LastLoadMessage = message;
            ProjectReplaced?.Invoke();
            return true;
        }

        public bool Save(out string message)
        {
            if (CurrentPath == null)
            {
                message = "Choose a file name first (Save As).";
                return false;
            }

            return SaveTo(CurrentPath, out message);
        }

        public bool SaveAs(string path, out string message)
        {
            return SaveTo(Path.GetFullPath(path), out message);
        }

        // ------------------------------------------------------------------------------------------------
        // Startup, recovery and autosave
        // ------------------------------------------------------------------------------------------------

        /// <summary>
        /// Start-up sequence. If an autosave holds newer work, <paramref name="askRecover"/> is invoked with a
        /// description and two continuations (recover, discard). Otherwise the last project is reopened when
        /// <paramref name="reopenLast"/> is set, or an empty project is created.
        /// </summary>
        public void Startup(bool reopenLast, Action<string, Action, Action> askRecover)
        {
            string last = reopenLast && RememberLastProject ? PlayerPrefs.GetString(LastProjectKey, null) : null;
            if (string.IsNullOrEmpty(last) || !File.Exists(last))
            {
                last = null;
            }

            RecoveryCandidate candidate = ProjectFileStore.FindRecovery(last, null) ??
                                          ProjectFileStore.FindRecovery(null, UntitledAutosavePath);
            if (candidate != null && askRecover != null)
            {
                string owner = candidate.ProjectPath == null
                    ? "an untitled project"
                    : "'" + Path.GetFileNameWithoutExtension(candidate.ProjectPath) + "'";
                string description = "Unsaved changes to " + owner + " were autosaved at " +
                                     candidate.AutosaveUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm") +
                                     " but never saved. Recover them?";
                OpenLastOrNew(last); // show the last project (or an empty one) behind the question
                askRecover(description,
                    () => Recover(candidate),
                    () => ProjectFileStore.DeleteIfExists(candidate.AutosavePath));
                return;
            }

            OpenLastOrNew(last);
        }

        /// <summary>Loads an autosave as unsaved work belonging to its project (or an untitled project).</summary>
        public bool Recover(RecoveryCandidate candidate)
        {
            try
            {
                ProjectLoadResult load = _store.Read(candidate.AutosavePath);
                Apply(load.Document);
                SetPath(candidate.ProjectPath);
                SetDirty(true);
                LastLoadMessage = DescribeLoad(load, null, "Recovered unsaved work. Save to keep it.");
                _workspace.PostNotification(LastLoadMessage);
                ProjectReplaced?.Invoke();
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is ProjectFormatException)
            {
                _workspace.PostNotification("The autosave could not be recovered: " + exception.Message);
                return false;
            }
        }

        /// <summary>Writes the autosave now if there are unsaved changes. Returns true if a file was written.</summary>
        public bool AutosaveNow()
        {
            if (!IsDirty || _workspace == null)
            {
                return false;
            }

            try
            {
                _store.WriteAutosave(CurrentAutosavePath, CaptureDocument());
                _changedSinceAutosave = false;
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogWarning("Autosave failed: " + exception.Message);
                return false;
            }
        }

        // ------------------------------------------------------------------------------------------------
        // Document mapping
        // ------------------------------------------------------------------------------------------------

        /// <summary>Snapshot of the current project as a document (shares the live definitions; nothing is copied).</summary>
        public ProjectDocument CaptureDocument()
        {
            var document = new ProjectDocument
            {
                Name = DisplayName,
                RootCircuit = _workspace.RootCircuit,
            };

            IReadOnlyList<CircuitDefinition> versions = _library.AllVersions;
            for (int i = 0; i < versions.Count; i++)
            {
                document.Definitions.Add(versions[i]);
            }

            CircuitSession session = _workspace.Navigator.RootSession;
            CircuitDefinition root = document.RootCircuit;
            for (int i = 0; i < root.Inputs.Count; i++)
            {
                PortId port = root.Inputs[i].Id;
                if (root.TryGetBoundaryNode(port, out NodeId node))
                {
                    document.InputValues[port] = session.GetInputBits(node);
                }
            }

            ProjectSettingsData settings = document.Settings;
            settings.GridVisible = _workspace.GridVisible;
            settings.SnapToGrid = _workspace.SnapToGrid;
            settings.VisualizationMode = _workspace.VisualizationMode.ToString();
            settings.WireCrossings = _workspace.WireCrossingStyle.ToString();
            settings.ShowGateVersions = _workspace.ShowGateVersions;
            if (_animation != null)
            {
                settings.Animation.Enabled = _animation.Enabled;
                settings.Animation.Speed = _animation.Speed;
                settings.Animation.ShowParticles = _animation.ShowParticles;
                settings.Animation.MaxBatchSeconds = _animation.MaxBatchSeconds;
            }

            if (_truthTableLimits != null)
            {
                settings.TruthTable.MaxInputs = _truthTableLimits.MaxInputs;
                settings.TruthTable.MaxRows = _truthTableLimits.MaxRows;
                settings.TruthTable.ConfirmRows = _truthTableLimits.ConfirmRows;
            }

            settings.Simulation.ClockFrequency = _workspace.ClockFrequency;
            settings.Simulation.ClockEnabled = _workspace.ClockEnabled;
            settings.Simulation.PowerOnState = _workspace.PowerOnState.ToString();
            if (_workspace.WorkspaceCamera != null)
            {
                settings.Camera.HasValue = true;
                settings.Camera.X = _workspace.WorkspaceCamera.Position.x;
                settings.Camera.Y = _workspace.WorkspaceCamera.Position.y;
                settings.Camera.Size = _workspace.WorkspaceCamera.Size;
            }

            return document;
        }

        /// <summary>Replaces the editor state with <paramref name="document"/> (history and navigation reset).</summary>
        public void Apply(ProjectDocument document)
        {
            _suppressDirty++;
            try
            {
                // Detach the workspace from the old project before the library is replaced.
                _workspace.LoadCircuit(new CircuitDefinition(DefinitionId.New(), WorkspaceController.DefaultCircuitName));
                _library.Clear();
                for (int i = 0; i < document.Definitions.Count; i++)
                {
                    _library.Add(document.Definitions[i]);
                }

                // Power-on mode must be in place before the main circuit is compiled and powered on.
                ApplySimulationSettings(document.Settings?.Simulation);
                _workspace.LoadCircuit(document.RootCircuit);
                foreach (KeyValuePair<PortId, ulong> input in document.InputValues)
                {
                    if (document.RootCircuit.TryGetBoundaryNode(input.Key, out NodeId node))
                    {
                        _workspace.SetInputBits(node, input.Value);
                    }
                }

                ApplySettings(document.Settings);
                _workspace.Sync();
            }
            finally
            {
                _suppressDirty--;
            }
        }

        private void ApplySimulationSettings(SimulationSettingsData simulation)
        {
            simulation ??= new SimulationSettingsData();
            _workspace.ClockFrequency = simulation.ClockFrequency;
            _workspace.ClockEnabled = simulation.ClockEnabled;
            _workspace.PowerOnState = Enum.TryParse(simulation.PowerOnState, out PowerOnState powerOn)
                ? powerOn
                : PowerOnState.Resolved;
        }

        private void ApplySettings(ProjectSettingsData settings)
        {
            if (settings == null)
            {
                return;
            }

            _workspace.GridVisible = settings.GridVisible;
            _workspace.SnapToGrid = settings.SnapToGrid;
            _workspace.VisualizationMode = Enum.TryParse(settings.VisualizationMode, out SignalVisualizationMode mode)
                ? mode
                : SignalVisualizationMode.Standard;
            _workspace.WireCrossingStyle = Enum.TryParse(settings.WireCrossings, out WireCrossingStyle crossings) &&
                Enum.IsDefined(typeof(WireCrossingStyle), crossings)
                    ? crossings
                    : WireCrossingStyle.Hop;
            _workspace.ShowGateVersions = settings.ShowGateVersions;
            if (_animation != null && settings.Animation != null)
            {
                _animation.Enabled = settings.Animation.Enabled;
                _animation.Speed = settings.Animation.Speed;
                _animation.ShowParticles = settings.Animation.ShowParticles;
                _animation.MaxBatchSeconds = settings.Animation.MaxBatchSeconds;
            }

            if (_truthTableLimits != null && settings.TruthTable != null)
            {
                _truthTableLimits.MaxInputs = settings.TruthTable.MaxInputs;
                _truthTableLimits.MaxRows = settings.TruthTable.MaxRows;
                _truthTableLimits.ConfirmRows = settings.TruthTable.ConfirmRows;
            }

            if (settings.Camera != null && settings.Camera.HasValue && _workspace.WorkspaceCamera != null)
            {
                _workspace.WorkspaceCamera.MoveTo(new Vector2(settings.Camera.X, settings.Camera.Y), settings.Camera.Size);
            }
        }

        private static ProjectDocument CreateEmptyDocument()
        {
            return new ProjectDocument
            {
                RootCircuit = new CircuitDefinition(DefinitionId.New(), WorkspaceController.DefaultCircuitName),
                Settings = new ProjectSettingsData(),
            };
        }

        // ------------------------------------------------------------------------------------------------
        // Internals
        // ------------------------------------------------------------------------------------------------

        private bool SaveTo(string path, out string message)
        {
            string previousAutosave = CurrentAutosavePath;
            try
            {
                _store.Save(path, CaptureDocument());
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException ||
                                              exception is ArgumentException || exception is NotSupportedException)
            {
                message = "Could not save the project: " + exception.Message;
                return false;
            }

            SetPath(path);
            SetDirty(false);
            ProjectFileStore.DeleteIfExists(previousAutosave);
            ProjectFileStore.DeleteIfExists(CurrentAutosavePath);
            message = "Saved '" + DisplayName + "'.";
            NavigationLevel draft = _workspace.Navigator.Draft;
            if (draft != null)
            {
                message += " The gate being edited ('" + draft.Circuit.Name +
                           "') is saved only after you use Save Version.";
            }

            return true;
        }

        private void OpenLastOrNew(string last)
        {
            _starting = true;
            try
            {
                if (last != null && Open(last, out string message))
                {
                    _workspace.PostNotification(message);
                    return;
                }

                NewProjectWithoutDeletingAutosave();
            }
            finally
            {
                _starting = false;
            }
        }

        private void NewProjectWithoutDeletingAutosave()
        {
            Apply(CreateEmptyDocument());
            SetPath(null);
            SetDirty(false);
            _workspace.ResetCamera();
            ProjectReplaced?.Invoke();
        }

        private static string DescribeLoad(ProjectLoadResult load, string recoveryNote, string headline)
        {
            var builder = new StringBuilder(headline);
            if (recoveryNote != null)
            {
                builder.Append(' ').Append(recoveryNote);
            }

            if (load.WasMigrated)
            {
                builder.Append(" Upgraded from file format v").Append(load.StoredSchemaVersion).Append('.');
            }

            IReadOnlyList<Diagnostic> diagnostics = load.Report.Diagnostics;
            if (diagnostics.Count > 0)
            {
                builder.Append(' ').Append(diagnostics.Count).Append(diagnostics.Count == 1 ? " warning: " : " warnings, first: ")
                    .Append(diagnostics[0].Message);
            }

            return builder.ToString();
        }

        private void SetPath(string path)
        {
            CurrentPath = path;
            _unsavedName = null;
            if (path != null && RememberLastProject)
            {
                PlayerPrefs.SetString(LastProjectKey, path);
                PlayerPrefs.Save();
            }

            Changed?.Invoke();
        }

        private void SetDirty(bool dirty)
        {
            _changedSinceAutosave = dirty;
            if (IsDirty == dirty)
            {
                return;
            }

            IsDirty = dirty;
            Changed?.Invoke();
        }

        private void MarkDirty()
        {
            if (_suppressDirty > 0)
            {
                return;
            }

            _changedSinceAutosave = true;
            if (!IsDirty)
            {
                IsDirty = true;
                Changed?.Invoke();
            }
        }

        private void OnLibraryChanged(CircuitDefinition definition)
        {
            MarkDirty();
        }

        private void ScheduleAutosave()
        {
            _nextAutosave = Time.unscaledTime + Mathf.Max(5f, AutosaveIntervalSeconds);
        }

        private void Update()
        {
            if (AutosaveIntervalSeconds <= 0f || Time.unscaledTime < _nextAutosave)
            {
                return;
            }

            ScheduleAutosave();
            if (_changedSinceAutosave)
            {
                AutosaveNow();
            }
        }

        private void OnApplicationQuit()
        {
            AutosaveNow();
        }

        private void OnDestroy()
        {
            if (_workspace != null && _workspace.Navigator != null)
            {
                _workspace.Navigator.HistoryChanged -= MarkDirty;
                _workspace.SettingsChanged -= MarkDirty;
            }

            if (_library != null)
            {
                _library.DefinitionAdded -= OnLibraryChanged;
                _library.DefinitionRemoved -= OnLibraryChanged;
                _library.DefinitionUpdated -= OnLibraryChanged;
            }

            if (_animation != null)
            {
                _animation.Changed -= MarkDirty;
            }
        }
    }
}
