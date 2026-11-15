using System;
using System.Collections.Generic;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.Core.Signals;
using DigitalLogicSandbox.Rendering.Gates;
using DigitalLogicSandbox.Rendering.Wires;
using DigitalLogicSandbox.Simulation.Context;
using DigitalLogicSandbox.Simulation.Engine;
using DigitalLogicSandbox.Simulation.Propagation;
using UnityEngine;

namespace DigitalLogicSandbox.Rendering
{
    /// <summary>
    /// Keeps pooled views in sync with one circuit scope of the model (structure, via
    /// <see cref="CircuitDefinition.Changed"/>) and with the live simulation (signals, via the change log).
    /// Only affected views are touched. The presenter never mutates the model or the simulation.
    /// </summary>
    public sealed class CircuitPresenter : IDisposable
    {
        private readonly VisualTheme _theme;
        private readonly CircuitGeometry _geometry;
        private readonly Transform _gateRoot;
        private readonly Transform _wireRoot;
        private readonly ViewPool<GateView> _gatePool;
        private readonly ViewPool<WireView> _wirePool;
        private readonly Dictionary<NodeId, GateView> _gates = new Dictionary<NodeId, GateView>();
        private readonly Dictionary<PinRef, WireView> _wires = new Dictionary<PinRef, WireView>();
        private readonly Dictionary<int, List<NetTarget>> _netTargets = new Dictionary<int, List<NetTarget>>();
        private readonly Dictionary<PinRef, int[]> _busWireNets = new Dictionary<PinRef, int[]>();
        private readonly Dictionary<NodeId, int[]> _displayNets = new Dictionary<NodeId, int[]>();
        private readonly HashSet<NodeId> _selectedNodes = new HashSet<NodeId>();
        private readonly List<PinRef> _highlightedPins = new List<PinRef>();
        private readonly List<PinRef> _scratchReroutes = new List<PinRef>();
        private readonly HashSet<PinRef> _junctionSources = new HashSet<PinRef>();
        private readonly List<ConnectionDefinition> _scratchSiblings = new List<ConnectionDefinition>();

        /// <summary>A shared trunk shorter than this (world units) gets no junction dot (the pin marks it).</summary>
        private const float MinJunctionTrunk = 0.2f;
        private readonly SignalStyle _style;

        private CircuitDefinition _circuit;
        private SimulationContext _context;
        private CompiledInstance _scope;
        private PinRef? _selectedWire;
        private PinRef? _hoveredWire;
        private NodeId _hoveredNode;
        private WireView _preview;
        private GateView _ghost;

        public CircuitPresenter(Transform root, VisualTheme theme, CircuitGeometry geometry)
        {
            _theme = theme ?? throw new ArgumentNullException(nameof(theme));
            _geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
            _wireRoot = new GameObject("Wires").transform;
            _wireRoot.SetParent(root, false);
            _gateRoot = new GameObject("Gates").transform;
            _gateRoot.SetParent(root, false);
            _style = new SignalStyle(theme);
            _gatePool = new ViewPool<GateView>(() => GateView.Create(_gateRoot, _theme));
            _wirePool = new ViewPool<WireView>(() => WireView.Create(_wireRoot, _theme, _style));
        }

        /// <summary>
        /// Raised after every full signal refresh (new simulation or scope, detach, overflow, rebind). Animation
        /// layers drop in-flight transitions then, because the views already show the true values.
        /// </summary>
        public event Action SignalsRefreshed;

        public CircuitGeometry Geometry => _geometry;

        /// <summary>True while views are bound to a simulation (signals can change).</summary>
        public bool HasSimulation => _context != null && _scope != null;

        public SignalVisualizationMode VisualizationMode => _style.Mode;

        public Transform Root => _wireRoot.parent;

        public CircuitDefinition Circuit => _circuit;

        public int GateViewCount => _gates.Count;

        public int WireViewCount => _wires.Count;

        /// <summary>
        /// Diagnostics: wire views rerouted after gates moved, rotated, appeared or disappeared (since the last
        /// reset).
        /// </summary>
        public int WireRerouteCount { get; private set; }

        /// <summary>Diagnostics: nets whose value was pushed to views (since the last reset).</summary>
        public int NetSignalUpdateCount { get; private set; }

        /// <summary>Resets the diagnostic counters (profiling and tests).</summary>
        public void ResetDiagnostics()
        {
            WireRerouteCount = 0;
            NetSignalUpdateCount = 0;
        }

        public bool TryGetGateView(NodeId node, out GateView view)
        {
            return _gates.TryGetValue(node, out view);
        }

        public bool TryGetWireView(PinRef destination, out WireView view)
        {
            return _wires.TryGetValue(destination, out view);
        }

        // ------------------------------------------------------------------------------------------------
        // Structure
        // ------------------------------------------------------------------------------------------------

        /// <summary>Shows <paramref name="circuit"/>; releases all views of the previous circuit.</summary>
        public void Bind(CircuitDefinition circuit)
        {
            if (_circuit != null)
            {
                _circuit.Changed -= OnCircuitChanged;
            }

            ReleaseAll();
            _circuit = circuit;
            _geometry.Bind(circuit);
            _context = null;
            _scope = null;
            SignalsRefreshed?.Invoke();
            if (circuit == null)
            {
                return;
            }

            circuit.Changed += OnCircuitChanged;
            IReadOnlyList<NodeDefinition> nodes = circuit.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                CreateGateView(nodes[i].Id);
            }

            IReadOnlyList<ConnectionDefinition> connections = circuit.Connections;
            for (int i = 0; i < connections.Count; i++)
            {
                CreateWireView(connections[i].Destination);
                _junctionSources.Add(connections[i].Source);
            }

            _scratchReroutes.Clear();
            _geometry.DrainInvalidatedWires(_scratchReroutes); // nothing to reroute on a fresh bind
            UpdateJunctions();
        }

        private void OnCircuitChanged(CircuitChange change)
        {
            _geometry.OnChanged(change);
            switch (change.Kind)
            {
                case CircuitChangeKind.NodeAdded:
                    CreateGateView(change.Node);
                    break;

                case CircuitChangeKind.NodeRemoved:
                    if (_gates.TryGetValue(change.Node, out GateView removed))
                    {
                        _gates.Remove(change.Node);
                        _selectedNodes.Remove(change.Node);
                        _gatePool.Release(removed);
                    }

                    break;

                case CircuitChangeKind.NodeMoved:
                    if (_gates.TryGetValue(change.Node, out GateView moved))
                    {
                        moved.SetPosition(_geometry.GetPosition(change.Node));
                    }

                    break;

                case CircuitChangeKind.NodeRotated:
                case CircuitChangeKind.InterfacePortRenamed:
                    if (_gates.TryGetValue(change.Node, out GateView reconfigured) && _circuit.ContainsNode(change.Node))
                    {
                        reconfigured.Configure(change.Node, _geometry.GetGate(change.Node));
                        reconfigured.SetSelected(_selectedNodes.Contains(change.Node));
                        RefreshNodeSignals(change.Node);
                    }

                    break;

                case CircuitChangeKind.Connected:
                    CreateWireView(change.Connection.Destination);
                    _junctionSources.Add(change.Connection.Source);
                    break;

                case CircuitChangeKind.Disconnected:
                    if (_wires.TryGetValue(change.Connection.Destination, out WireView wire))
                    {
                        _wires.Remove(change.Connection.Destination);
                        if (_selectedWire == change.Connection.Destination)
                        {
                            _selectedWire = null;
                        }

                        _wirePool.Release(wire);
                    }

                    _junctionSources.Add(change.Connection.Source);
                    break;
            }

            RerouteInvalidatedWires();
            UpdateJunctions();
        }

        private void CreateGateView(NodeId node)
        {
            GateView view = _gatePool.Get();
            view.SetGhost(false);
            view.Configure(node, _geometry.GetGate(node));
            view.SetPosition(_geometry.GetPosition(node));
            view.name = "Gate " + node;
            _gates[node] = view;
        }

        private void CreateWireView(PinRef destination)
        {
            WireView view = _wirePool.Get();
            view.ResetState();
            view.Destination = destination;
            view.IsBus = _geometry.GetConnectionWidth(destination) > 1;
            view.SetPath(_geometry.GetWirePath(destination));
            view.name = "Wire -> " + destination;
            _wires[destination] = view;
        }

        /// <summary>
        /// Switches wire routing to the fast candidate router while gates are being dragged. Turning it off reroutes
        /// the wires that were routed the fast way with the maze router (see <see cref="CircuitGeometry"/>).
        /// </summary>
        public void SetInteractiveRouting(bool interactive)
        {
            if (_geometry.InteractiveRouting == interactive)
            {
                return;
            }

            _geometry.InteractiveRouting = interactive;
            if (!interactive && _circuit != null)
            {
                RerouteInvalidatedWires();
                UpdateJunctions();
            }
        }

        /// <summary>
        /// Reroutes the wire views whose cached route the geometry dropped: wires of a moved or reshaped gate, and
        /// wires a gate now blocks or no longer blocks. Each view is rerouted at most once per change.
        /// </summary>
        private void RerouteInvalidatedWires()
        {
            _scratchReroutes.Clear();
            _geometry.DrainInvalidatedWires(_scratchReroutes);
            for (int i = 0; i < _scratchReroutes.Count; i++)
            {
                PinRef destination = _scratchReroutes[i];
                if (_wires.TryGetValue(destination, out WireView view))
                {
                    view.SetPath(_geometry.GetWirePath(destination));
                    WireRerouteCount++;
                    if (_circuit.TryGetConnectionTo(destination, out ConnectionDefinition connection))
                    {
                        _junctionSources.Add(connection.Source);
                    }
                }
            }
        }

        /// <summary>
        /// Places junction dots for every source pin whose wires changed. Fan-out wires from one output share their
        /// first stretch (drawn as one merged trunk); a dot marks the point where each wire leaves that trunk.
        /// </summary>
        private void UpdateJunctions()
        {
            if (_junctionSources.Count == 0 || _circuit == null)
            {
                return;
            }

            // One pass over the connections collects every wire of an affected source; sorting by source hash makes
            // each source's wires contiguous (hash collisions only widen a run; the source check below handles it).
            _scratchSiblings.Clear();
            IReadOnlyList<ConnectionDefinition> connections = _circuit.Connections;
            for (int i = 0; i < connections.Count; i++)
            {
                if (_junctionSources.Contains(connections[i].Source))
                {
                    _scratchSiblings.Add(connections[i]);
                }
            }

            _junctionSources.Clear();
            _scratchSiblings.Sort(SourceHashComparer.Instance);

            int runStart = 0;
            while (runStart < _scratchSiblings.Count)
            {
                int hash = _scratchSiblings[runStart].Source.GetHashCode();
                int runEnd = runStart + 1;
                while (runEnd < _scratchSiblings.Count && _scratchSiblings[runEnd].Source.GetHashCode() == hash)
                {
                    runEnd++;
                }

                for (int i = runStart; i < runEnd; i++)
                {
                    if (!_wires.TryGetValue(_scratchSiblings[i].Destination, out WireView view))
                    {
                        continue;
                    }

                    PinRef source = _scratchSiblings[i].Source;
                    IReadOnlyList<Vector3> path = _geometry.GetWirePath(_scratchSiblings[i].Destination);
                    float trunk = 0f;
                    for (int j = runStart; j < runEnd; j++)
                    {
                        if (j != i && _scratchSiblings[j].Source == source)
                        {
                            trunk = Mathf.Max(trunk,
                                SharedPrefixLength(path, _geometry.GetWirePath(_scratchSiblings[j].Destination)));
                        }
                    }

                    bool visible = trunk >= MinJunctionTrunk && trunk < PathLength(path) - MinJunctionTrunk;
                    view.SetJunction(visible, visible ? PointAlong(path, trunk) : default);
                }

                runStart = runEnd;
            }
        }

        /// <summary>Length of the common start of two polylines that begin at the same point.</summary>
        internal static float SharedPrefixLength(IReadOnlyList<Vector3> a, IReadOnlyList<Vector3> b)
        {
            if (a.Count < 2 || b.Count < 2 || ((Vector2)(a[0] - b[0])).sqrMagnitude > 1e-6f)
            {
                return 0f;
            }

            float shared = 0f;
            int i = 1;
            int j = 1;
            Vector2 pa = a[0];
            Vector2 pb = b[0];
            while (i < a.Count && j < b.Count)
            {
                Vector2 qa = a[i];
                Vector2 qb = b[j];
                float la = (qa - pa).magnitude;
                float lb = (qb - pb).magnitude;
                if (la < 1e-5f)
                {
                    pa = qa;
                    i++;
                    continue;
                }

                if (lb < 1e-5f)
                {
                    pb = qb;
                    j++;
                    continue;
                }

                if (Vector2.Dot((qa - pa) / la, (qb - pb) / lb) < 0.999f)
                {
                    break; // the paths turn different ways here
                }

                float step = Mathf.Min(la, lb);
                shared += step;
                if (Mathf.Abs(la - lb) > 1e-4f)
                {
                    break; // one path bends mid-way along the other's segment
                }

                pa = qa;
                pb = qb;
                i++;
                j++;
            }

            return shared;
        }

        private sealed class SourceHashComparer : IComparer<ConnectionDefinition>
        {
            public static readonly SourceHashComparer Instance = new SourceHashComparer();

            public int Compare(ConnectionDefinition x, ConnectionDefinition y)
            {
                return x.Source.GetHashCode().CompareTo(y.Source.GetHashCode());
            }
        }

        private static float PathLength(IReadOnlyList<Vector3> path)
        {
            float total = 0f;
            for (int i = 1; i < path.Count; i++)
            {
                total += Vector3.Distance(path[i - 1], path[i]);
            }

            return total;
        }

        private static Vector2 PointAlong(IReadOnlyList<Vector3> path, float distance)
        {
            for (int i = 1; i < path.Count; i++)
            {
                float segment = Vector3.Distance(path[i - 1], path[i]);
                if (distance <= segment)
                {
                    return Vector3.Lerp(path[i - 1], path[i], segment > 0f ? distance / segment : 0f);
                }

                distance -= segment;
            }

            return path.Count > 0 ? (Vector2)path[path.Count - 1] : Vector2.zero;
        }

        private void ReleaseAll()
        {
            foreach (GateView view in _gates.Values)
            {
                _gatePool.Release(view);
            }

            foreach (WireView view in _wires.Values)
            {
                _wirePool.Release(view);
            }

            _gates.Clear();
            _wires.Clear();
            _junctionSources.Clear();
            _netTargets.Clear();
            _busWireNets.Clear();
            _displayNets.Clear();
            _selectedNodes.Clear();
            _highlightedPins.Clear();
            _selectedWire = null;
            _hoveredWire = null;
            _hoveredNode = NodeId.None;
        }

        // ------------------------------------------------------------------------------------------------
        // Signals
        // ------------------------------------------------------------------------------------------------

        /// <summary>
        /// Connects the views to a simulation context. <paramref name="scope"/> selects which expanded instance
        /// this presenter shows (the root, or a specific custom-gate instance when inspecting). Performs a full
        /// refresh.
        /// </summary>
        public void SetSimulation(SimulationContext context, CompiledInstance scope)
        {
            _context = context;
            _scope = scope;
            RebuildNetIndex();
            RefreshAllSignals();
        }

        /// <summary>Detaches the simulation (e.g. the circuit failed to compile); views show neutral LOW.</summary>
        public void ClearSimulation()
        {
            _context = null;
            _scope = null;
            _netTargets.Clear();
            _busWireNets.Clear();
            _displayNets.Clear();
            RefreshAllSignals();
        }

        /// <summary>Applies recorded net changes to affected views only.</summary>
        public void ApplyChanges(SignalChangeLog changes)
        {
            if (_context == null)
            {
                return;
            }

            if (changes.Overflowed)
            {
                RefreshAllSignals();
                return;
            }

            ReadOnlySpan<SignalChange> span = changes.AsSpan();
            for (int i = 0; i < span.Length; i++)
            {
                ApplyNetSignal(span[i].Net, span[i].Current);
            }
        }

        /// <summary>Pushes a value to every view bound to <paramref name="net"/> (used by animation layers too).</summary>
        public void ApplyNetSignal(int net, Signal signal)
        {
            if (!_netTargets.TryGetValue(net, out List<NetTarget> targets))
            {
                return;
            }

            NetSignalUpdateCount++;

            for (int i = 0; i < targets.Count; i++)
            {
                NetTarget target = targets[i];
                if (target.Kind == NetTargetKind.DisplayInputs)
                {
                    RefreshDisplay(target.Node, target.Bus);
                    continue;
                }

                Signal value = target.Bus != null ? Aggregate(target.Bus) : signal;
                switch (target.Kind)
                {
                    case NetTargetKind.Wire:
                        if (_wires.TryGetValue(target.Wire, out WireView wire))
                        {
                            wire.SetSignal(value);
                        }

                        break;
                    case NetTargetKind.InputPin:
                        if (_gates.TryGetValue(target.Node, out GateView inputGate))
                        {
                            inputGate.SetInputSignal(target.Index, value);
                        }

                        break;
                    case NetTargetKind.OutputPin:
                        if (_gates.TryGetValue(target.Node, out GateView outputGate))
                        {
                            outputGate.SetOutputSignal(target.Index, value);
                        }

                        break;
                    case NetTargetKind.BoundaryState:
                    case NetTargetKind.LampState:
                        if (_gates.TryGetValue(target.Node, out GateView boundary))
                        {
                            boundary.SetStateSignal(signal);
                        }

                        break;
                    case NetTargetKind.BitState:
                    case NetTargetKind.LampBit:
                        if (_gates.TryGetValue(target.Node, out GateView bus))
                        {
                            bus.SetBitSignal(target.Index, signal);
                        }

                        break;
                }
            }
        }

        /// <summary>
        /// One state summarising the bits of a bus for drawing: UNKNOWN if any bit is unknown, HIGH if any bit is
        /// high, high impedance if every bit floats, otherwise LOW.
        /// </summary>
        private Signal Aggregate(int[] nets)
        {
            if (_context == null)
            {
                return Signal.Low;
            }

            bool high = false;
            bool floating = true;
            for (int i = 0; i < nets.Length; i++)
            {
                switch (_context.GetNet(nets[i]))
                {
                    case Signal.Unknown:
                        return Signal.Unknown;
                    case Signal.High:
                        high = true;
                        floating = false;
                        break;
                    case Signal.Low:
                        floating = false;
                        break;
                }
            }

            return high ? Signal.High : floating ? Signal.HighImpedance : Signal.Low;
        }

        /// <summary>Aggregate state of a bus wire (by destination), or null for single-bit wires.</summary>
        public Signal? GetBusWireSignal(PinRef destination)
        {
            return _busWireNets.TryGetValue(destination, out int[] nets) ? Aggregate(nets) : (Signal?)null;
        }

        /// <summary>Net bound to a wire (by destination pin), or -1 when not simulated.</summary>
        public int GetWireNet(PinRef destination)
        {
            if (_scope == null || !_circuit.TryGetConnectionTo(destination, out ConnectionDefinition connection))
            {
                return -1;
            }

            return _scope.TryGetOutputPinNet(connection.Source, out int net) ? net : -1;
        }

        /// <summary>Reads every input of a display from the model and redraws it.</summary>
        private void RefreshDisplay(NodeId node, int[] nets)
        {
            if (_context == null || !_gates.TryGetValue(node, out GateView view))
            {
                return;
            }

            for (int i = 0; i < nets.Length; i++)
            {
                view.SetDisplayInput(i, _context.GetNet(nets[i]));
            }

            view.CommitDisplay();
        }

        /// <summary>
        /// Nets read by a display, in display-input order: segments a–g and dp of a seven-segment display, otherwise
        /// the bits of its single input (bit 0 first). Unconnected inputs read the floating (LOW) net.
        /// </summary>
        private int[] DisplayNets(NodeId node, GateGeometry geometry)
        {
            int count = Displays.DisplayEncoding.InputCount(geometry.DisplayStyle, geometry.BusWidth);
            var nets = new int[count];
            for (int i = 0; i < count; i++)
            {
                nets[i] = geometry.DisplayStyle == DisplayStyle.SevenSegment
                    ? _scope.GetInputPinNet(new PinRef(node, BoundarySignatures.BitPin(i)))
                    : _scope.GetInputPinNet(new PinRef(node, BoundarySignatures.Pin), i);
            }

            return nets;
        }

        private void RebuildNetIndex()
        {
            _netTargets.Clear();
            _busWireNets.Clear();
            _displayNets.Clear();
            if (_scope == null || _circuit == null)
            {
                return;
            }

            IReadOnlyList<NodeDefinition> nodes = _circuit.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                NodeDefinition node = nodes[i];
                GateGeometry geometry = _geometry.GetGate(node.Id);
                bool busBoundary = geometry.IsBusBoundary;
                for (int p = 0; p < geometry.InputPorts.Length; p++)
                {
                    var pin = new PinRef(node.Id, geometry.InputPorts[p]);
                    int[] bus = InputPinNets(pin);
                    AddTargets(bus, new NetTarget(NetTargetKind.InputPin, node.Id, p, default, bus));
                    if (node.Component.Kind == ComponentKind.OutputBoundary && !busBoundary)
                    {
                        AddTarget(_scope.GetInputPinNet(pin), new NetTarget(NetTargetKind.LampState, node.Id, 0, default));
                    }
                }

                if (geometry.IsDisplay)
                {
                    int[] nets = DisplayNets(node.Id, geometry);
                    _displayNets[node.Id] = nets;
                    var target = new NetTarget(NetTargetKind.DisplayInputs, node.Id, 0, default, nets);
                    for (int bit = 0; bit < nets.Length; bit++)
                    {
                        bool duplicate = false;
                        for (int j = 0; j < bit; j++)
                        {
                            duplicate |= nets[j] == nets[bit];
                        }

                        if (!duplicate)
                        {
                            AddTarget(nets[bit], target);
                        }
                    }
                }

                if (node.Component.Kind == ComponentKind.OutputBoundary && busBoundary)
                {
                    var pin = new PinRef(node.Id, BoundarySignatures.Pin);
                    for (int bit = 0; bit < geometry.BitCells.Length; bit++)
                    {
                        AddTarget(_scope.GetInputPinNet(pin, bit), new NetTarget(NetTargetKind.LampBit, node.Id, bit, default));
                    }
                }

                for (int p = 0; p < geometry.OutputPorts.Length; p++)
                {
                    var pin = new PinRef(node.Id, geometry.OutputPorts[p]);
                    int[] bus = OutputPinNets(pin);
                    if (bus == null)
                    {
                        continue;
                    }

                    AddTargets(bus, new NetTarget(NetTargetKind.OutputPin, node.Id, p, default, bus));
                    if (busBoundary)
                    {
                        for (int bit = 0; bit < bus.Length; bit++)
                        {
                            AddTarget(bus[bit], new NetTarget(NetTargetKind.BitState, node.Id, bit, default));
                        }
                    }
                    else if (node.Component.Kind == ComponentKind.InputBoundary || node.Component.Kind == ComponentKind.Clock)
                    {
                        AddTarget(bus[0], new NetTarget(NetTargetKind.BoundaryState, node.Id, 0, default));
                    }
                }
            }

            IReadOnlyList<ConnectionDefinition> connections = _circuit.Connections;
            for (int i = 0; i < connections.Count; i++)
            {
                int[] bus = OutputPinNets(connections[i].Source);
                if (bus == null)
                {
                    continue;
                }

                AddTargets(bus, new NetTarget(NetTargetKind.Wire, default, 0, connections[i].Destination, bus));
                if (bus.Length > 1)
                {
                    _busWireNets[connections[i].Destination] = bus;
                }
            }
        }

        /// <summary>Nets of every bit of an output pin, or null when the pin is not simulated.</summary>
        private int[] OutputPinNets(PinRef pin)
        {
            int width = _scope.GetOutputPinWidth(pin);
            if (width == 0)
            {
                return null;
            }

            var nets = new int[width];
            for (int bit = 0; bit < width; bit++)
            {
                _scope.TryGetOutputPinNet(pin, bit, out nets[bit]);
            }

            return nets;
        }

        private int[] InputPinNets(PinRef pin)
        {
            int width = _scope.GetInputPinWidth(pin);
            var nets = new int[width];
            for (int bit = 0; bit < width; bit++)
            {
                nets[bit] = _scope.GetInputPinNet(pin, bit);
            }

            return nets;
        }

        /// <summary>
        /// Registers <paramref name="target"/> under every net of a pin or wire. Single-bit targets keep no bus array
        /// (they show the net's own value); bus targets recompute their aggregate whenever any bit changes.
        /// </summary>
        private void AddTargets(int[] nets, NetTarget target)
        {
            if (nets.Length == 1)
            {
                AddTarget(nets[0], new NetTarget(target.Kind, target.Node, target.Index, target.Wire));
                return;
            }

            for (int bit = 0; bit < nets.Length; bit++)
            {
                bool duplicate = false;
                for (int j = 0; j < bit; j++)
                {
                    duplicate |= nets[j] == nets[bit];
                }

                if (!duplicate)
                {
                    AddTarget(nets[bit], target);
                }
            }
        }

        private void AddTarget(int net, NetTarget target)
        {
            if (!_netTargets.TryGetValue(net, out List<NetTarget> list))
            {
                list = new List<NetTarget>(4);
                _netTargets.Add(net, list);
            }

            list.Add(target);
        }

        /// <summary>Re-applies the simulation's current values to every view (ends all visual transitions).</summary>
        public void RefreshSignals()
        {
            RefreshAllSignals();
        }

        /// <summary>Switches how wire signal states are drawn (all wires update at once).</summary>
        public void SetVisualizationMode(SignalVisualizationMode mode)
        {
            if (_style.Mode == mode)
            {
                return;
            }

            _style.Mode = mode;
            foreach (WireView wire in _wires.Values)
            {
                wire.RefreshStyle();
            }
        }

        /// <summary>Destination pins of every wire carrying <paramref name="net"/> (fan-out order).</summary>
        public void GetNetWires(int net, List<PinRef> destinations)
        {
            if (!_netTargets.TryGetValue(net, out List<NetTarget> targets))
            {
                return;
            }

            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i].Kind == NetTargetKind.Wire)
                {
                    destinations.Add(targets[i].Wire);
                }
            }
        }

        /// <summary>
        /// Applies a net change on its driving side only: driver output pins and input-switch states. Wires and
        /// destination pins keep their old state until <see cref="ApplyWireArrival"/>.
        /// </summary>
        public void ApplyNetSource(int net, Signal signal)
        {
            if (!_netTargets.TryGetValue(net, out List<NetTarget> targets))
            {
                return;
            }

            for (int i = 0; i < targets.Count; i++)
            {
                NetTarget target = targets[i];
                if (target.Kind == NetTargetKind.OutputPin && _gates.TryGetValue(target.Node, out GateView gate))
                {
                    gate.SetOutputSignal(target.Index, target.Bus != null ? Aggregate(target.Bus) : signal);
                }
                else if (target.Kind == NetTargetKind.BoundaryState && _gates.TryGetValue(target.Node, out GateView source))
                {
                    source.SetStateSignal(signal);
                }
                else if (target.Kind == NetTargetKind.BitState && _gates.TryGetValue(target.Node, out GateView bus))
                {
                    bus.SetBitSignal(target.Index, signal);
                }
            }
        }

        /// <summary>
        /// Completes a signal's arrival over one wire: the wire settles to <paramref name="signal"/> and the
        /// destination pin (and an output lamp) take the new value.
        /// </summary>
        public void ApplyWireArrival(PinRef destination, Signal signal)
        {
            if (_busWireNets.TryGetValue(destination, out int[] busNets))
            {
                // A bus wire shows its aggregate; a bus lamp shows every bit (all are final in the model).
                signal = Aggregate(busNets);
                if (_gates.TryGetValue(destination.Node, out GateView lamp) && lamp.Geometry.IsBusBoundary)
                {
                    for (int bit = 0; bit < busNets.Length && bit < lamp.BitCount; bit++)
                    {
                        lamp.SetBitSignal(bit, _context.GetNet(busNets[bit]));
                    }
                }
            }

            if (_wires.TryGetValue(destination, out WireView wire))
            {
                wire.SetSignal(signal);
            }

            if (!_gates.TryGetValue(destination.Node, out GateView gate))
            {
                return;
            }

            PortId[] inputs = gate.Geometry.InputPorts;
            for (int p = 0; p < inputs.Length; p++)
            {
                if (inputs[p] == destination.Port)
                {
                    gate.SetInputSignal(p, signal);
                    break;
                }
            }

            if (gate.Geometry.Kind == ComponentKind.OutputBoundary && !gate.Geometry.IsBusBoundary)
            {
                gate.SetStateSignal(signal);
            }

            if (gate.IsDisplay && _displayNets.TryGetValue(destination.Node, out int[] displayNets))
            {
                if (gate.Geometry.DisplayStyle == DisplayStyle.SevenSegment)
                {
                    // Each segment lights as its own wire's signal arrives.
                    gate.SetDisplayInput(BoundarySignatures.IndexOfBitPin(destination.Port), signal);
                    gate.CommitDisplay();
                }
                else
                {
                    RefreshDisplay(destination.Node, displayNets);
                }
            }
        }

        private void RefreshAllSignals()
        {
            RefreshAllSignalsCore();
            SignalsRefreshed?.Invoke();
        }

        private void RefreshAllSignalsCore()
        {
            if (_context == null)
            {
                foreach (GateView gate in _gates.Values)
                {
                    GateGeometry geometry = gate.Geometry;
                    for (int p = 0; p < geometry.InputPorts.Length; p++)
                    {
                        gate.SetInputSignal(p, Signal.Low);
                    }

                    for (int p = 0; p < geometry.OutputPorts.Length; p++)
                    {
                        gate.SetOutputSignal(p, Signal.Low);
                    }

                    gate.SetStateSignal(Signal.Low);
                    gate.ResetDisplay();
                    for (int bit = 0; bit < gate.BitCount; bit++)
                    {
                        gate.SetBitSignal(bit, Signal.Low);
                    }
                }

                foreach (WireView wire in _wires.Values)
                {
                    wire.SetSignal(Signal.Low);
                }

                return;
            }

            foreach (KeyValuePair<int, List<NetTarget>> pair in _netTargets)
            {
                ApplyNetSignal(pair.Key, _context.GetNet(pair.Key));
            }
        }

        private void RefreshNodeSignals(NodeId node)
        {
            if (_context == null || _scope == null || !_gates.TryGetValue(node, out GateView view))
            {
                return;
            }

            GateGeometry geometry = view.Geometry;
            for (int p = 0; p < geometry.InputPorts.Length; p++)
            {
                view.SetInputSignal(p, Aggregate(InputPinNets(new PinRef(node, geometry.InputPorts[p]))));
            }

            for (int p = 0; p < geometry.OutputPorts.Length; p++)
            {
                int[] nets = OutputPinNets(new PinRef(node, geometry.OutputPorts[p]));
                if (nets != null)
                {
                    view.SetOutputSignal(p, Aggregate(nets));
                }
            }

            if (geometry.IsDisplay && _displayNets.TryGetValue(node, out int[] displayNets))
            {
                RefreshDisplay(node, displayNets);
            }

            if (geometry.IsBusBoundary)
            {
                var pin = new PinRef(node, BoundarySignatures.Pin);
                bool input = geometry.Kind == ComponentKind.InputBoundary;
                for (int bit = 0; bit < view.BitCount; bit++)
                {
                    int net;
                    if (input)
                    {
                        _scope.TryGetOutputPinNet(pin, bit, out net);
                    }
                    else
                    {
                        net = _scope.GetInputPinNet(pin, bit);
                    }

                    view.SetBitSignal(bit, _context.GetNet(net));
                }
            }
        }

        // ------------------------------------------------------------------------------------------------
        // Selection, hover, highlights, previews
        // ------------------------------------------------------------------------------------------------

        public void SetSelection(IReadOnlyCollection<NodeId> nodes, PinRef? wire)
        {
            _selectedNodes.Clear();
            foreach (NodeId node in nodes)
            {
                _selectedNodes.Add(node);
            }

            foreach (KeyValuePair<NodeId, GateView> pair in _gates)
            {
                pair.Value.SetSelected(_selectedNodes.Contains(pair.Key));
            }

            if (_selectedWire.HasValue && _wires.TryGetValue(_selectedWire.Value, out WireView previous))
            {
                previous.SetSelected(false);
            }

            _selectedWire = wire;
            if (wire.HasValue && _wires.TryGetValue(wire.Value, out WireView current))
            {
                current.SetSelected(true);
            }
        }

        public void SetHover(NodeId node, PinRef? wire)
        {
            if (_hoveredNode != node)
            {
                if (_gates.TryGetValue(_hoveredNode, out GateView oldNode))
                {
                    oldNode.SetHovered(false);
                }

                _hoveredNode = node;
                if (_gates.TryGetValue(node, out GateView newNode))
                {
                    newNode.SetHovered(true);
                }
            }

            if (!Nullable.Equals(_hoveredWire, wire))
            {
                if (_hoveredWire.HasValue && _wires.TryGetValue(_hoveredWire.Value, out WireView oldWire))
                {
                    oldWire.SetHovered(false);
                }

                _hoveredWire = wire;
                if (wire.HasValue && _wires.TryGetValue(wire.Value, out WireView newWire))
                {
                    newWire.SetHovered(true);
                }
            }
        }

        public void HighlightPin(PinRef pin)
        {
            if (!_gates.TryGetValue(pin.Node, out GateView view))
            {
                return;
            }

            int index = view.Geometry.IndexOfInput(pin.Port);
            if (index >= 0)
            {
                view.SetPinHighlight(true, index, true);
            }
            else
            {
                index = view.Geometry.IndexOfOutput(pin.Port);
                if (index < 0)
                {
                    return;
                }

                view.SetPinHighlight(false, index, true);
            }

            _highlightedPins.Add(pin);
        }

        public void ClearPinHighlights()
        {
            for (int i = 0; i < _highlightedPins.Count; i++)
            {
                if (_gates.TryGetValue(_highlightedPins[i].Node, out GateView view))
                {
                    view.ClearPinHighlights();
                }
            }

            _highlightedPins.Clear();
        }

        public void ShowWirePreview(List<Vector3> path, bool valid)
        {
            if (_preview == null)
            {
                _preview = WireView.Create(_wireRoot, _theme);
                _preview.name = "Wire Preview";
            }

            _preview.gameObject.SetActive(true);
            _preview.SetPreview(valid);
            _preview.SetPath(path);
        }

        public void HideWirePreview()
        {
            if (_preview != null)
            {
                _preview.gameObject.SetActive(false);
            }
        }

        public void ShowGhost(GateGeometry geometry, Vector2 position, bool valid)
        {
            if (_ghost == null)
            {
                _ghost = GateView.Create(_gateRoot, _theme);
                _ghost.name = "Placement Ghost";
            }

            _ghost.gameObject.SetActive(true);
            if (_ghost.Geometry != geometry)
            {
                _ghost.Configure(NodeId.None, geometry);
            }

            _ghost.SetGhost(true, valid);
            _ghost.SetPosition(position);
        }

        public void HideGhost()
        {
            if (_ghost != null)
            {
                _ghost.gameObject.SetActive(false);
            }
        }

        public void Dispose()
        {
            if (_circuit != null)
            {
                _circuit.Changed -= OnCircuitChanged;
                _circuit = null;
            }

            ReleaseAll();
        }

        private enum NetTargetKind : byte
        {
            Wire,
            InputPin,
            OutputPin,

            /// <summary>State of an input switch (driving side of its net).</summary>
            BoundaryState,

            /// <summary>State of an output lamp (receiving side of its net).</summary>
            LampState,

            /// <summary>One bit cell of a bus input (driving side); <c>Index</c> is the bit.</summary>
            BitState,

            /// <summary>One bit cell of a bus output (receiving side); <c>Index</c> is the bit.</summary>
            LampBit,

            /// <summary>A display (Phase D); <c>Bus</c> holds all nets it reads, and any change redraws it.</summary>
            DisplayInputs,
        }

        private readonly struct NetTarget
        {
            public NetTarget(NetTargetKind kind, NodeId node, int index, PinRef wire, int[] bus = null)
            {
                Kind = kind;
                Node = node;
                Index = index;
                Wire = wire;
                Bus = bus;
            }

            /// <summary>All nets of a multi-bit pin or wire (shown as their aggregate); null for single bits.</summary>
            public int[] Bus { get; }

            public NetTargetKind Kind { get; }

            public NodeId Node { get; }

            public int Index { get; }

            public PinRef Wire { get; }
        }
    }
}
