using System;
using System.Collections.Generic;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;

namespace DigitalLogicSandbox.Core.Circuit
{
    /// <summary>
    /// A circuit graph: interface ports, nodes, and connections. This is the single source of truth for a
    /// circuit; Unity objects only mirror it. The same type describes the root workspace circuit and every
    /// packaged custom gate.
    /// <para>
    /// Structural invariants maintained here (independent of any component knowledge):
    /// unique node ids, at most one driver per input pin, no connection from a node to itself, connections only
    /// between existing nodes, and a one-to-one mapping between interface ports and boundary nodes.
    /// Pin existence/direction against component signatures is checked by
    /// <see cref="Validation.ConnectionRules"/> and <see cref="Validation.CircuitValidator"/>.
    /// </para>
    /// <para>
    /// Once <see cref="Freeze"/> is called (on publish) the definition is immutable; any mutation throws.
    /// </para>
    /// </summary>
    public sealed class CircuitDefinition
    {
        private readonly List<NodeDefinition> _nodes = new List<NodeDefinition>();
        private readonly Dictionary<NodeId, NodeDefinition> _nodeLookup = new Dictionary<NodeId, NodeDefinition>();
        private readonly List<ConnectionDefinition> _connections = new List<ConnectionDefinition>();

        private readonly Dictionary<PinRef, ConnectionDefinition> _connectionByDestination =
            new Dictionary<PinRef, ConnectionDefinition>();

        private readonly List<InterfacePort> _inputs = new List<InterfacePort>();
        private readonly List<InterfacePort> _outputs = new List<InterfacePort>();
        private readonly Dictionary<PortId, NodeId> _boundaryNodeByPort = new Dictionary<PortId, NodeId>();

        private int _nextNodeId = 1;
        private int _nextPortId = 0;
        private ComponentSignature _signature;
        private string _name;
        private string _description = string.Empty;
        private string _category = string.Empty;
        private string _icon = string.Empty;

        public CircuitDefinition(DefinitionId id, string name, int version = 1)
        {
            if (!id.IsValid)
            {
                throw new ArgumentException("A circuit definition requires a valid id.", nameof(id));
            }

            if (version < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(version), version, "Versions start at 1.");
            }

            Id = id;
            Version = version;
            _name = name ?? string.Empty;
            CreatedUtc = DateTime.UtcNow;
            ModifiedUtc = CreatedUtc;
        }

        /// <summary>Raised after every successful mutation.</summary>
        public event Action<CircuitChange> Changed;

        public DefinitionId Id { get; }

        public int Version { get; }

        public DefinitionRef Reference => new DefinitionRef(Id, Version);

        public DateTime CreatedUtc { get; private set; }

        public DateTime ModifiedUtc { get; private set; }

        /// <summary>True once frozen (published). Read-only definitions can be shared by any number of instances.</summary>
        public bool IsReadOnly { get; private set; }

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public string Description
        {
            get => _description;
            set => SetProperty(ref _description, value);
        }

        public string Category
        {
            get => _category;
            set => SetProperty(ref _category, value);
        }

        /// <summary>Icon key for the library and gate body. Purely visual.</summary>
        public string Icon
        {
            get => _icon;
            set => SetProperty(ref _icon, value);
        }

        public IReadOnlyList<NodeDefinition> Nodes => _nodes;

        public IReadOnlyList<ConnectionDefinition> Connections => _connections;

        /// <summary>External inputs in interface order.</summary>
        public IReadOnlyList<InterfacePort> Inputs => _inputs;

        /// <summary>External outputs in interface order.</summary>
        public IReadOnlyList<InterfacePort> Outputs => _outputs;

        // ------------------------------------------------------------------------------------------------
        // Lifecycle
        // ------------------------------------------------------------------------------------------------

        /// <summary>Makes the definition permanently immutable.</summary>
        public void Freeze()
        {
            IsReadOnly = true;
        }

        /// <summary>Restores persisted timestamps. Intended for deserialization.</summary>
        public void RestoreTimestamps(DateTime createdUtc, DateTime modifiedUtc)
        {
            EnsureMutable();
            CreatedUtc = createdUtc;
            ModifiedUtc = modifiedUtc;
        }

        /// <summary>
        /// Deep copy with a new identity and/or version. The copy is mutable, has no subscribers and shares no
        /// state with the original. Node and port ids are preserved, so layouts and references stay valid.
        /// </summary>
        public CircuitDefinition Clone(DefinitionId id, int version)
        {
            var copy = new CircuitDefinition(id, _name, version)
            {
                _description = _description,
                _category = _category,
                _icon = _icon,
                _nextNodeId = _nextNodeId,
                _nextPortId = _nextPortId,
                CreatedUtc = CreatedUtc,
                ModifiedUtc = ModifiedUtc,
            };

            for (int i = 0; i < _inputs.Count; i++)
            {
                copy._inputs.Add(_inputs[i].Clone());
            }

            for (int i = 0; i < _outputs.Count; i++)
            {
                copy._outputs.Add(_outputs[i].Clone());
            }

            foreach (KeyValuePair<PortId, NodeId> pair in _boundaryNodeByPort)
            {
                copy._boundaryNodeByPort.Add(pair.Key, pair.Value);
            }

            for (int i = 0; i < _nodes.Count; i++)
            {
                NodeDefinition node = _nodes[i].Clone();
                copy._nodes.Add(node);
                copy._nodeLookup.Add(node.Id, node);
            }

            for (int i = 0; i < _connections.Count; i++)
            {
                ConnectionDefinition connection = _connections[i];
                copy._connections.Add(connection);
                copy._connectionByDestination.Add(connection.Destination, connection);
            }

            return copy;
        }

        /// <summary>Deep copy keeping the same id with a different version (used for gate versioning).</summary>
        public CircuitDefinition CloneAsVersion(int version)
        {
            return Clone(Id, version);
        }

        // ------------------------------------------------------------------------------------------------
        // Nodes
        // ------------------------------------------------------------------------------------------------

        /// <summary>Adds a primitive or custom-gate node. Boundaries must use AddInputBoundary/AddOutputBoundary.</summary>
        public NodeDefinition AddNode(ComponentRef component, CircuitPoint position, Rotation rotation = Rotation.R0,
            string label = null)
        {
            EnsureMutable();
            if (component.IsBoundary)
            {
                throw new ArgumentException(
                    "Boundary nodes define the circuit interface; use AddInputBoundary or AddOutputBoundary.",
                    nameof(component));
            }

            var node = new NodeDefinition(AllocateNodeId(), component, position, rotation, default, label);
            InsertNode(node);
            return node;
        }

        /// <summary>Adds an external input (interface port + boundary node) at the end of the input order.</summary>
        public NodeDefinition AddInputBoundary(string name, CircuitPoint position)
        {
            EnsureMutable();
            return InsertBoundary(AllocateNodeId(), AllocatePortId(), PortDirection.Input, name, position,
                Rotation.R0, null, -1);
        }

        /// <summary>Adds an external output (interface port + boundary node) at the end of the output order.</summary>
        public NodeDefinition AddOutputBoundary(string name, CircuitPoint position)
        {
            EnsureMutable();
            return InsertBoundary(AllocateNodeId(), AllocatePortId(), PortDirection.Output, name, position,
                Rotation.R0, null, -1);
        }

        /// <summary>
        /// Re-creates a non-boundary node with an explicit id. Used by deserialization and undo so ids, and
        /// therefore connections and layouts, survive round trips.
        /// </summary>
        public NodeDefinition RestoreNode(NodeId id, ComponentRef component, CircuitPoint position, Rotation rotation,
            string label)
        {
            EnsureMutable();
            if (component.IsBoundary)
            {
                throw new ArgumentException("Use RestoreBoundary for boundary nodes.", nameof(component));
            }

            ReserveNodeId(id);
            var node = new NodeDefinition(id, component, position, rotation, default, label);
            InsertNode(node);
            return node;
        }

        /// <summary>
        /// Re-creates a boundary node and its interface port with explicit ids. <paramref name="portIndex"/> of
        /// -1 appends; otherwise the port is inserted at that interface position.
        /// </summary>
        public NodeDefinition RestoreBoundary(NodeId nodeId, PortId portId, PortDirection direction, string name,
            CircuitPoint position, Rotation rotation, string label, int portIndex = -1)
        {
            EnsureMutable();
            ReserveNodeId(nodeId);
            ReservePortId(portId);
            return InsertBoundary(nodeId, portId, direction, name, position, rotation, label, portIndex);
        }

        /// <summary>Removes a node and every connection touching it. Removing a boundary removes its port.</summary>
        public bool RemoveNode(NodeId id)
        {
            EnsureMutable();
            if (!_nodeLookup.TryGetValue(id, out NodeDefinition node))
            {
                return false;
            }

            for (int i = _connections.Count - 1; i >= 0; i--)
            {
                ConnectionDefinition connection = _connections[i];
                if (connection.Source.Node == id || connection.Destination.Node == id)
                {
                    RemoveConnectionAt(i);
                }
            }

            _nodes.Remove(node);
            _nodeLookup.Remove(id);
            Touch();
            Raise(new CircuitChange(CircuitChangeKind.NodeRemoved, id));

            if (node.IsBoundary)
            {
                List<InterfacePort> ports = node.Component.Kind == ComponentKind.InputBoundary ? _inputs : _outputs;
                int index = IndexOfPort(ports, node.InterfacePort);
                if (index >= 0)
                {
                    ports.RemoveAt(index);
                }

                _boundaryNodeByPort.Remove(node.InterfacePort);
                _signature = null;
                Raise(new CircuitChange(CircuitChangeKind.InterfacePortRemoved, id, node.InterfacePort));
            }

            return true;
        }

        public bool ContainsNode(NodeId id)
        {
            return _nodeLookup.ContainsKey(id);
        }

        public bool TryGetNode(NodeId id, out NodeDefinition node)
        {
            return _nodeLookup.TryGetValue(id, out node);
        }

        public NodeDefinition GetNode(NodeId id)
        {
            if (!_nodeLookup.TryGetValue(id, out NodeDefinition node))
            {
                throw new KeyNotFoundException("Node " + id + " does not exist in circuit '" + _name + "'.");
            }

            return node;
        }

        public void MoveNode(NodeId id, CircuitPoint position)
        {
            EnsureMutable();
            NodeDefinition node = GetNode(id);
            if (node.Position == position)
            {
                return;
            }

            node.Position = position;
            Touch();
            Raise(new CircuitChange(CircuitChangeKind.NodeMoved, id));
        }

        public void RotateNode(NodeId id, Rotation rotation)
        {
            EnsureMutable();
            NodeDefinition node = GetNode(id);
            if (node.Rotation == rotation)
            {
                return;
            }

            node.Rotation = rotation;
            Touch();
            Raise(new CircuitChange(CircuitChangeKind.NodeRotated, id));
        }

        public void SetNodeLabel(NodeId id, string label)
        {
            EnsureMutable();
            NodeDefinition node = GetNode(id);
            label ??= string.Empty;
            if (string.Equals(node.Label, label, StringComparison.Ordinal))
            {
                return;
            }

            node.Label = label;
            Touch();
            Raise(new CircuitChange(CircuitChangeKind.NodeLabelChanged, id));
        }

        // ------------------------------------------------------------------------------------------------
        // Connections
        // ------------------------------------------------------------------------------------------------

        /// <summary>
        /// Adds a connection if it satisfies the structural invariants. Returns <see cref="ConnectionError.None"/>
        /// on success. Does not check pins against component signatures; use
        /// <see cref="Validation.ConnectionRules.Check"/> first for full editor-side validation.
        /// </summary>
        public ConnectionError TryConnect(PinRef source, PinRef destination)
        {
            if (IsReadOnly)
            {
                return ConnectionError.CircuitReadOnly;
            }

            if (!_nodeLookup.ContainsKey(source.Node))
            {
                return ConnectionError.MissingSourceNode;
            }

            if (!_nodeLookup.ContainsKey(destination.Node))
            {
                return ConnectionError.MissingDestinationNode;
            }

            if (source.Node == destination.Node)
            {
                return ConnectionError.SelfConnection;
            }

            if (_connectionByDestination.ContainsKey(destination))
            {
                return ConnectionError.DestinationAlreadyDriven;
            }

            var connection = new ConnectionDefinition(source, destination);
            _connections.Add(connection);
            _connectionByDestination.Add(destination, connection);
            Touch();
            Raise(new CircuitChange(CircuitChangeKind.Connected, connection: connection));
            return ConnectionError.None;
        }

        /// <summary>Adds a connection or throws <see cref="InvalidOperationException"/> describing the failure.</summary>
        public ConnectionDefinition Connect(PinRef source, PinRef destination)
        {
            ConnectionError error = TryConnect(source, destination);
            if (error != ConnectionError.None)
            {
                throw new InvalidOperationException(
                    "Cannot connect " + source + " -> " + destination + ": " + error + ".");
            }

            return _connectionByDestination[destination];
        }

        /// <summary>Removes the connection driving <paramref name="destination"/>.</summary>
        public bool Disconnect(PinRef destination)
        {
            EnsureMutable();
            if (!_connectionByDestination.ContainsKey(destination))
            {
                return false;
            }

            for (int i = 0; i < _connections.Count; i++)
            {
                if (_connections[i].Destination == destination)
                {
                    RemoveConnectionAt(i);
                    return true;
                }
            }

            return false;
        }

        public bool IsDriven(PinRef destination)
        {
            return _connectionByDestination.ContainsKey(destination);
        }

        public bool TryGetConnectionTo(PinRef destination, out ConnectionDefinition connection)
        {
            return _connectionByDestination.TryGetValue(destination, out connection);
        }

        /// <summary>Appends all connections whose source is on <paramref name="node"/> to <paramref name="results"/>.</summary>
        public void GetConnectionsFrom(NodeId node, List<ConnectionDefinition> results)
        {
            for (int i = 0; i < _connections.Count; i++)
            {
                if (_connections[i].Source.Node == node)
                {
                    results.Add(_connections[i]);
                }
            }
        }

        /// <summary>Appends all connections whose destination is on <paramref name="node"/> to <paramref name="results"/>.</summary>
        public void GetConnectionsTo(NodeId node, List<ConnectionDefinition> results)
        {
            for (int i = 0; i < _connections.Count; i++)
            {
                if (_connections[i].Destination.Node == node)
                {
                    results.Add(_connections[i]);
                }
            }
        }

        // ------------------------------------------------------------------------------------------------
        // Interface
        // ------------------------------------------------------------------------------------------------

        public bool TryGetInterfacePort(PortId id, out InterfacePort port)
        {
            int index = IndexOfPort(_inputs, id);
            if (index >= 0)
            {
                port = _inputs[index];
                return true;
            }

            index = IndexOfPort(_outputs, id);
            if (index >= 0)
            {
                port = _outputs[index];
                return true;
            }

            port = null;
            return false;
        }

        public int IndexOfInput(PortId id)
        {
            return IndexOfPort(_inputs, id);
        }

        public int IndexOfOutput(PortId id)
        {
            return IndexOfPort(_outputs, id);
        }

        /// <summary>The boundary node implementing an interface port.</summary>
        public bool TryGetBoundaryNode(PortId port, out NodeId node)
        {
            return _boundaryNodeByPort.TryGetValue(port, out node);
        }

        public void RenameInterfacePort(PortId id, string name)
        {
            EnsureMutable();
            if (!TryGetInterfacePort(id, out InterfacePort port))
            {
                throw new KeyNotFoundException("Interface port " + id + " does not exist.");
            }

            name ??= string.Empty;
            if (string.Equals(port.Name, name, StringComparison.Ordinal))
            {
                return;
            }

            port.Name = name;
            _signature = null;
            Touch();
            _boundaryNodeByPort.TryGetValue(id, out NodeId node);
            Raise(new CircuitChange(CircuitChangeKind.InterfacePortRenamed, node, id));
        }

        /// <summary>Changes the external order of an interface port within its direction.</summary>
        public void MoveInterfacePort(PortId id, int newIndex)
        {
            EnsureMutable();
            List<InterfacePort> ports = _inputs;
            int index = IndexOfPort(_inputs, id);
            if (index < 0)
            {
                ports = _outputs;
                index = IndexOfPort(_outputs, id);
            }

            if (index < 0)
            {
                throw new KeyNotFoundException("Interface port " + id + " does not exist.");
            }

            if (newIndex < 0 || newIndex >= ports.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(newIndex), newIndex, "Port index out of range.");
            }

            if (newIndex == index)
            {
                return;
            }

            InterfacePort port = ports[index];
            ports.RemoveAt(index);
            ports.Insert(newIndex, port);
            _signature = null;
            Touch();
            _boundaryNodeByPort.TryGetValue(id, out NodeId node);
            Raise(new CircuitChange(CircuitChangeKind.InterfacePortMoved, node, id));
        }

        /// <summary>
        /// The external pin layout this circuit exposes when used as a custom gate. Cached; rebuilt only after
        /// interface changes.
        /// </summary>
        public ComponentSignature GetSignature()
        {
            if (_signature != null)
            {
                return _signature;
            }

            var inputs = new PortDescriptor[_inputs.Count];
            for (int i = 0; i < inputs.Length; i++)
            {
                inputs[i] = new PortDescriptor(_inputs[i].Id, _inputs[i].Name);
            }

            var outputs = new PortDescriptor[_outputs.Count];
            for (int i = 0; i < outputs.Length; i++)
            {
                outputs[i] = new PortDescriptor(_outputs[i].Id, _outputs[i].Name);
            }

            _signature = new ComponentSignature(inputs, outputs);
            return _signature;
        }

        public override string ToString()
        {
            return "'" + _name + "' (" + Reference + ", " + _nodes.Count + " nodes, " + _connections.Count +
                " connections)";
        }

        // ------------------------------------------------------------------------------------------------
        // Internals
        // ------------------------------------------------------------------------------------------------

        private NodeDefinition InsertBoundary(NodeId nodeId, PortId portId, PortDirection direction, string name,
            CircuitPoint position, Rotation rotation, string label, int portIndex)
        {
            List<InterfacePort> ports = direction == PortDirection.Input ? _inputs : _outputs;
            if (portIndex > ports.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(portIndex), portIndex, "Port index out of range.");
            }

            var port = new InterfacePort(portId, direction, name);
            if (portIndex < 0)
            {
                ports.Add(port);
            }
            else
            {
                ports.Insert(portIndex, port);
            }

            ComponentRef component = direction == PortDirection.Input
                ? ComponentRef.InputBoundary
                : ComponentRef.OutputBoundary;
            var node = new NodeDefinition(nodeId, component, position, rotation, portId, label);
            _boundaryNodeByPort.Add(portId, nodeId);
            _signature = null;
            InsertNode(node);
            Raise(new CircuitChange(CircuitChangeKind.InterfacePortAdded, nodeId, portId));
            return node;
        }

        private void InsertNode(NodeDefinition node)
        {
            _nodes.Add(node);
            _nodeLookup.Add(node.Id, node);
            Touch();
            Raise(new CircuitChange(CircuitChangeKind.NodeAdded, node.Id));
        }

        private void RemoveConnectionAt(int index)
        {
            ConnectionDefinition connection = _connections[index];
            _connections.RemoveAt(index);
            _connectionByDestination.Remove(connection.Destination);
            Touch();
            Raise(new CircuitChange(CircuitChangeKind.Disconnected, connection: connection));
        }

        private NodeId AllocateNodeId()
        {
            return new NodeId(_nextNodeId++);
        }

        private PortId AllocatePortId()
        {
            return new PortId(_nextPortId++);
        }

        private void ReserveNodeId(NodeId id)
        {
            if (!id.IsValid)
            {
                throw new ArgumentException("Invalid node id.", nameof(id));
            }

            if (_nodeLookup.ContainsKey(id))
            {
                throw new InvalidOperationException("Node id " + id + " is already in use.");
            }

            if (id.Value >= _nextNodeId)
            {
                _nextNodeId = id.Value + 1;
            }
        }

        private void ReservePortId(PortId id)
        {
            if (IndexOfPort(_inputs, id) >= 0 || IndexOfPort(_outputs, id) >= 0)
            {
                throw new InvalidOperationException("Port id " + id + " is already in use.");
            }

            if (id.Value >= _nextPortId)
            {
                _nextPortId = id.Value + 1;
            }
        }

        private static int IndexOfPort(List<InterfacePort> ports, PortId id)
        {
            for (int i = 0; i < ports.Count; i++)
            {
                if (ports[i].Id == id)
                {
                    return i;
                }
            }

            return -1;
        }

        private void SetProperty(ref string field, string value)
        {
            EnsureMutable();
            value ??= string.Empty;
            if (string.Equals(field, value, StringComparison.Ordinal))
            {
                return;
            }

            field = value;
            Touch();
            Raise(new CircuitChange(CircuitChangeKind.PropertiesChanged));
        }

        private void EnsureMutable()
        {
            if (IsReadOnly)
            {
                throw new InvalidOperationException(
                    "Circuit '" + _name + "' (" + Reference + ") is published and read-only. Create a new version to edit it.");
            }
        }

        private void Touch()
        {
            ModifiedUtc = DateTime.UtcNow;
        }

        private void Raise(CircuitChange change)
        {
            Changed?.Invoke(change);
        }
    }
}
