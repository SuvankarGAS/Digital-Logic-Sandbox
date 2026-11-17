using System;
using System.Collections.Generic;
using DigitalLogicSandbox.Animation.Controllers;
using DigitalLogicSandbox.Animation.Signals;
using DigitalLogicSandbox.CircuitEditor.CameraControl;
using DigitalLogicSandbox.CircuitEditor.Interaction;
using DigitalLogicSandbox.CircuitEditor.Navigation;
using DigitalLogicSandbox.CircuitEditor.Placement;
using DigitalLogicSandbox.CircuitEditor.Selection;
using DigitalLogicSandbox.Commands.Circuit;
using DigitalLogicSandbox.Commands.Library;
using DigitalLogicSandbox.Commands.UndoRedo;
using DigitalLogicSandbox.Components.Abstractions;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.Core.Signals;
using DigitalLogicSandbox.Core.Validation;
using DigitalLogicSandbox.GateLibrary.Custom;
using DigitalLogicSandbox.GateLibrary.Packaging;
using DigitalLogicSandbox.Rendering;
using DigitalLogicSandbox.Rendering.Gates;
using DigitalLogicSandbox.Rendering.Wires;
using DigitalLogicSandbox.Simulation.Engine;
using DigitalLogicSandbox.Simulation.Propagation;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DigitalLogicSandbox.CircuitEditor.Workspace
{
    /// <summary>
    /// Hub of the visual circuit editor. It owns the session (model plus live simulation), the command history,
    /// the selection, the presenter (views) and the camera, and exposes every editing operation as a public method.
    /// Pointer and keyboard input is translated into those calls by <see cref="WorkspaceInteraction"/>. UI panels
    /// and tests call the same API.
    ///
    /// Frame order: input (Update) → model mutations via commands → <see cref="Sync"/> (LateUpdate), which
    /// recompiles at most once per frame after structural edits and forwards the simulation's change log to the
    /// views. The views never drive logic.
    ///
    /// Hierarchy: the <see cref="CircuitNavigator"/> holds the breadcrumb levels. <see cref="Circuit"/>,
    /// <see cref="Session"/> and <see cref="History"/> always refer to the level being shown. Inspection levels are
    /// read-only (every editing call is refused with a notification); a draft level edits the next version of a
    /// gate, which <see cref="SaveDraft"/> publishes as one undoable step on the main circuit's history.
    /// </summary>
    public sealed class WorkspaceController : MonoBehaviour
    {
        public const float DuplicateOffset = 1f;
        public const string DefaultCircuitName = "Main Circuit";

        private readonly CommandHistory _readOnlyHistory = new CommandHistory();
        private readonly SelectionModel _selection = new SelectionModel();
        private readonly List<NodeId> _scratchNodes = new List<NodeId>();

        private WorkspaceSetup _setup;
        private ComponentCatalog _catalog;
        private bool _snapToGrid = true;
        private DefinitionLibrary _library;
        private CircuitPackager _packager;
        private CircuitNavigator _navigator;
        private NavigationLevel _shownLevel;
        private bool _scopeDirty;
        private CircuitGeometry _geometry;
        private CircuitPresenter _presenter;
        private SignalAnimationController _animation;
        private WorkspaceCamera _camera;
        private WorkspaceHitTester _hitTester;
        private WorkspaceInteraction _interaction;
        private GridView _grid;
        private SelectionRectView _selectionRect;
        private PlacementRequest? _placement;
        private GateGeometry _placementGeometry;
        private bool _placementFromDrag;
        private bool _statusDirty;
        private bool _inputSuspended;

        /// <summary>Raised (at most once per frame) when anything shown in status or toolbar UI may have changed.</summary>
        public event Action StatusChanged;

        /// <summary>Raised when placement mode starts or ends.</summary>
        public event Action PlacementChanged;

        /// <summary>Raised after the shown circuit level changed (open, back, edit, save, discard, load).</summary>
        public event Action NavigationChanged;

        /// <summary>Short user-facing messages (refused operations, save results) for the status bar.</summary>
        public event Action<string> Notification;

        /// <summary>Raised on a right-click without dragging.</summary>
        public event Action<ContextMenuRequest> ContextMenuRequested;

        /// <summary>Raised when a persisted workspace setting changes (grid, snapping, visualization mode, clock).</summary>
        public event Action SettingsChanged;

        public bool IsInitialized => _navigator != null;

        public CircuitNavigator Navigator => _navigator;

        /// <summary>The breadcrumb level being shown.</summary>
        public NavigationLevel CurrentLevel => _navigator?.Current;

        /// <summary>The circuit being shown (the main circuit, an inspected gate or a draft).</summary>
        public CircuitDefinition Circuit => _navigator?.Current?.Circuit;

        /// <summary>The project's main circuit, whatever level is shown.</summary>
        public CircuitDefinition RootCircuit => _navigator?.Root?.Circuit;

        /// <summary>The session simulating the shown level.</summary>
        public CircuitSession Session => _navigator?.Current?.Session ?? _navigator?.RootSession;

        public SimulationRunner Runner => Session?.Runner;

        /// <summary>Clock frequency in full cycles per second (shared by the main circuit and drafts).</summary>
        public float ClockFrequency
        {
            get => _navigator != null ? _navigator.ClockFrequency : SimulationRunner.DefaultClockFrequency;
            set
            {
                if (_navigator == null)
                {
                    return;
                }

                float before = _navigator.ClockFrequency;
                _navigator.ClockFrequency = value;
                if (!Mathf.Approximately(before, _navigator.ClockFrequency))
                {
                    MarkStatusDirty();
                    SettingsChanged?.Invoke();
                }
            }
        }

        /// <summary>Whether clock nodes tick in real time while the simulation plays.</summary>
        public bool ClockEnabled
        {
            get => _navigator == null || _navigator.ClockEnabled;
            set
            {
                if (_navigator != null && _navigator.ClockEnabled != value)
                {
                    _navigator.ClockEnabled = value;
                    MarkStatusDirty();
                    SettingsChanged?.Invoke();
                }
            }
        }

        /// <summary>How feedback loops start on load and Reset (takes effect at the next Reset).</summary>
        public PowerOnState PowerOnState
        {
            get => _navigator != null ? _navigator.PowerOnState : PowerOnState.Resolved;
            set
            {
                if (_navigator != null && _navigator.PowerOnState != value)
                {
                    _navigator.PowerOnState = value;
                    MarkStatusDirty();
                    SettingsChanged?.Invoke();
                }
            }
        }

        /// <summary>Applies one clock edge to the shown level's simulation (e.g. clicking a clock node).</summary>
        public bool TickClock()
        {
            SimulationRunner runner = Runner;
            if (runner == null || runner.Context == null || !runner.HasClock)
            {
                Notification?.Invoke("This circuit has no clock.");
                return false;
            }

            runner.TickClock();
            MarkStatusDirty();
            return true;
        }

        /// <summary>Undo history of the shown level (an always-empty history for read-only levels).</summary>
        public CommandHistory History => _navigator?.Current?.History ?? _readOnlyHistory;

        /// <summary>False while inspecting a custom gate: its internals cannot be changed outside edit mode.</summary>
        public bool IsEditable => CurrentLevel != null && CurrentLevel.IsEditable;

        /// <summary>Input switches are user-controlled only at the top of a simulation (main circuit or draft).</summary>
        public bool CanToggleInputs => CurrentLevel != null && CurrentLevel.CanToggleInputs;

        /// <summary>Packaging new gates happens in the main circuit.</summary>
        public bool CanPackage => CurrentLevel != null && CurrentLevel.Kind == NavigationLevelKind.Root;

        public SelectionModel Selection => _selection;

        public CircuitPresenter Presenter => _presenter;

        /// <summary>Presentation-only replay of signal changes (never affects logical values).</summary>
        public SignalAnimationController SignalAnimation => _animation;

        public SignalAnimationSettings AnimationSettings => _animation?.Settings;

        /// <summary>How wire signal states are drawn.</summary>
        public SignalVisualizationMode VisualizationMode
        {
            get => _presenter != null ? _presenter.VisualizationMode : SignalVisualizationMode.Standard;
            set
            {
                if (_presenter != null && _presenter.VisualizationMode != value)
                {
                    _presenter.SetVisualizationMode(value);
                    MarkStatusDirty();
                    SettingsChanged?.Invoke();
                }
            }
        }

        /// <summary>How wires are drawn where they cross another net (hop, gap or plain).</summary>
        public WireCrossingStyle WireCrossingStyle
        {
            get => _presenter != null ? _presenter.CrossingStyle : WireCrossingStyle.Hop;
            set
            {
                if (_presenter != null && _presenter.CrossingStyle != value)
                {
                    _presenter.SetWireCrossingStyle(value);
                    MarkStatusDirty();
                    SettingsChanged?.Invoke();
                }
            }
        }

        public CircuitGeometry Geometry => _geometry;

        public WorkspaceCamera WorkspaceCamera => _camera;

        public WorkspaceHitTester HitTester => _hitTester;

        public VisualTheme Theme => _setup?.Theme;

        public ComponentCatalog Catalog => _catalog;

        public DefinitionLibrary Library => _library;

        /// <summary>
        /// While true (e.g. a modal dialog is open), workspace pointer and keyboard input is ignored and any gesture
        /// in progress is cancelled. The public API keeps working.
        /// </summary>
        public bool InputSuspended
        {
            get => _inputSuspended;
            set
            {
                if (value && !_inputSuspended && IsInitialized)
                {
                    CancelInteraction();
                    _presenter.SetHover(NodeId.None, null);
                }

                _inputSuspended = value;
            }
        }

        public SelectionRectView SelectionRect => _selectionRect;

        public bool SnapToGrid
        {
            get => _snapToGrid;
            set
            {
                if (_snapToGrid != value)
                {
                    _snapToGrid = value;
                    SettingsChanged?.Invoke();
                }
            }
        }

        public bool IsPlacing => _placement.HasValue;

        public PlacementRequest? Placement => _placement;

        internal bool PlacementFromDrag => _placementFromDrag;

        public bool GridVisible
        {
            get => _grid != null && _grid.Visible;
            set
            {
                if (_grid != null && _grid.Visible != value)
                {
                    _grid.Visible = value;
                    SettingsChanged?.Invoke();
                }
            }
        }

        // ------------------------------------------------------------------------------------------------
        // Lifecycle
        // ------------------------------------------------------------------------------------------------

        public void Initialize(WorkspaceSetup setup)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException("The workspace is already initialized.");
            }

            _setup = setup ?? throw new ArgumentNullException(nameof(setup));
            _catalog = setup.Catalog ?? throw new ArgumentException("A component catalog is required.", nameof(setup));
            _library = setup.Library ?? throw new ArgumentException("A definition library is required.", nameof(setup));
            _packager = new CircuitPackager(_catalog, _library);
            _library.DefinitionAdded += OnLibraryChanged;
            _library.DefinitionRemoved += OnLibraryChanged;
            _library.DefinitionUpdated += OnLibraryMetadataChanged;
            if (setup.Theme == null)
            {
                throw new ArgumentException("A visual theme is required.", nameof(setup));
            }

            if (setup.Camera == null)
            {
                throw new ArgumentException("A camera is required.", nameof(setup));
            }

            setup.Theme.EnsureMaterials();
            _navigator = new CircuitNavigator(_catalog, _library, setup.SimulationOptions);
            _navigator.SessionActivity += MarkStatusDirty;
            _navigator.HistoryChanged += OnHistoryChanged;
            _navigator.LevelsChanged += OnLevelsChanged;
            _selection.Changed += OnSelectionChanged;

            _geometry = new CircuitGeometry(_catalog);
            _presenter = new CircuitPresenter(transform, setup.Theme, _geometry);
            _animation = new SignalAnimationController(_presenter, setup.Theme,
                setup.SignalAnimation ?? new SignalAnimationSettings { Enabled = false });
            _animation.ActivityChanged += MarkStatusDirty;
            _hitTester = new WorkspaceHitTester(_geometry);
            _camera = new WorkspaceCamera(setup.Camera);
            _selectionRect = SelectionRectView.Create(transform, setup.Theme);
            if (setup.ShowGrid)
            {
                _grid = GridView.Create(setup.Camera, setup.Theme);
            }

            if (setup.Controls != null)
            {
                _interaction = new WorkspaceInteraction(this, setup.Controls);
                setup.Controls.Enable();
            }
        }

        /// <summary>Shows and simulates <paramref name="circuit"/>, discarding undo history and selection.</summary>
        public void LoadCircuit(CircuitDefinition circuit)
        {
            EnsureInitialized();
            CancelInteraction();
            _selection.Clear();
            _shownLevel = null;
            _navigator.LoadRoot(circuit);
            if (circuit == null)
            {
                _presenter.Bind(null);
            }

            Sync();
            MarkStatusDirty();
        }

        private void Update()
        {
            if (!_inputSuspended)
            {
                _interaction?.Tick();
            }
        }

        private void LateUpdate()
        {
            if (!IsInitialized)
            {
                return;
            }

            // The clock ticks in real time; the simulation never waits for animation.
            CircuitSession shown = Session;
            if (shown?.Runner.Advance(Time.unscaledDeltaTime) > 0)
            {
                MarkStatusDirty();
            }

            // While a draft or preview is shown, the main circuit keeps running in the background. Nothing draws
            // it, so its change log is dropped; returning to it redraws every view from the live context.
            CircuitSession root = _navigator?.RootSession;
            if (root != null && shown != root && root.Runner.Advance(Time.unscaledDeltaTime) > 0)
            {
                root.Runner.Context?.Changes.Clear();
            }

            Sync();
            _animation.Tick(Time.unscaledDeltaTime);
            if (_statusDirty)
            {
                _statusDirty = false;
                StatusChanged?.Invoke();
            }
        }

        private void OnDestroy()
        {
            if (_setup?.Controls != null)
            {
                _setup.Controls.Disable();
            }

            if (_library != null)
            {
                _library.DefinitionAdded -= OnLibraryChanged;
                _library.DefinitionRemoved -= OnLibraryChanged;
                _library.DefinitionUpdated -= OnLibraryMetadataChanged;
            }

            if (_camera != null && _camera.Camera != null)
            {
                _camera.StopAnimations();
            }

            _animation?.Dispose();
            _presenter?.Dispose();
            _navigator?.Dispose();
            if (_grid != null)
            {
                Destroy(_grid.gameObject);
            }
        }

        /// <summary>
        /// Brings simulation and views up to date: recompiles after structural edits and applies recorded signal
        /// changes. Runs every LateUpdate. Tests may call it directly.
        /// </summary>
        public void Sync()
        {
            EnsureInitialized();
            _navigator.ValidateStack();
            if (_navigator.RebuildDirtySessions())
            {
                _scopeDirty = true;
            }

            NavigationLevel level = _navigator.Current;
            if (level == null)
            {
                return;
            }

            CircuitSession session = level.Session;
            if (_scopeDirty)
            {
                _scopeDirty = false;
                CompiledInstance scope = _navigator.ResolveScope(level);
                if (scope != null)
                {
                    _presenter.SetSimulation(session.Runner.Context, scope);
                    session.Runner.Context.Changes.Clear();
                }
                else
                {
                    _presenter.ClearSimulation();
                }

                MarkStatusDirty();
                return;
            }

            if (!session.IsSimulating)
            {
                return;
            }

            SignalChangeLog changes = session.Runner.Context.Changes;
            if (changes.Count > 0 || changes.Overflowed)
            {
                _animation.Consume(changes); // animated or instant; the simulation is already up to date
                changes.Clear();
                MarkStatusDirty();
            }
        }

        // ------------------------------------------------------------------------------------------------
        // Editing API
        // ------------------------------------------------------------------------------------------------

        /// <summary>Adds a component at <paramref name="world"/> (snapped when enabled) as one undoable step.</summary>
        public NodeId PlaceNode(ComponentRef component, Vector2 world, string displayName = null)
        {
            EnsureCircuit();
            if (!CheckEditable() || !CheckPlaceable(component))
            {
                return NodeId.None;
            }

            string boundaryName = null;
            if (component.Kind == ComponentKind.InputBoundary)
            {
                boundaryName = BoundaryNames.NextInputName(Circuit);
            }
            else if (component.Kind == ComponentKind.OutputBoundary)
            {
                boundaryName = BoundaryNames.NextOutputName(Circuit);
            }

            var command = new AddNodeCommand(Circuit, component, GridSnap.ToPoint(world, SnapToGrid),
                displayName ?? boundaryName ?? _geometry.TitleOf(component), boundaryName);
            History.Execute(command);
            _selection.Select(command.CreatedNode);
            return command.CreatedNode;
        }

        /// <summary>
        /// Checks whether two pins can be wired (in either order) and resolves which is the driver.
        /// </summary>
        public ConnectionError CheckConnection(PinRef a, PinRef b, out PinRef source, out PinRef destination)
        {
            source = a;
            destination = b;
            if (Circuit == null)
            {
                return ConnectionError.MissingSourceNode;
            }

            if (!IsEditable)
            {
                return ConnectionError.CircuitReadOnly;
            }

            if (!_geometry.TryGetPin(a, out _, out PortDirection directionA))
            {
                return ConnectionError.InvalidSourcePort;
            }

            if (!_geometry.TryGetPin(b, out _, out PortDirection directionB))
            {
                return ConnectionError.InvalidDestinationPort;
            }

            if (directionA == directionB)
            {
                return directionA == PortDirection.Output
                    ? ConnectionError.InvalidDestinationPort
                    : ConnectionError.InvalidSourcePort;
            }

            if (directionA == PortDirection.Input)
            {
                source = b;
                destination = a;
            }

            return ConnectionRules.Check(Circuit, _catalog, source, destination);
        }

        /// <summary>Wires two pins (either order). Returns the reason when the connection is rejected.</summary>
        public ConnectionError Connect(PinRef a, PinRef b)
        {
            EnsureCircuit();
            ConnectionError error = CheckConnection(a, b, out PinRef source, out PinRef destination);
            if (error == ConnectionError.None)
            {
                History.Execute(new ConnectCommand(Circuit, source, destination));
            }
            else if (error == ConnectionError.CircuitReadOnly)
            {
                NotifyReadOnly();
            }
            else if (error == ConnectionError.WidthMismatch)
            {
                Notify("Cannot connect a " + PinWidth(source, output: true) + "-bit pin to a " +
                    PinWidth(destination, output: false) + "-bit pin. Use a splitter or merger to change the width.");
            }

            return error;
        }

        private int PinWidth(PinRef pin, bool output)
        {
            if (Circuit == null || !Circuit.TryGetNode(pin.Node, out NodeDefinition node) ||
                !_catalog.TryGetSignature(node.Component, out ComponentSignature signature))
            {
                return 1;
            }

            return ConnectionRules.WidthOf(output ? signature.Outputs : signature.Inputs, pin.Port);
        }

        /// <summary>
        /// Flips an input boundary switch as an undoable step ("Change input value"). For a bus input,
        /// <paramref name="bit"/> selects the bit to flip (bit 0 = least significant). Returns false when the node
        /// is not a simulated input or inputs cannot be changed at this level.
        /// </summary>
        public bool ToggleInput(NodeId boundary, int bit = 0)
        {
            EnsureInitialized();
            if (!CanToggleInputs)
            {
                return false; // inside an inspected gate inputs are driven by the enclosing circuit
            }

            Sync(); // edits made earlier this frame must be compiled before the input can be addressed
            CircuitSession session = Session;
            if (!session.IsSimulating || !Circuit.TryGetNode(boundary, out NodeDefinition node) ||
                node.Component.Kind != ComponentKind.InputBoundary)
            {
                return false;
            }

            if (bit < 0 || bit >= node.Component.Width)
            {
                return false;
            }

            ulong next = session.GetInputBits(boundary) ^ (1UL << bit);
            string name = Circuit.TryGetInterfacePort(node.InterfacePort, out InterfacePort port) ? port.Name : null;
            History.Execute(new SetInputValueCommand(session, boundary, next, name));
            MarkStatusDirty();
            return true;
        }

        /// <summary>
        /// Sets every bit of an input switch as an undoable step (e.g. typing a value for a bus input). Bits above
        /// the input's width are ignored. Returns false when nothing changed or inputs cannot be changed here.
        /// </summary>
        public bool SetInputValue(NodeId boundary, ulong value)
        {
            EnsureInitialized();
            if (!CanToggleInputs)
            {
                return false;
            }

            Sync();
            CircuitSession session = Session;
            if (!session.IsSimulating || !Circuit.TryGetNode(boundary, out NodeDefinition node) ||
                node.Component.Kind != ComponentKind.InputBoundary)
            {
                return false;
            }

            int width = node.Component.Width;
            ulong mask = width >= 64 ? ulong.MaxValue : (1UL << width) - 1;
            value &= mask;
            if (session.GetInputBits(boundary) == value)
            {
                return false;
            }

            string name = Circuit.TryGetInterfacePort(node.InterfacePort, out InterfacePort port) ? port.Name : null;
            History.Execute(new SetInputValueCommand(session, boundary, value, name));
            MarkStatusDirty();
            return true;
        }

        /// <summary>Sets every bit of an input switch directly, without an undo step (loading projects, tools).</summary>
        public void SetInputBits(NodeId boundary, ulong value)
        {
            EnsureInitialized();
            if (!CanToggleInputs)
            {
                return;
            }

            Sync();
            if (Session.SetInputBits(boundary, value))
            {
                MarkStatusDirty();
            }
        }

        /// <summary>Sets an input switch directly, without an undo step (loading projects, tests, tools).</summary>
        public void SetInput(NodeId boundary, Signal value)
        {
            EnsureInitialized();
            if (!CanToggleInputs)
            {
                return;
            }

            Sync();
            if (Session.SetInput(boundary, value))
            {
                MarkStatusDirty();
            }
        }

        public bool DeleteSelection()
        {
            if (Circuit == null || _selection.IsEmpty || !CheckEditable())
            {
                return false;
            }

            PinRef[] wires = _selection.Wire.HasValue ? new[] { _selection.Wire.Value } : null;
            History.Execute(new RemoveNodesCommand(Circuit, _selection.ToSortedArray(), wires));
            _selection.Prune(Circuit);
            return true;
        }

        public void DeleteWire(PinRef destination)
        {
            if (Circuit != null && Circuit.IsDriven(destination) && CheckEditable())
            {
                History.Execute(new RemoveNodesCommand(Circuit, null, new[] { destination }));
                _selection.Prune(Circuit);
            }
        }

        public bool DuplicateSelection()
        {
            if (Circuit == null || _selection.NodeCount == 0 || !CheckEditable())
            {
                return false;
            }

            var command = new DuplicateNodesCommand(Circuit, _selection.ToSortedArray(), DuplicateOffset, -DuplicateOffset);
            History.Execute(command);
            _scratchNodes.Clear();
            for (int i = 0; i < command.Created.Count; i++)
            {
                _scratchNodes.Add(command.Created[i].Id);
            }

            _selection.SetNodes(_scratchNodes, additive: false);
            return true;
        }

        /// <summary>Rotates every selected node 90° counter-clockwise about its own centre.</summary>
        public bool RotateSelection()
        {
            if (Circuit == null || _selection.NodeCount == 0 || !CheckEditable())
            {
                return false;
            }

            NodeId[] nodes = _selection.ToSortedArray();
            TransformNodesCommand.CaptureTransforms(Circuit, nodes, out CircuitPoint[] positions, out Rotation[] from);
            var to = new Rotation[from.Length];
            for (int i = 0; i < from.Length; i++)
            {
                to[i] = GateLayout.Next(from[i]);
            }

            History.Execute(new TransformNodesCommand(nodes.Length == 1 ? "Rotate node" : "Rotate " + nodes.Length + " nodes",
                Circuit, nodes, positions, positions, from, to));
            return true;
        }

        /// <summary>True for components whose size or style can be changed after placement.</summary>
        public static bool IsResizable(ComponentRef component)
        {
            switch (component.Kind)
            {
                case ComponentKind.InputBoundary:
                case ComponentKind.OutputBoundary:
                case ComponentKind.Splitter:
                case ComponentKind.Merger:
                case ComponentKind.BusJoin:
                case ComponentKind.TriState:
                case ComponentKind.Display:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Builds a variant of <paramref name="current"/> with another width, bus-join driver count or display style
        /// (pass -1 to keep a parameter). Returns false with a user-facing reason when the values are out of range.
        /// </summary>
        public static bool TryBuildVariant(ComponentRef current, int width, int inputs, int style,
            out ComponentRef variant, out string error)
        {
            variant = current;
            error = null;
            width = width < 0 ? current.Width : width;
            try
            {
                switch (current.Kind)
                {
                    case ComponentKind.InputBoundary:
                        variant = ComponentRef.InputBus(width);
                        return true;
                    case ComponentKind.OutputBoundary:
                        variant = ComponentRef.OutputBus(width);
                        return true;
                    case ComponentKind.Splitter:
                        variant = ComponentRef.Splitter(width);
                        return true;
                    case ComponentKind.Merger:
                        variant = ComponentRef.Merger(width);
                        return true;
                    case ComponentKind.BusJoin:
                        variant = ComponentRef.BusJoin(width, inputs < 0 ? current.Arity : inputs);
                        return true;
                    case ComponentKind.TriState:
                        variant = ComponentRef.TriState(width);
                        return true;
                    case ComponentKind.Display:
                    {
                        DisplayStyle newStyle = style < 0 ? current.DisplayStyle : (DisplayStyle)style;
                        if (newStyle != current.DisplayStyle && style >= 0)
                        {
                            // A new style starts from a width it supports (keep the old one when it fits).
                            width = Mathf.Clamp(width, DisplayStyles.MinWidth(newStyle), DisplayStyles.MaxWidth(newStyle));
                        }

                        variant = ComponentRef.Display(newStyle, width);
                        if (newStyle == DisplayStyle.Led && current.DisplayStyle == DisplayStyle.Led)
                        {
                            variant = variant.WithLedColor(current.LedColor);
                        }

                        return true;
                    }
                    default:
                        error = "This component has no size or style to change.";
                        return false;
                }
            }
            catch (ArgumentOutOfRangeException exception)
            {
                string message = exception.Message;
                int newline = message.IndexOf('\n');
                error = newline >= 0 ? message.Substring(0, newline).Trim() : message;
                return false;
            }
        }

        /// <summary>
        /// Replaces a placed component with a variant of the same kind (width, bus-join drivers, display style) as
        /// one undoable step, keeping the node's id, position and every connection that still fits. Refusals and
        /// dropped connections are reported through <see cref="Notification"/>.
        /// </summary>
        public bool ChangeComponent(NodeId node, ComponentRef component)
        {
            if (Circuit == null || !CheckEditable() || !Circuit.TryGetNode(node, out NodeDefinition existing))
            {
                return false;
            }

            bool changeable = IsResizable(existing.Component) || existing.Component.Kind == ComponentKind.Clock;
            if (!changeable || existing.Component.Kind != component.Kind)
            {
                Notify("This component's size or style cannot be changed.");
                return false;
            }

            if (existing.Component == component)
            {
                return false;
            }

            string description = "Change " + DescribeComponent(existing.Component);
            var command = new ChangeComponentCommand(Circuit, _catalog, node, component, description);
            if (existing.Component.Kind == ComponentKind.InputBoundary)
            {
                // Narrowing an input switch truncates its value. A no-op value step placed first is undone last,
                // after the width is restored, so undo brings back every original bit.
                CircuitSession session = Session;
                session.RebuildIfDirty();
                ulong value = session.GetInputBits(node);
                History.Execute(new CompositeCommand(description, new ICommand[]
                {
                    new SetInputValueCommand(session, node, value, null),
                    command,
                }));
            }
            else
            {
                History.Execute(command);
            }

            _selection.Select(node);
            if (command.DroppedConnections > 0)
            {
                Notify(command.DroppedConnections + " connection" + (command.DroppedConnections == 1 ? "" : "s") +
                    " no longer fit and " + (command.DroppedConnections == 1 ? "was" : "were") +
                    " removed. Undo restores " + (command.DroppedConnections == 1 ? "it" : "them") + ".");
            }

            return true;
        }

        /// <summary>Changes the width, driver count or display style of a placed component (see <see cref="TryBuildVariant"/>).</summary>
        public bool ChangeComponent(NodeId node, int width, int inputs = -1, int style = -1)
        {
            if (Circuit == null || !Circuit.TryGetNode(node, out NodeDefinition existing))
            {
                return false;
            }

            if (!TryBuildVariant(existing.Component, width, inputs, style, out ComponentRef variant, out string error))
            {
                Notify(error);
                return false;
            }

            return ChangeComponent(node, variant);
        }

        /// <summary>Lights an LED display in another colour (see <see cref="LedColors"/>) as one undoable step.</summary>
        public bool ChangeLedColor(NodeId node, int color)
        {
            if (Circuit == null || !Circuit.TryGetNode(node, out NodeDefinition existing))
            {
                return false;
            }

            if (existing.Component.Kind != ComponentKind.Display || existing.Component.DisplayStyle != DisplayStyle.Led ||
                !LedColors.IsValid(color))
            {
                Notify("Only LED displays have a colour.");
                return false;
            }

            return ChangeComponent(node, existing.Component.WithLedColor(color));
        }

        /// <summary>
        /// Runs a clock at the base frequency divided by <paramref name="divider"/> (1, 2, 4 … 64) as one undoable
        /// step. Clocks with the same divider share a clock domain.
        /// </summary>
        public bool ChangeClockDivider(NodeId node, int divider)
        {
            if (Circuit == null || !Circuit.TryGetNode(node, out NodeDefinition existing))
            {
                return false;
            }

            if (existing.Component.Kind != ComponentKind.Clock)
            {
                Notify("Only clocks have a divider.");
                return false;
            }

            if (!ComponentRef.IsValidClockDivider(divider))
            {
                Notify("A clock divider must be a power of two from 1 to " + (1 << ComponentRef.MaxClockExponent) + ".");
                return false;
            }

            return ChangeComponent(node, existing.Component.WithClockDivider(divider));
        }

        private static string DescribeComponent(ComponentRef component)
        {
            switch (component.Kind)
            {
                case ComponentKind.Clock:
                    return "clock divider";
                case ComponentKind.Display:
                    return "display";
                case ComponentKind.BusJoin:
                    return "bus join";
                case ComponentKind.TriState:
                    return "tri-state width";
                case ComponentKind.Splitter:
                    return "splitter width";
                case ComponentKind.Merger:
                    return "merger width";
                default:
                    return "bus width";
            }
        }

        /// <summary>Records a move that the interaction already applied live (so undo restores <paramref name="from"/>).</summary>
        public void RecordMove(NodeId[] nodes, CircuitPoint[] from)
        {
            EnsureCircuit();
            if (!IsEditable)
            {
                return;
            }

            TransformNodesCommand.CaptureTransforms(Circuit, nodes, out CircuitPoint[] to, out Rotation[] rotations);
            History.Record(new TransformNodesCommand(nodes.Length == 1 ? "Move node" : "Move " + nodes.Length + " nodes",
                Circuit, nodes, from, to, rotations, rotations));
        }

        /// <summary>Moves nodes as one undoable step (used by non-interactive callers).</summary>
        public void MoveNodes(NodeId[] nodes, CircuitPoint[] to)
        {
            EnsureCircuit();
            if (!CheckEditable())
            {
                return;
            }

            TransformNodesCommand.CaptureTransforms(Circuit, nodes, out CircuitPoint[] from, out Rotation[] rotations);
            History.Execute(new TransformNodesCommand(nodes.Length == 1 ? "Move node" : "Move " + nodes.Length + " nodes",
                Circuit, nodes, from, to, rotations, rotations));
        }

        public void SelectAll()
        {
            if (Circuit == null)
            {
                return;
            }

            _scratchNodes.Clear();
            IReadOnlyList<NodeDefinition> nodes = Circuit.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                _scratchNodes.Add(nodes[i].Id);
            }

            _selection.SetNodes(_scratchNodes, additive: false);
        }

        public bool Undo()
        {
            CancelInteraction();
            bool undone = History.Undo();
            _selection.Prune(Circuit);
            return undone;
        }

        public bool Redo()
        {
            CancelInteraction();
            bool redone = History.Redo();
            _selection.Prune(Circuit);
            return redone;
        }

        /// <summary>Renames an external input/output of the current circuit (undoable).</summary>
        public void RenameInterfacePort(PortId port, string name)
        {
            EnsureCircuit();
            if (Circuit.TryGetInterfacePort(port, out InterfacePort existing) &&
                !string.Equals(existing.Name, name, StringComparison.Ordinal) && CheckEditable())
            {
                History.Execute(new RenameInterfacePortCommand(Circuit, port, name));
            }
        }

        /// <summary>Moves an external port within the interface order of the current circuit (undoable).</summary>
        public bool MoveInterfacePort(PortId port, int newIndex)
        {
            EnsureCircuit();
            int index = Circuit.IndexOfInput(port);
            int count = Circuit.Inputs.Count;
            if (index < 0)
            {
                index = Circuit.IndexOfOutput(port);
                count = Circuit.Outputs.Count;
            }

            if (index < 0 || newIndex < 0 || newIndex >= count || newIndex == index || !CheckEditable())
            {
                return false;
            }

            History.Execute(new MoveInterfacePortCommand(Circuit, port, newIndex));
            MarkStatusDirty();
            return true;
        }

        /// <summary>
        /// Renames a custom gate in the library (all versions; metadata only, so every instance keeps working).
        /// Recorded in the main circuit's history. Returns false for empty or unchanged names.
        /// </summary>
        public bool RenameCustomGate(DefinitionId id, string name)
        {
            EnsureInitialized();
            if (string.IsNullOrWhiteSpace(name) || !_library.TryGetLatest(id, out CircuitDefinition latest) ||
                string.Equals(latest.Name, name.Trim(), StringComparison.Ordinal))
            {
                return false;
            }

            _navigator.RootHistory.Execute(new RenameCustomGateCommand(_library, id, name));
            return true;
        }

        /// <summary>Changes name, description and category of the current circuit (undoable).</summary>
        public void SetCircuitProperties(string name, string description, string category)
        {
            EnsureCircuit();
            if ((Circuit.Name == name && Circuit.Description == description && Circuit.Category == category) ||
                !CheckEditable())
            {
                return;
            }

            History.Execute(new SetCircuitPropertiesCommand(Circuit, name, description, category));
            MarkStatusDirty();
        }

        // ------------------------------------------------------------------------------------------------
        // Custom gate packaging
        // ------------------------------------------------------------------------------------------------

        /// <summary>
        /// Default packaging spec: the selected nodes when <paramref name="selectionOnly"/> and something is
        /// selected, otherwise the whole circuit. The gate name is prefilled from the circuit name unless that is
        /// still the default.
        /// </summary>
        public PackagingSpec CreatePackagingSpec(bool selectionOnly)
        {
            EnsureCircuit();
            PackagingSpec spec = selectionOnly && _selection.NodeCount > 0
                ? PackagingSpec.FromCircuit(Circuit, _selection.Nodes)
                : PackagingSpec.FromCircuit(Circuit);
            spec.Name = Circuit.Name == DefaultCircuitName ? string.Empty : Circuit.Name;
            spec.Description = Circuit.Description;
            spec.Category = Circuit.Category;
            return spec;
        }

        /// <summary>Validates a spec without publishing anything (live feedback in the packaging dialog).</summary>
        public PackagingResult PreviewPackaging(PackagingSpec spec)
        {
            EnsureCircuit();
            return _packager.Package(Circuit, spec);
        }

        /// <summary>Why packaging is unavailable at the current level, or null.</summary>
        public string PackagingUnavailableReason =>
            CanPackage ? null : "Return to the main circuit to create a custom gate.";

        /// <summary>
        /// Packages the circuit (or selection) as a new custom gate and publishes it to the library as one undoable
        /// step. The original circuit is unchanged unless <paramref name="clearWorkspace"/> asks to remove the
        /// packaged nodes afterwards (undone together with the publication).
        /// </summary>
        public PackagingResult PackageCustomGate(PackagingSpec spec, bool clearWorkspace = false)
        {
            EnsureCircuit();
            if (!CanPackage)
            {
                var refused = new ValidationReport();
                refused.Add(new Diagnostic(DiagnosticSeverity.Error, DiagnosticCode.CircuitReadOnly,
                    PackagingUnavailableReason));
                return new PackagingResult(null, refused);
            }

            PackagingResult result = _packager.Package(Circuit, spec);
            if (!result.Succeeded)
            {
                return result;
            }

            var publish = new PublishDefinitionCommand(_library, result.Definition);
            if (!clearWorkspace)
            {
                History.Execute(publish);
                return result;
            }

            _scratchNodes.Clear();
            IReadOnlyList<NodeDefinition> nodes = Circuit.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (spec.Contains(nodes[i].Id))
                {
                    _scratchNodes.Add(nodes[i].Id);
                }
            }

            History.Execute(new CompositeCommand(publish.Description, new ICommand[]
            {
                publish,
                new RemoveNodesCommand(Circuit, _scratchNodes.ToArray()),
            }));
            _selection.Prune(Circuit);
            return result;
        }

        // ------------------------------------------------------------------------------------------------
        // Hierarchy navigation, edit mode and versioning
        // ------------------------------------------------------------------------------------------------

        /// <summary>True when <paramref name="node"/> is a custom gate whose internals can be opened.</summary>
        public bool CanOpenNode(NodeId node)
        {
            return IsInitialized && _navigator.CanOpen(node, out _, out _);
        }

        /// <summary>
        /// Navigates into the internal circuit of a custom gate (read-only, live signals of that instance).
        /// Refusals (missing definition, circular path, depth limit) are reported through <see cref="Notification"/>.
        /// </summary>
        public bool OpenNode(NodeId node)
        {
            EnsureInitialized();
            Sync();
            StoreCurrentView();
            if (_navigator.TryOpen(node, out string error))
            {
                Sync();
                return true;
            }

            Notify(error);
            return false;
        }

        /// <summary>Whether the published gate <paramref name="reference"/> can be opened from the library.</summary>
        public bool CanOpenDefinition(DefinitionRef reference)
        {
            return IsInitialized && _navigator.CanOpenDefinition(reference, out _, out _);
        }

        /// <summary>
        /// Opens a published gate from the library without placing it: a read-only preview with its own simulation,
        /// replacing any opened levels. Refusals are reported through <see cref="Notification"/>.
        /// </summary>
        public bool OpenDefinition(DefinitionRef reference)
        {
            EnsureInitialized();
            Sync();
            StoreCurrentView();
            if (_navigator.TryOpenDefinition(reference, out string error))
            {
                Sync();
                return true;
            }

            Notify(error);
            return false;
        }

        /// <summary>Opens the single selected custom gate.</summary>
        public bool OpenSelected()
        {
            if (_selection.NodeCount == 1)
            {
                foreach (NodeId node in _selection.Nodes)
                {
                    return OpenNode(node);
                }
            }

            Notify("Select one custom gate to open its internal circuit.");
            return false;
        }

        /// <summary>Breadcrumb navigation. Leaving an edited draft requires <paramref name="discardChanges"/>.</summary>
        public bool NavigateTo(int index, bool discardChanges = false)
        {
            EnsureInitialized();
            StoreCurrentView();
            if (_navigator.TryNavigateTo(index, out string error, discardChanges))
            {
                Sync();
                return true;
            }

            Notify(error);
            return false;
        }

        public bool NavigateUp()
        {
            return IsInitialized && _navigator.CurrentIndex > 0 && NavigateTo(_navigator.CurrentIndex - 1);
        }

        /// <summary>Switches the inspected gate to edit mode (a draft of its next version).</summary>
        public bool BeginEdit()
        {
            EnsureInitialized();
            StoreCurrentView();
            if (_navigator.TryBeginEdit(out string error))
            {
                Sync();
                Notify("Editing '" + Circuit.Name + "' as version " + Circuit.Version +
                    ". Existing instances are unaffected until you save.");
                return true;
            }

            Notify(error);
            return false;
        }

        /// <summary>Throws away the draft and returns to where editing started.</summary>
        public bool DiscardDraft()
        {
            EnsureInitialized();
            StoreCurrentView();
            if (!_navigator.DiscardDraft())
            {
                return false;
            }

            Sync();
            Notify("Changes discarded.");
            return true;
        }

        /// <summary>Validation of the current draft with the packaging rules (null when nothing is being edited).</summary>
        public ValidationReport ValidateDraft()
        {
            NavigationLevel draft = _navigator?.Draft;
            return draft == null ? null : _packager.ValidateDefinition(draft.Circuit);
        }

        /// <summary>What saving the draft would affect under each update mode (null when nothing is being edited).</summary>
        public VersionImpact AnalyzeDraftSave()
        {
            NavigationLevel draft = _navigator?.Draft;
            return draft == null
                ? null
                : DefinitionVersioning.AnalyzeImpact(_library, draft.Circuit, draft.DraftSource, RootCircuit);
        }

        /// <summary>
        /// Publishes the draft as a new version and, per <paramref name="mode"/>, switches references to it, all as
        /// one undoable step on the main circuit's history. Older versions stay in the library, so circuits that keep
        /// them remain valid. Afterwards the main circuit is shown.
        /// </summary>
        public DraftSaveResult SaveDraft(VersionUpdateMode mode)
        {
            EnsureInitialized();
            NavigationLevel draft = _navigator.Draft;
            if (draft == null)
            {
                return DraftSaveResult.Failed("No gate is being edited.");
            }

            if (!draft.HasUnsavedChanges)
            {
                return DraftSaveResult.Failed("There are no changes to save.");
            }

            ValidationReport report = _packager.ValidateDefinition(draft.Circuit);
            if (report.HasErrors)
            {
                return DraftSaveResult.Failed("Fix the errors before saving.", report);
            }

            VersionPlan plan = DefinitionVersioning.Plan(_library, draft.Circuit, draft.DraftSource,
                cascade: mode == VersionUpdateMode.UpdateEverywhere);
            var commands = new List<ICommand>();
            foreach (CircuitDefinition definition in plan.AllNewDefinitions())
            {
                commands.Add(new PublishDefinitionCommand(_library, definition,
                    "Publish '" + definition.Name + "' v" + definition.Version));
            }

            RetargetReferencesCommand retarget = null;
            if (mode != VersionUpdateMode.KeepReferences && RootCircuit != null)
            {
                retarget = new RetargetReferencesCommand(RootCircuit, plan.Map, plan.SignatureOf);
                commands.Add(retarget);
            }

            CircuitDefinition saved = plan.Draft;
            _navigator.ReturnToRoot();
            _navigator.RootHistory.Execute(new CompositeCommand("Save '" + saved.Name + "' v" + saved.Version, commands));
            Sync();

            int dropped = plan.DroppedConnections + (retarget?.DroppedConnections ?? 0);
            var result = new DraftSaveResult(report, null, plan, dropped, retarget?.RetargetedNodes ?? 0);
            string message = "Saved '" + saved.Name + "' v" + saved.Version;
            if (plan.Cascaded.Count > 0)
            {
                message += " and " + plan.Cascaded.Count + " dependent gate" + (plan.Cascaded.Count == 1 ? "" : "s");
            }

            message += mode == VersionUpdateMode.KeepReferences
                ? ". Existing instances keep the previous version."
                : ". Updated " + result.RetargetedNodes + " instance" + (result.RetargetedNodes == 1 ? "" : "s") + ".";
            if (dropped > 0)
            {
                message += " " + dropped + " connection" + (dropped == 1 ? " was" : "s were") + " removed (pins no longer exist).";
            }

            Notify(message);
            return result;
        }

        /// <summary>
        /// Right-click without dragging: selects what is under the pointer (keeping an existing multi-selection that
        /// contains it) and raises <see cref="ContextMenuRequested"/>.
        /// </summary>
        public void RequestContextMenu(Vector2 screen, Vector2 world)
        {
            EnsureInitialized();
            NodeId node = NodeId.None;
            PinRef? wire = null;
            if (_hitTester.TryHitNode(world, out NodeId hit))
            {
                node = hit;
                if (!_selection.Contains(hit))
                {
                    _selection.Select(hit);
                }
            }
            else if (_hitTester.TryHitWire(world, 6f * _camera.WorldPerPixel, out PinRef hitWire))
            {
                wire = hitWire;
                _selection.SelectWire(hitWire);
            }

            ContextMenuRequested?.Invoke(new ContextMenuRequest(screen, node, wire));
        }

        // ------------------------------------------------------------------------------------------------
        // Placement
        // ------------------------------------------------------------------------------------------------

        /// <summary>
        /// Enters placement mode: a ghost follows the pointer. A click places the component (Shift keeps placing).
        /// With <paramref name="fromDrag"/> (dragged from the library), releasing the button places it.
        /// </summary>
        public void BeginPlacement(PlacementRequest request, bool fromDrag)
        {
            EnsureCircuit();
            if (!CheckEditable() || !CheckPlaceable(request.Component))
            {
                return;
            }

            _interaction?.Cancel();
            _placement = request;
            _placementFromDrag = fromDrag;
            string boundaryTitle = request.Component.Kind == ComponentKind.InputBoundary
                ? BoundaryNames.NextInputName(Circuit)
                : request.Component.Kind == ComponentKind.OutputBoundary
                    ? BoundaryNames.NextOutputName(Circuit)
                    : null;
            _placementGeometry = _geometry.ComputeDetached(request.Component, boundaryTitle);
            PlacementChanged?.Invoke();
        }

        public void UpdatePlacementGhost(Vector2 world, bool visible)
        {
            if (!_placement.HasValue)
            {
                return;
            }

            if (!visible)
            {
                _presenter.HideGhost();
                return;
            }

            _presenter.ShowGhost(_placementGeometry, SnapToGrid ? GridSnap.Snap(world) : world, valid: true);
        }

        /// <summary>Places the pending component at <paramref name="world"/>; ends placement unless <paramref name="keepPlacing"/>.</summary>
        public NodeId CommitPlacement(Vector2 world, bool keepPlacing)
        {
            if (!_placement.HasValue)
            {
                return NodeId.None;
            }

            PlacementRequest request = _placement.Value;
            NodeId node = PlaceNode(request.Component, world, request.DisplayName);
            if (keepPlacing)
            {
                // Refresh boundary names (A → B) for the next ghost.
                BeginPlacement(request, fromDrag: false);
            }
            else
            {
                CancelPlacement();
            }

            return node;
        }

        public void CancelPlacement()
        {
            if (!_placement.HasValue)
            {
                return;
            }

            _placement = null;
            _placementGeometry = null;
            _placementFromDrag = false;
            _presenter.HideGhost();
            PlacementChanged?.Invoke();
        }

        // ------------------------------------------------------------------------------------------------
        // Simulation controls
        // ------------------------------------------------------------------------------------------------

        public void Play()
        {
            if (Session.IsSimulating)
            {
                Session.Runner.Play();
            }
        }

        public void Pause()
        {
            Session.Runner.Pause();
        }

        public void TogglePlayPause()
        {
            if (Session.Runner.State == SimulationState.Running)
            {
                Pause();
            }
            else
            {
                Play();
            }
        }

        /// <summary>Propagates one wave (meaningful while paused).</summary>
        public bool Step()
        {
            if (!Session.IsSimulating)
            {
                return false;
            }

            bool more = Session.Runner.Step();
            MarkStatusDirty();
            return more;
        }

        public void ResetSimulation()
        {
            if (Session.IsSimulating)
            {
                Session.Runner.Reset();
                MarkStatusDirty();
            }
        }

        /// <summary>Drops running signal animations and redraws every view from the simulation.</summary>
        public void ResetVisualState()
        {
            EnsureInitialized();
            _animation.ResetVisualState();
        }

        // ------------------------------------------------------------------------------------------------
        // Camera
        // ------------------------------------------------------------------------------------------------

        public void ZoomIn()
        {
            _camera.ZoomIn();
        }

        public void ZoomOut()
        {
            _camera.ZoomOut();
        }

        public void ResetCamera()
        {
            _camera.ResetView();
        }

        /// <summary>
        /// Screen-pixel rect of the workspace area not covered by UI panels, supplied by the UI. Framing centres
        /// content in this area. Null means the whole screen.
        /// </summary>
        public Func<Rect> VisibleScreenArea { get; set; }

        private void FrameWorld(Rect bounds)
        {
            if (VisibleScreenArea != null)
            {
                _camera.Frame(bounds, VisibleScreenArea());
            }
            else
            {
                _camera.Frame(bounds);
            }
        }

        /// <summary>Frames the selection, or all content when nothing is selected.</summary>
        public void FrameContent()
        {
            if (_selection.NodeCount > 0)
            {
                Rect bounds = default;
                bool first = true;
                foreach (NodeId node in _selection.Nodes)
                {
                    Rect r = _geometry.GetBounds(node);
                    bounds = first
                        ? r
                        : Rect.MinMaxRect(Mathf.Min(bounds.xMin, r.xMin), Mathf.Min(bounds.yMin, r.yMin),
                            Mathf.Max(bounds.xMax, r.xMax), Mathf.Max(bounds.yMax, r.yMax));
                    first = false;
                }

                FrameWorld(bounds);
            }
            else if (_hitTester.TryGetContentBounds(out Rect all))
            {
                FrameWorld(all);
            }
            else
            {
                _camera.ResetView();
            }
        }

        // ------------------------------------------------------------------------------------------------
        // Environment queries (used by the interaction layer)
        // ------------------------------------------------------------------------------------------------

        internal bool IsPointerOverUi()
        {
            if (_setup.IsPointerOverUi != null)
            {
                return _setup.IsPointerOverUi();
            }

            EventSystem eventSystem = EventSystem.current;
            return eventSystem != null && eventSystem.IsPointerOverGameObject();
        }

        internal bool IsTextInputFocused()
        {
            return _setup.IsTextInputFocused != null && _setup.IsTextInputFocused();
        }

        /// <summary>Aborts any in-progress pointer gesture (drag, wiring, rectangle, placement).</summary>
        public void CancelInteraction()
        {
            _interaction?.Cancel();
            CancelPlacement();
        }

        private void OnLibraryChanged(CircuitDefinition definition)
        {
            // Instances may have become resolvable (redo) or unresolvable (undo of a publication).
            _navigator?.MarkAllDirty();
            MarkStatusDirty();
        }

        private void OnLibraryMetadataChanged(CircuitDefinition definition)
        {
            // Gate titles come from definition names: rebuild the views (cheap; no recompilation needed).
            if (_shownLevel != null)
            {
                _presenter.Bind(_shownLevel.Circuit);
                _presenter.SetSelection(_selection.Nodes, _selection.Wire);
                _scopeDirty = true;
            }

            NavigationChanged?.Invoke();
            MarkStatusDirty();
        }

        private void OnLevelsChanged()
        {
            CancelInteraction();
            _selection.Clear();
            NavigationLevel level = _navigator.Current;
            if (level == _shownLevel)
            {
                return;
            }

            bool firstShow = _shownLevel == null;
            _shownLevel = level;
            _presenter.Bind(level?.Circuit);
            _presenter.SetSelection(_selection.Nodes, _selection.Wire);
            _scopeDirty = true;
            if (level != null && !firstShow)
            {
                if (level.HasView)
                {
                    _camera.MoveTo(new Vector2(level.ViewX, level.ViewY), level.ViewSize);
                }
                else if (level.Kind != NavigationLevelKind.Root)
                {
                    FrameContent();
                }
            }

            MarkStatusDirty();
            NavigationChanged?.Invoke();
        }

        private void StoreCurrentView()
        {
            NavigationLevel level = _navigator.Current;
            if (level != null && _camera != null && _camera.Camera != null)
            {
                level.StoreView(_camera.Position.x, _camera.Position.y, _camera.Size);
            }
        }

        private bool CheckEditable()
        {
            if (IsEditable)
            {
                return true;
            }

            NotifyReadOnly();
            return false;
        }

        private void NotifyReadOnly()
        {
            Notify("'" + Circuit?.Name + "' is open for inspection only. Click Edit to change its implementation.");
        }

        /// <summary>Refuses custom gates that would make the edited draft contain itself.</summary>
        private bool CheckPlaceable(ComponentRef component)
        {
            NavigationLevel level = CurrentLevel;
            if (level == null || !level.IsDraft || component.Kind != ComponentKind.Custom)
            {
                return true;
            }

            if (!DefinitionVersioning.DependsOn(_library, component.Definition, level.Circuit.Id))
            {
                return true;
            }

            Notify("'" + _geometry.TitleOf(component) + "' contains '" + level.Circuit.Name +
                "', so it cannot be placed inside it (circular reference).");
            return false;
        }

        /// <summary>Shows a transient message in the status bar (used by panels such as the truth table).</summary>
        public void PostNotification(string message)
        {
            Notify(message);
        }

        private void Notify(string message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                Notification?.Invoke(message);
            }
        }

        private void OnHistoryChanged()
        {
            MarkStatusDirty();
        }

        private void OnSelectionChanged()
        {
            _presenter.SetSelection(_selection.Nodes, _selection.Wire);
            MarkStatusDirty();
        }

        private void MarkStatusDirty()
        {
            _statusDirty = true;
        }

        private void EnsureInitialized()
        {
            if (!IsInitialized)
            {
                throw new InvalidOperationException("The workspace has not been initialized.");
            }
        }

        private void EnsureCircuit()
        {
            EnsureInitialized();
            if (Circuit == null)
            {
                throw new InvalidOperationException("No circuit is loaded in the workspace.");
            }
        }
    }
}
