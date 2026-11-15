using System;
using System.Collections.Generic;
using DigitalLogicSandbox.Components.Abstractions;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.Rendering.Gates;
using DigitalLogicSandbox.Rendering.Wires;
using UnityEngine;

namespace DigitalLogicSandbox.Rendering
{
    /// <summary>
    /// Cached world-space geometry of a circuit: gate shapes, pin positions and wire routes, derived from the model.
    /// Owned by <see cref="CircuitPresenter"/>, which forwards model changes so caches are invalidated before views
    /// update. Contains no GameObjects, so hit-testing works on the same data the views draw.
    /// <para>
    /// Wires are routed around nearby gates. Two spatial indexes (gate bodies and wire bounds) keep this cheap:
    /// routing asks for the gates near a wire, and when a gate is added, moved, resized or removed only the wires
    /// whose bounds touch its old or new body are invalidated. Every wire invalidated because of a gate change is
    /// queued in <see cref="DrainInvalidatedWires"/> so the presenter can reroute exactly those views.
    /// </para>
    /// </summary>
    public sealed class CircuitGeometry
    {
        private readonly ComponentCatalog _catalog;
        private readonly Dictionary<NodeId, GateGeometry> _gates = new Dictionary<NodeId, GateGeometry>();
        private readonly Dictionary<PinRef, List<Vector3>> _wires = new Dictionary<PinRef, List<Vector3>>();
        private readonly List<ConnectionDefinition> _scratchConnections = new List<ConnectionDefinition>();

        /// <summary>Cell size of the spatial indexes (world units; a few gates wide).</summary>
        private const float IndexCellSize = 6f;

        /// <summary>How far beyond its endpoint gates a wire looks for obstacles.</summary>
        private const float ObstacleSearchMargin = 2f;

        private readonly SpatialHash<NodeId> _nodeIndex = new SpatialHash<NodeId>(IndexCellSize);
        private readonly SpatialHash<PinRef> _wireIndex = new SpatialHash<PinRef>(IndexCellSize);
        private readonly HashSet<PinRef> _invalidated = new HashSet<PinRef>();
        private readonly List<PinRef> _invalidatedOrder = new List<PinRef>();
        private readonly List<NodeId> _scratchNodes = new List<NodeId>();
        private readonly List<PinRef> _scratchWires = new List<PinRef>();
        private readonly List<Rect> _obstacles = new List<Rect>(WireRouter.MaxObstacles);
        private bool _nodeIndexBuilt;

        public CircuitGeometry(ComponentCatalog catalog)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        public CircuitDefinition Circuit { get; private set; }

        public ComponentCatalog Catalog => _catalog;

        public void Bind(CircuitDefinition circuit)
        {
            Circuit = circuit;
            _gates.Clear();
            _wires.Clear();
            _nodeIndex.Clear();
            _wireIndex.Clear();
            _invalidated.Clear();
            _invalidatedOrder.Clear();
            _nodeIndexBuilt = false;
        }

        /// <summary>
        /// Wires whose cached route was dropped because a gate changed (an endpoint moved, or a gate appeared,
        /// moved or disappeared across the route) since the last call. Appends to <paramref name="results"/> and
        /// clears the queue. Connections that no longer exist may be included; callers skip unknown ones.
        /// </summary>
        public void DrainInvalidatedWires(List<PinRef> results)
        {
            results.AddRange(_invalidatedOrder);
            _invalidated.Clear();
            _invalidatedOrder.Clear();
        }

        /// <summary>Invalidates caches affected by a model change.</summary>
        public void OnChanged(CircuitChange change)
        {
            switch (change.Kind)
            {
                case CircuitChangeKind.NodeAdded:
                    UpdateNodeIndex(change.Node, removed: false);
                    break;
                case CircuitChangeKind.NodeRemoved:
                    _gates.Remove(change.Node);
                    InvalidateWiresOf(change.Node);
                    UpdateNodeIndex(change.Node, removed: true);
                    break;
                case CircuitChangeKind.NodeMoved:
                    InvalidateWiresOf(change.Node);
                    UpdateNodeIndex(change.Node, removed: false);
                    break;
                case CircuitChangeKind.NodeRotated:
                case CircuitChangeKind.InterfacePortRenamed:
                    _gates.Remove(change.Node);
                    InvalidateWiresOf(change.Node);
                    UpdateNodeIndex(change.Node, removed: false);
                    break;
                case CircuitChangeKind.Connected:
                case CircuitChangeKind.Disconnected:
                    _wires.Remove(change.Connection.Destination);
                    _wireIndex.Remove(change.Connection.Destination);
                    break;
            }
        }

        public GateGeometry GetGate(NodeId id)
        {
            if (_gates.TryGetValue(id, out GateGeometry geometry))
            {
                return geometry;
            }

            NodeDefinition node = Circuit.GetNode(id);
            geometry = Compute(node);
            _gates[id] = geometry;
            return geometry;
        }

        /// <summary>Computes geometry for a component that is not (yet) in the circuit, e.g. a placement ghost.</summary>
        public GateGeometry ComputeDetached(ComponentRef component, string boundaryTitle)
        {
            if (!_catalog.TryGetSignature(component, out ComponentSignature signature))
            {
                signature = new ComponentSignature(null, null);
            }

            string title = component.IsBoundary ? boundaryTitle : TitleOf(component);
            string icon = component.Kind == ComponentKind.Custom &&
                _catalog.TryGetDefinition(component.Definition, out CircuitDefinition definition)
                    ? definition.Icon
                    : null;
            return GateLayout.Compute(component.Kind, signature, Rotation.R0, title, icon, component.DisplayStyle);
        }

        public Vector2 GetPosition(NodeId id)
        {
            CircuitPoint position = Circuit.GetNode(id).Position;
            return new Vector2(position.X, position.Y);
        }

        public Rect GetBounds(NodeId id)
        {
            GateGeometry geometry = GetGate(id);
            Vector2 center = GetPosition(id);
            return new Rect(center - geometry.Size * 0.5f, geometry.Size);
        }

        /// <summary>World position of a pin; determines whether it is an input or output pin of its node.</summary>
        public bool TryGetPin(PinRef pin, out Vector2 world, out PortDirection direction)
        {
            world = default;
            direction = PortDirection.Input;
            if (Circuit == null || !Circuit.ContainsNode(pin.Node))
            {
                return false;
            }

            GateGeometry geometry = GetGate(pin.Node);
            Vector2 center = GetPosition(pin.Node);
            int index = geometry.IndexOfInput(pin.Port);
            if (index >= 0)
            {
                world = center + geometry.InputPins[index];
                return true;
            }

            index = geometry.IndexOfOutput(pin.Port);
            if (index >= 0)
            {
                world = center + geometry.OutputPins[index];
                direction = PortDirection.Output;
                return true;
            }

            return false;
        }

        /// <summary>Route of the wire driving <paramref name="destination"/> (cached until an endpoint moves).</summary>
        public IReadOnlyList<Vector3> GetWirePath(PinRef destination)
        {
            if (_wires.TryGetValue(destination, out List<Vector3> path))
            {
                return path;
            }

            path = new List<Vector3>(8);
            if (Circuit.TryGetConnectionTo(destination, out ConnectionDefinition connection) &&
                TryGetPin(connection.Source, out Vector2 from, out _) &&
                TryGetPin(destination, out Vector2 to, out _))
            {
                Rect fromBody = GetBounds(connection.Source.Node);
                Rect toBody = GetBounds(destination.Node);
                CollectObstacles(connection.Source.Node, destination.Node, from, to, fromBody, toBody);
                WireRouter.Route(from, GetPinNormal(connection.Source.Node, PortDirection.Output), fromBody, to,
                    GetPinNormal(destination.Node, PortDirection.Input), toBody, _obstacles, _obstacles.Count, path);
                _wireIndex.Set(destination, PathBounds(path));
            }

            _wires[destination] = path;
            return path;
        }

        /// <summary>
        /// Gathers the bodies of gates near the straight-line region of a wire (endpoint gates excluded), nearest
        /// first, capped at <see cref="WireRouter.MaxObstacles"/>.
        /// </summary>
        private void CollectObstacles(NodeId source, NodeId destination, Vector2 from, Vector2 to, Rect fromBody,
            Rect toBody)
        {
            _obstacles.Clear();
            EnsureNodeIndex();
            float xMin = Mathf.Min(from.x, to.x) - ObstacleSearchMargin;
            float yMin = Mathf.Min(from.y, to.y) - ObstacleSearchMargin;
            float xMax = Mathf.Max(from.x, to.x) + ObstacleSearchMargin;
            float yMax = Mathf.Max(from.y, to.y) + ObstacleSearchMargin;
            Rect area = Rect.MinMaxRect(xMin, yMin, xMax, yMax);

            _scratchNodes.Clear();
            _nodeIndex.Query(area, _scratchNodes);
            Vector2 middle = (from + to) * 0.5f;
            for (int i = 0; i < _scratchNodes.Count; i++)
            {
                NodeId node = _scratchNodes[i];
                if (node == source || node == destination || !_nodeIndex.TryGetRect(node, out Rect body))
                {
                    continue;
                }

                // Gates overlapping an endpoint gate (stacked parts) would block every route; ignore them.
                if (body.Overlaps(fromBody) || body.Overlaps(toBody))
                {
                    continue;
                }

                if (_obstacles.Count < WireRouter.MaxObstacles)
                {
                    _obstacles.Add(body);
                    continue;
                }

                // Keep the nearest ones: replace the farthest kept obstacle when this one is closer.
                int farthest = 0;
                float farthestDistance = -1f;
                for (int k = 0; k < _obstacles.Count; k++)
                {
                    float d = (_obstacles[k].center - middle).sqrMagnitude;
                    if (d > farthestDistance)
                    {
                        farthestDistance = d;
                        farthest = k;
                    }
                }

                if ((body.center - middle).sqrMagnitude < farthestDistance)
                {
                    _obstacles[farthest] = body;
                }
            }
        }

        private void EnsureNodeIndex()
        {
            if (_nodeIndexBuilt || Circuit == null)
            {
                return;
            }

            _nodeIndexBuilt = true;
            IReadOnlyList<NodeDefinition> nodes = Circuit.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                _nodeIndex.Set(nodes[i].Id, GetBounds(nodes[i].Id));
            }
        }

        /// <summary>
        /// Moves a gate's body in the index and drops the cached routes of wires that its old or new body touches
        /// (they may now be blocked, or free to take a shorter path).
        /// </summary>
        private void UpdateNodeIndex(NodeId node, bool removed)
        {
            if (!_nodeIndexBuilt)
            {
                return; // built lazily from the model on the next route
            }

            bool hadOld = _nodeIndex.TryGetRect(node, out Rect oldBody);
            if (removed || Circuit == null || !Circuit.ContainsNode(node))
            {
                _nodeIndex.Remove(node);
            }
            else
            {
                Rect newBody = GetBounds(node);
                _nodeIndex.Set(node, newBody);
                InvalidateWiresTouching(newBody);
            }

            if (hadOld)
            {
                InvalidateWiresTouching(oldBody);
            }
        }

        private void InvalidateWiresTouching(Rect body)
        {
            if (_wireIndex.Count == 0)
            {
                return;
            }

            _scratchWires.Clear();
            _wireIndex.Query(Inflate(body, WireRouter.BodyMargin), _scratchWires);
            for (int i = 0; i < _scratchWires.Count; i++)
            {
                InvalidateWire(_scratchWires[i]);
            }
        }

        private void InvalidateWire(PinRef destination)
        {
            _wires.Remove(destination);
            _wireIndex.Remove(destination);
            if (_invalidated.Add(destination))
            {
                _invalidatedOrder.Add(destination);
            }
        }

        private static Rect PathBounds(List<Vector3> path)
        {
            if (path.Count == 0)
            {
                return default;
            }

            float xMin = path[0].x, xMax = path[0].x, yMin = path[0].y, yMax = path[0].y;
            for (int i = 1; i < path.Count; i++)
            {
                Vector3 p = path[i];
                xMin = Mathf.Min(xMin, p.x);
                xMax = Mathf.Max(xMax, p.x);
                yMin = Mathf.Min(yMin, p.y);
                yMax = Mathf.Max(yMax, p.y);
            }

            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        private static Rect Inflate(Rect rect, float margin)
        {
            return Rect.MinMaxRect(rect.xMin - margin, rect.yMin - margin, rect.xMax + margin, rect.yMax + margin);
        }

        /// <summary>
        /// Outward normal of a node's pins on one side: outputs face right and inputs face left before rotation.
        /// </summary>
        public Vector2 GetPinNormal(NodeId node, PortDirection direction)
        {
            Vector2 local = direction == PortDirection.Output ? Vector2.right : Vector2.left;
            return GateLayout.Rotate(local, GetGate(node).Rotation);
        }

        /// <summary>Bit width carried by the connection into <paramref name="destination"/> (its source pin's width).</summary>
        public int GetConnectionWidth(PinRef destination)
        {
            if (Circuit == null || !Circuit.TryGetConnectionTo(destination, out ConnectionDefinition connection) ||
                !Circuit.TryGetNode(connection.Source.Node, out NodeDefinition source) ||
                !_catalog.TryGetSignature(source.Component, out ComponentSignature signature))
            {
                return 1;
            }

            return Core.Validation.ConnectionRules.WidthOf(signature.Outputs, connection.Source.Port);
        }

        /// <summary>Title drawn on clock nodes.</summary>
        public const string ClockTitle = "CLK";

        /// <summary>Display title of a node: primitive name, custom gate name, or interface port name.</summary>
        public string TitleOf(NodeDefinition node)
        {
            if (node.IsBoundary)
            {
                return Circuit.TryGetInterfacePort(node.InterfacePort, out InterfacePort port) ? port.Name : "?";
            }

            return TitleOf(node.Component);
        }

        public string TitleOf(ComponentRef component)
        {
            switch (component.Kind)
            {
                case ComponentKind.Primitive:
                    return _catalog.TryGetPrimitive(component, out IPrimitiveComponent primitive)
                        ? primitive.DisplayName
                        : "? " + component.PrimitiveId;
                case ComponentKind.Custom:
                    if (!_catalog.TryGetDefinition(component.Definition, out CircuitDefinition definition))
                    {
                        return "Missing gate";
                    }

                    // Show the version once a gate has several, so old and new instances are distinguishable.
                    return definition.Version > 1 ? definition.Name + " v" + definition.Version : definition.Name;
                case ComponentKind.InputBoundary:
                    return "Input";
                case ComponentKind.Clock:
                    return ClockTitle;
                case ComponentKind.Splitter:
                case ComponentKind.Merger:
                    return string.Empty; // the shape and bit labels say it all
                case ComponentKind.BusJoin:
                    return "JOIN";
                case ComponentKind.TriState:
                    return "TRI";
                case ComponentKind.Display:
                    return DisplayCaption(component.DisplayStyle);
                default:
                    return "Output";
            }
        }

        /// <summary>Small caption above a display's digits or lamps (none for LEDs and seven-segment digits).</summary>
        public static string DisplayCaption(DisplayStyle style)
        {
            switch (style)
            {
                case DisplayStyle.Binary:
                    return "BIN";
                case DisplayStyle.Hex:
                    return "HEX";
                case DisplayStyle.Decimal:
                    return "DEC";
                case DisplayStyle.SignedDecimal:
                    return "SIGNED";
                default:
                    return string.Empty;
            }
        }

        private GateGeometry Compute(NodeDefinition node)
        {
            if (!_catalog.TryGetSignature(node.Component, out ComponentSignature signature))
            {
                signature = new ComponentSignature(null, null);
            }

            string icon = null;
            if (node.Component.Kind == ComponentKind.Custom &&
                _catalog.TryGetDefinition(node.Component.Definition, out CircuitDefinition definition))
            {
                icon = definition.Icon;
            }

            return GateLayout.Compute(node.Component.Kind, signature, node.Rotation, TitleOf(node), icon,
                node.Component.DisplayStyle);
        }

        private void InvalidateWiresOf(NodeId node)
        {
            if (Circuit == null)
            {
                return;
            }

            _scratchConnections.Clear();
            Circuit.GetConnectionsFrom(node, _scratchConnections);
            Circuit.GetConnectionsTo(node, _scratchConnections);
            for (int i = 0; i < _scratchConnections.Count; i++)
            {
                InvalidateWire(_scratchConnections[i].Destination);
            }

            // Connections already removed from the model (node deletion) are keyed by destination on this node.
            if (_wires.Count > 0)
            {
                List<PinRef> stale = null;
                foreach (PinRef key in _wires.Keys)
                {
                    if (key.Node == node)
                    {
                        (stale ??= new List<PinRef>()).Add(key);
                    }
                }

                if (stale != null)
                {
                    for (int i = 0; i < stale.Count; i++)
                    {
                        _wires.Remove(stale[i]);
                        _wireIndex.Remove(stale[i]);
                    }
                }
            }
        }
    }
}
