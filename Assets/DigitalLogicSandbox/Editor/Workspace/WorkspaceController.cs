using System;
using System.Collections.Generic;
using DigitalLogicSandbox.CircuitEditor.CameraControl;
using DigitalLogicSandbox.CircuitEditor.Interaction;
using DigitalLogicSandbox.CircuitEditor.Placement;
using DigitalLogicSandbox.CircuitEditor.Selection;
using DigitalLogicSandbox.Commands.Circuit;
using DigitalLogicSandbox.Commands.UndoRedo;
using DigitalLogicSandbox.Components.Abstractions;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.Core.Signals;
using DigitalLogicSandbox.Core.Validation;
using DigitalLogicSandbox.Rendering;
using DigitalLogicSandbox.Rendering.Gates;
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
    /// </summary>
    public sealed class WorkspaceController : MonoBehaviour
    {
        public const float DuplicateOffset = 1f;

        private readonly CommandHistory _history = new CommandHistory();
        private readonly SelectionModel _selection = new SelectionModel();
        private readonly List<NodeId> _scratchNodes = new List<NodeId>();

        private WorkspaceSetup _setup;
        private ComponentCatalog _catalog;
        private CircuitSession _session;
        private CircuitGeometry _geometry;
        private CircuitPresenter _presenter;
        private WorkspaceCamera _camera;
        private WorkspaceHitTester _hitTester;
        private WorkspaceInteraction _interaction;
        private GridView _grid;
        private SelectionRectView _selectionRect;
        private PlacementRequest? _placement;
        private GateGeometry _placementGeometry;
        private bool _placementFromDrag;
        private bool _statusDirty;

        /// <summary>Raised (at most once per frame) when anything shown in status or toolbar UI may have changed.</summary>
        public event Action StatusChanged;

        /// <summary>Raised when placement mode starts or ends.</summary>
        public event Action PlacementChanged;

        public bool IsInitialized => _session != null;

        public CircuitDefinition Circuit => _session?.Circuit;

        public CircuitSession Session => _session;

        public SimulationRunner Runner => _session?.Runner;

        public CommandHistory History => _history;

        public SelectionModel Selection => _selection;

        public CircuitPresenter Presenter => _presenter;

        public CircuitGeometry Geometry => _geometry;

        public WorkspaceCamera WorkspaceCamera => _camera;

        public WorkspaceHitTester HitTester => _hitTester;

        public VisualTheme Theme => _setup?.Theme;

        public ComponentCatalog Catalog => _catalog;

        public SelectionRectView SelectionRect => _selectionRect;

        public bool SnapToGrid { get; set; } = true;

        public bool IsPlacing => _placement.HasValue;

        public PlacementRequest? Placement => _placement;

        internal bool PlacementFromDrag => _placementFromDrag;

        public bool GridVisible
        {
            get => _grid != null && _grid.Visible;
            set
            {
                if (_grid != null)
                {
                    _grid.Visible = value;
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
            if (setup.Theme == null)
            {
                throw new ArgumentException("A visual theme is required.", nameof(setup));
            }

            if (setup.Camera == null)
            {
                throw new ArgumentException("A camera is required.", nameof(setup));
            }

            setup.Theme.EnsureMaterials();
            _session = new CircuitSession(_catalog, setup.SimulationOptions);
            _session.Rebuilt += MarkStatusDirty;
            _session.Runner.StateChanged += _ => MarkStatusDirty();
            _history.Changed += OnHistoryChanged;
            _selection.Changed += OnSelectionChanged;

            _geometry = new CircuitGeometry(_catalog);
            _presenter = new CircuitPresenter(transform, setup.Theme, _geometry);
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
            _history.Clear();
            _selection.Clear();
            _session.Bind(circuit);
            _presenter.Bind(circuit);
            Sync();
            MarkStatusDirty();
        }

        private void Update()
        {
            _interaction?.Tick();
        }

        private void LateUpdate()
        {
            if (!IsInitialized)
            {
                return;
            }

            Sync();
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

            if (_camera != null && _camera.Camera != null)
            {
                _camera.StopAnimations();
            }

            _presenter?.Dispose();
            _session?.Dispose();
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
            if (_session.RebuildIfDirty())
            {
                if (_session.IsSimulating)
                {
                    _presenter.SetSimulation(_session.Runner.Context, _session.Compiled.Root);
                    _session.Runner.Context.Changes.Clear();
                }
                else
                {
                    _presenter.ClearSimulation();
                }

                return;
            }

            if (!_session.IsSimulating)
            {
                return;
            }

            SignalChangeLog changes = _session.Runner.Context.Changes;
            if (changes.Count > 0 || changes.Overflowed)
            {
                _presenter.ApplyChanges(changes);
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
            _history.Execute(command);
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
                _history.Execute(new ConnectCommand(Circuit, source, destination));
            }

            return error;
        }

        /// <summary>Flips an input boundary switch (live simulation state; not part of the undo history).</summary>
        public bool ToggleInput(NodeId boundary)
        {
            EnsureInitialized();
            Sync(); // edits made earlier this frame must be compiled before the input can be addressed
            bool toggled = _session.ToggleInput(boundary);
            if (toggled)
            {
                MarkStatusDirty();
            }

            return toggled;
        }

        public void SetInput(NodeId boundary, Signal value)
        {
            EnsureInitialized();
            Sync();
            if (_session.SetInput(boundary, value))
            {
                MarkStatusDirty();
            }
        }

        public bool DeleteSelection()
        {
            if (Circuit == null || _selection.IsEmpty)
            {
                return false;
            }

            PinRef[] wires = _selection.Wire.HasValue ? new[] { _selection.Wire.Value } : null;
            _history.Execute(new RemoveNodesCommand(Circuit, _selection.ToSortedArray(), wires));
            _selection.Prune(Circuit);
            return true;
        }

        public void DeleteWire(PinRef destination)
        {
            if (Circuit != null && Circuit.IsDriven(destination))
            {
                _history.Execute(new RemoveNodesCommand(Circuit, null, new[] { destination }));
                _selection.Prune(Circuit);
            }
        }

        public bool DuplicateSelection()
        {
            if (Circuit == null || _selection.NodeCount == 0)
            {
                return false;
            }

            var command = new DuplicateNodesCommand(Circuit, _selection.ToSortedArray(), DuplicateOffset, -DuplicateOffset);
            _history.Execute(command);
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
            if (Circuit == null || _selection.NodeCount == 0)
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

            _history.Execute(new TransformNodesCommand(nodes.Length == 1 ? "Rotate node" : "Rotate " + nodes.Length + " nodes",
                Circuit, nodes, positions, positions, from, to));
            return true;
        }

        /// <summary>Records a move that the interaction already applied live (so undo restores <paramref name="from"/>).</summary>
        public void RecordMove(NodeId[] nodes, CircuitPoint[] from)
        {
            EnsureCircuit();
            TransformNodesCommand.CaptureTransforms(Circuit, nodes, out CircuitPoint[] to, out Rotation[] rotations);
            _history.Record(new TransformNodesCommand(nodes.Length == 1 ? "Move node" : "Move " + nodes.Length + " nodes",
                Circuit, nodes, from, to, rotations, rotations));
        }

        /// <summary>Moves nodes as one undoable step (used by non-interactive callers).</summary>
        public void MoveNodes(NodeId[] nodes, CircuitPoint[] to)
        {
            EnsureCircuit();
            TransformNodesCommand.CaptureTransforms(Circuit, nodes, out CircuitPoint[] from, out Rotation[] rotations);
            _history.Execute(new TransformNodesCommand(nodes.Length == 1 ? "Move node" : "Move " + nodes.Length + " nodes",
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
            bool undone = _history.Undo();
            _selection.Prune(Circuit);
            return undone;
        }

        public bool Redo()
        {
            CancelInteraction();
            bool redone = _history.Redo();
            _selection.Prune(Circuit);
            return redone;
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
            if (_session.IsSimulating)
            {
                _session.Runner.Play();
            }
        }

        public void Pause()
        {
            _session.Runner.Pause();
        }

        public void TogglePlayPause()
        {
            if (_session.Runner.State == SimulationState.Running)
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
            if (!_session.IsSimulating)
            {
                return false;
            }

            bool more = _session.Runner.Step();
            MarkStatusDirty();
            return more;
        }

        public void ResetSimulation()
        {
            if (_session.IsSimulating)
            {
                _session.Runner.Reset();
                MarkStatusDirty();
            }
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

                _camera.Frame(bounds);
            }
            else if (_hitTester.TryGetContentBounds(out Rect all))
            {
                _camera.Frame(all);
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
