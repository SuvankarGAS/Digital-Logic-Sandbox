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
        private readonly HashSet<NodeId> _selectedNodes = new HashSet<NodeId>();
        private readonly List<ConnectionDefinition> _scratchConnections = new List<ConnectionDefinition>();
        private readonly List<PinRef> _highlightedPins = new List<PinRef>();
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

        /// <summary>Diagnostics: wire views rerouted after gates moved or rotated (since the last reset).</summary>
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
            }
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

                    RerouteWiresOf(change.Node);
                    break;

                case CircuitChangeKind.NodeRotated:
                case CircuitChangeKind.InterfacePortRenamed:
                    if (_gates.TryGetValue(change.Node, out GateView reconfigured) && _circuit.ContainsNode(change.Node))
                    {
                        reconfigured.Configure(change.Node, _geometry.GetGate(change.Node));
                        reconfigured.SetSelected(_selectedNodes.Contains(change.Node));
                        RefreshNodeSignals(change.Node);
                    }

                    RerouteWiresOf(change.Node);
                    break;

                case CircuitChangeKind.Connected:
                    CreateWireView(change.Connection.Destination);
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

                    break;
            }
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
            view.SetPath(_geometry.GetWirePath(destination));
            view.name = "Wire -> " + destination;
            _wires[destination] = view;
        }

        private void RerouteWiresOf(NodeId node)
        {
            if (!_circuit.ContainsNode(node))
            {
                return;
            }

            _scratchConnections.Clear();
            _circuit.GetConnectionsFrom(node, _scratchConnections);
            _circuit.GetConnectionsTo(node, _scratchConnections);
            for (int i = 0; i < _scratchConnections.Count; i++)
            {
                PinRef destination = _scratchConnections[i].Destination;
                if (_wires.TryGetValue(destination, out WireView view))
                {
                    view.SetPath(_geometry.GetWirePath(destination));
                    WireRerouteCount++;
                }
            }
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
            _netTargets.Clear();
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
                switch (target.Kind)
                {
                    case NetTargetKind.Wire:
                        if (_wires.TryGetValue(target.Wire, out WireView wire))
                        {
                            wire.SetSignal(signal);
                        }

                        break;
                    case NetTargetKind.InputPin:
                        if (_gates.TryGetValue(target.Node, out GateView inputGate))
                        {
                            inputGate.SetInputSignal(target.Index, signal);
                        }

                        break;
                    case NetTargetKind.OutputPin:
                        if (_gates.TryGetValue(target.Node, out GateView outputGate))
                        {
                            outputGate.SetOutputSignal(target.Index, signal);
                        }

                        break;
                    case NetTargetKind.BoundaryState:
                    case NetTargetKind.LampState:
                        if (_gates.TryGetValue(target.Node, out GateView boundary))
                        {
                            boundary.SetStateSignal(signal);
                        }

                        break;
                }
            }
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

        private void RebuildNetIndex()
        {
            _netTargets.Clear();
            if (_scope == null || _circuit == null)
            {
                return;
            }

            IReadOnlyList<NodeDefinition> nodes = _circuit.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                NodeDefinition node = nodes[i];
                GateGeometry geometry = _geometry.GetGate(node.Id);
                for (int p = 0; p < geometry.InputPorts.Length; p++)
                {
                    int net = _scope.GetInputPinNet(new PinRef(node.Id, geometry.InputPorts[p]));
                    AddTarget(net, new NetTarget(NetTargetKind.InputPin, node.Id, p, default));
                    if (node.Component.Kind == ComponentKind.OutputBoundary)
                    {
                        AddTarget(net, new NetTarget(NetTargetKind.LampState, node.Id, 0, default));
                    }
                }

                for (int p = 0; p < geometry.OutputPorts.Length; p++)
                {
                    if (!_scope.TryGetOutputPinNet(new PinRef(node.Id, geometry.OutputPorts[p]), out int net))
                    {
                        continue;
                    }

                    AddTarget(net, new NetTarget(NetTargetKind.OutputPin, node.Id, p, default));
                    if (node.Component.Kind == ComponentKind.InputBoundary)
                    {
                        AddTarget(net, new NetTarget(NetTargetKind.BoundaryState, node.Id, 0, default));
                    }
                }
            }

            IReadOnlyList<ConnectionDefinition> connections = _circuit.Connections;
            for (int i = 0; i < connections.Count; i++)
            {
                if (_scope.TryGetOutputPinNet(connections[i].Source, out int net))
                {
                    AddTarget(net, new NetTarget(NetTargetKind.Wire, default, 0, connections[i].Destination));
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
                    gate.SetOutputSignal(target.Index, signal);
                }
                else if (target.Kind == NetTargetKind.BoundaryState && _gates.TryGetValue(target.Node, out GateView source))
                {
                    source.SetStateSignal(signal);
                }
            }
        }

        /// <summary>
        /// Completes a signal's arrival over one wire: the wire settles to <paramref name="signal"/> and the
        /// destination pin (and an output lamp) take the new value.
        /// </summary>
        public void ApplyWireArrival(PinRef destination, Signal signal)
        {
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

            if (gate.Geometry.Kind == ComponentKind.OutputBoundary)
            {
                gate.SetStateSignal(signal);
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
                view.SetInputSignal(p, _context.GetNet(_scope.GetInputPinNet(new PinRef(node, geometry.InputPorts[p]))));
            }

            for (int p = 0; p < geometry.OutputPorts.Length; p++)
            {
                if (_scope.TryGetOutputPinNet(new PinRef(node, geometry.OutputPorts[p]), out int net))
                {
                    view.SetOutputSignal(p, _context.GetNet(net));
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
        }

        private readonly struct NetTarget
        {
            public NetTarget(NetTargetKind kind, NodeId node, int index, PinRef wire)
            {
                Kind = kind;
                Node = node;
                Index = index;
                Wire = wire;
            }

            public NetTargetKind Kind { get; }

            public NodeId Node { get; }

            public int Index { get; }

            public PinRef Wire { get; }
        }
    }
}
