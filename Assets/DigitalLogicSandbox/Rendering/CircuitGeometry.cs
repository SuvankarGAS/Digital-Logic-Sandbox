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
    /// <para>
    /// Settled wires use <see cref="GridRouter"/> (a maze search that avoids every gate in its area and keeps off
    /// other nets' lanes). While the user drags gates, <see cref="InteractiveRouting"/> switches to the fast
    /// candidate <see cref="WireRouter"/>; ending it reroutes the wires that were routed the fast way.
    /// </para>
    /// <para>
    /// Routing one wire at a time makes routes depend on the order: <see cref="ImproveRoutes"/> (rip-up and reroute)
    /// reconsiders the wires that could do better after a change. <see cref="GetCrossings"/> gives the points where
    /// a wire crosses another net, queued through <see cref="DrainCrossingChanges"/> when they may have changed.
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
        private readonly List<Rect> _areaObstacles = new List<Rect>(64);
        private readonly List<Vector4> _lanes = new List<Vector4>(128);
        private readonly HashSet<PinRef> _fastRouted = new HashSet<PinRef>();
        private readonly List<PinRef> _scratchFast = new List<PinRef>();
        private bool _nodeIndexBuilt;
        private bool _interactive;

        // Route improvement (rip-up and reroute): see ImproveRoutes.
        private readonly HashSet<PinRef> _laneSensitive = new HashSet<PinRef>();
        private readonly List<Rect> _freedAreas = new List<Rect>();
        private readonly List<PinRef> _recentRoutes = new List<PinRef>();
        private readonly List<PinRef> _improveQueue = new List<PinRef>();
        private readonly HashSet<PinRef> _improveQueued = new HashSet<PinRef>();
        private readonly Dictionary<PinRef, int> _improveAttempts = new Dictionary<PinRef, int>();
        private readonly List<PinRef> _scratchImprove = new List<PinRef>();
        private readonly List<Vector3> _candidatePath = new List<Vector3>(16);
        private readonly Dictionary<PinRef, List<Vector3>> _demand = new Dictionary<PinRef, List<Vector3>>();
        private readonly Stack<List<Vector3>> _pathPool = new Stack<List<Vector3>>();
        private readonly List<Vector4> _softLanes = new List<Vector4>(32);
        private static readonly List<Vector4> NoLanes = new List<Vector4>();
        private bool _hasInteractiveFreed;
        private Rect _interactiveFreed;

        // Crossings of other nets (drawn as hops or gaps): see GetCrossings.
        private readonly Dictionary<PinRef, List<Vector3>> _crossings = new Dictionary<PinRef, List<Vector3>>();
        private readonly Stack<List<Vector3>> _crossingPool = new Stack<List<Vector3>>();
        private readonly HashSet<PinRef> _crossingDirty = new HashSet<PinRef>();
        private readonly List<PinRef> _crossingDirtyOrder = new List<PinRef>();
        private readonly List<PinRef> _scratchCrossWires = new List<PinRef>();
        private readonly List<Vector4> _crossSegments = new List<Vector4>(64);
        private readonly List<Rect> _crossBodies = new List<Rect>(16);
        private static readonly List<Vector3> NoCrossings = new List<Vector3>();

        /// <summary>
        /// Padding for wire-bounds queries: a straight wire has zero-height (or zero-width) bounds, which a strict
        /// rectangle overlap test would never match against another straight wire on the same line.
        /// </summary>
        private const float QueryPadding = 0.05f;

        /// <summary>Most times one wire is rerouted by a single <see cref="ImproveRoutes"/> call.</summary>
        private const int MaxImprovementsPerWire = 2;

        /// <summary>Least cost reduction (world units) for an improved route to replace the current one.</summary>
        private const float ImprovementEpsilon = 0.05f;

        /// <summary>
        /// Most real cost a wire may give up to step off another wire's ideal route (a couple of crossings): enough
        /// to break ties, never enough to make a wire noticeably worse.
        /// </summary>
        private const float YieldTolerance = 2f * GridRouter.CrossingCost;

        /// <summary>Margin around a wire's endpoints searched by the maze router (first try, then the wide retry).</summary>
        private const float RouteMargin = 3f;
        private const float WideRouteMargin = 12f;

        /// <summary>Most gate bodies the maze router considers per wire.</summary>
        private const int MaxAreaObstacles = 512;

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
            _fastRouted.Clear();
            _interactive = false;
            _nodeIndexBuilt = false;
            _laneSensitive.Clear();
            _freedAreas.Clear();
            _recentRoutes.Clear();
            _hasInteractiveFreed = false;
            foreach (List<Vector3> list in _crossings.Values)
            {
                ReleaseCrossingList(list);
            }

            _crossings.Clear();
            _crossingDirty.Clear();
            _crossingDirtyOrder.Clear();
        }

        /// <summary>
        /// While true (gates being dragged), wires are routed with the fast candidate router. Setting it back to
        /// false invalidates those wires so the next <see cref="DrainInvalidatedWires"/> reroutes them properly.
        /// </summary>
        public bool InteractiveRouting
        {
            get => _interactive;
            set
            {
                if (_interactive == value)
                {
                    return;
                }

                _interactive = value;
                if (!value && _hasInteractiveFreed)
                {
                    // Room opened during the drag (the dragged wires' original routes) is offered on settle.
                    _freedAreas.Add(_interactiveFreed);
                    _hasInteractiveFreed = false;
                }

                if (!value && _fastRouted.Count > 0)
                {
                    _scratchFast.Clear();
                    foreach (PinRef destination in _fastRouted)
                    {
                        _scratchFast.Add(destination);
                    }

                    _fastRouted.Clear();
                    // Deterministic order: the maze router sees earlier routes as occupied lanes.
                    _scratchFast.Sort(PinRefComparer.Instance);
                    for (int i = 0; i < _scratchFast.Count; i++)
                    {
                        InvalidateWire(_scratchFast[i]);
                    }
                }
            }
        }

        /// <summary>Number of wires the maze router could not route (they fell back to the candidate router).</summary>
        public int RouteFallbackCount { get; private set; }

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
                    DropRoute(change.Connection.Destination);
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
            return GateLayout.Compute(component.Kind, signature, Rotation.R0, title, icon, component.DisplayStyle,
                component.LedColor);
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
                Vector2 fromNormal = GetPinNormal(connection.Source.Node, PortDirection.Output);
                Vector2 toNormal = GetPinNormal(destination.Node, PortDirection.Input);
                bool routed = false;
                if (!_interactive)
                {
                    routed = TryMazeRoute(connection, from, fromNormal, fromBody, to, toNormal, toBody, RouteMargin,
                            path) ||
                        TryMazeRoute(connection, from, fromNormal, fromBody, to, toNormal, toBody, WideRouteMargin, path);
                    if (!routed)
                    {
                        RouteFallbackCount++;
                    }
                }
                else
                {
                    _fastRouted.Add(destination);
                }

                if (!routed)
                {
                    CollectObstacles(connection.Source.Node, destination.Node, from, to, fromBody, toBody);
                    WireRouter.Route(from, fromNormal, fromBody, to, toNormal, toBody, _obstacles, _obstacles.Count,
                        path);
                    _laneSensitive.Remove(destination);
                }
                else
                {
                    SetLaneSensitive(destination, GridRouter.LastLaneInfluenced);
                    _recentRoutes.Add(destination);
                }

                Rect bounds = PathBounds(path);
                _wireIndex.Set(destination, bounds);
                MarkCrossingsAround(destination, bounds);
            }

            _wires[destination] = path;
            return path;
        }

        /// <summary>
        /// Routes one wire with the maze router: every gate body in the search area is an obstacle (gates overlapping
        /// an endpoint gate are ignored, as stacked parts would block every route) and every cached wire of another
        /// source in the area is an occupied lane.
        /// </summary>
        private bool TryMazeRoute(ConnectionDefinition connection, Vector2 from, Vector2 fromNormal, Rect fromBody,
            Vector2 to, Vector2 toNormal, Rect toBody, float margin, List<Vector3> path,
            LaneMode lanes = LaneMode.OtherNets)
        {
            Rect area = Rect.MinMaxRect(
                Mathf.Min(Mathf.Min(from.x, to.x), Mathf.Min(fromBody.xMin, toBody.xMin)) - margin,
                Mathf.Min(Mathf.Min(from.y, to.y), Mathf.Min(fromBody.yMin, toBody.yMin)) - margin,
                Mathf.Max(Mathf.Max(from.x, to.x), Mathf.Max(fromBody.xMax, toBody.xMax)) + margin,
                Mathf.Max(Mathf.Max(from.y, to.y), Mathf.Max(fromBody.yMax, toBody.yMax)) + margin);

            EnsureNodeIndex();
            _areaObstacles.Clear();
            _scratchNodes.Clear();
            _nodeIndex.Query(area, _scratchNodes);
            NodeId source = connection.Source.Node;
            NodeId destination = connection.Destination.Node;
            for (int i = 0; i < _scratchNodes.Count && _areaObstacles.Count < MaxAreaObstacles; i++)
            {
                NodeId node = _scratchNodes[i];
                if (node == source || node == destination || !_nodeIndex.TryGetRect(node, out Rect body) ||
                    body.Overlaps(fromBody) || body.Overlaps(toBody))
                {
                    continue;
                }

                _areaObstacles.Add(body);
            }

            if (lanes == LaneMode.None)
            {
                _lanes.Clear();
            }
            else
            {
                GatherSegments(connection, area, _lanes, _scratchWires);
            }

            List<Vector4> soft = null;
            if (lanes == LaneMode.OtherNetsAndDemand && _demand.Count > 0)
            {
                GatherDemand(connection, area);
                soft = _softLanes;
            }

            return GridRouter.TryRoute(from, fromNormal, fromBody, to, toNormal, toBody, area, _areaObstacles, _lanes,
                soft, path);
        }

        /// <summary>Which other wires a maze search sees.</summary>
        private enum LaneMode
        {
            /// <summary>Other nets' wires are lanes to keep off (normal routing).</summary>
            OtherNets,

            /// <summary>No lanes: the route the wire would take if it were alone (its ideal route).</summary>
            None,

            /// <summary>Other nets' lanes plus soft lanes along other wires' ideal routes (route improvement).</summary>
            OtherNetsAndDemand,
        }

        /// <summary>Collects into <see cref="_softLanes"/> the ideal routes of other sources' detoured wires.</summary>
        private void GatherDemand(ConnectionDefinition connection, Rect area)
        {
            _softLanes.Clear();
            foreach (KeyValuePair<PinRef, List<Vector3>> pair in _demand)
            {
                if (pair.Key == connection.Destination || !PathBounds(pair.Value).Overlaps(Inflate(area, QueryPadding)) ||
                    !Circuit.TryGetConnectionTo(pair.Key, out ConnectionDefinition other) ||
                    other.Source == connection.Source)
                {
                    continue;
                }

                List<Vector3> ideal = pair.Value;
                for (int k = 0; k < ideal.Count - 1; k++)
                {
                    _softLanes.Add(new Vector4(ideal[k].x, ideal[k].y, ideal[k + 1].x, ideal[k + 1].y));
                }
            }
        }

        /// <summary>
        /// Collects the segments (x0, y0, x1, y1) of every cached wire of <em>another</em> source whose bounds touch
        /// <paramref name="area"/>. Fan-out wires of the same output are skipped: they may share a trunk.
        /// </summary>
        private void GatherSegments(ConnectionDefinition connection, Rect area, List<Vector4> segments,
            List<PinRef> scratch)
        {
            segments.Clear();
            scratch.Clear();
            _wireIndex.Query(area, scratch);
            for (int i = 0; i < scratch.Count; i++)
            {
                PinRef other = scratch[i];
                if (other == connection.Destination || !_wires.TryGetValue(other, out List<Vector3> otherPath) ||
                    !Circuit.TryGetConnectionTo(other, out ConnectionDefinition otherConnection) ||
                    otherConnection.Source == connection.Source)
                {
                    continue;
                }

                for (int k = 0; k < otherPath.Count - 1; k++)
                {
                    Vector3 p = otherPath[k];
                    Vector3 q = otherPath[k + 1];
                    segments.Add(new Vector4(p.x, p.y, q.x, q.y));
                }
            }
        }

        /// <summary>Gate bodies overlapping <paramref name="area"/> (they decide which side a hop bulges to).</summary>
        private void GatherBodies(Rect area, List<Rect> bodies)
        {
            bodies.Clear();
            EnsureNodeIndex();
            _scratchNodes.Clear();
            _nodeIndex.Query(area, _scratchNodes);
            for (int i = 0; i < _scratchNodes.Count; i++)
            {
                if (_nodeIndex.TryGetRect(_scratchNodes[i], out Rect body))
                {
                    bodies.Add(body);
                }
            }
        }

        /// <summary>Number of wires <see cref="ImproveRoutes"/> tried to reroute (for tests and profiling).</summary>
        public int ImprovementAttempts { get; private set; }

        /// <summary>Number of wires <see cref="ImproveRoutes"/> gave a better route.</summary>
        public int ImprovedRoutes { get; private set; }

        /// <summary>
        /// Rip-up and reroute. Wires are routed one at a time, each seeing only the wires routed before it, so a wire
        /// routed early may share a lane with a later one, or keep a detour around a wire that has since moved away.
        /// This pass reconsiders exactly those wires:
        /// <list type="bullet">
        /// <item>wires that were shaped by other nets (<see cref="GridRouter.LastLaneInfluenced"/>) near a route that
        /// was dropped since the last call (room may have opened up);</item>
        /// <item>wires overlapping a newly routed wire of another net, and that new wire itself;</item>
        /// <item>wires lying on the <em>ideal</em> route (the route it would take alone) of a newly routed wire that
        /// had to detour around them, and then that detoured wire.</item>
        /// </list>
        /// Each candidate is searched again with every other wire as a lane, plus soft lanes
        /// (<see cref="GridRouter.SoftLaneCost"/>) along the ideal routes of detoured wires. The new route replaces the
        /// current one when it lowers <see cref="GridRouter.PathCost"/> by more than <see cref="ImprovementEpsilon"/>,
        /// or when it steps off a detoured wire's ideal route while giving up at most <see cref="YieldTolerance"/>.
        /// Lane and crossing costs are shared by the two wires involved, so improvements lower the total cost of the
        /// layout; yields are bounded by the per-wire limit. Accepted changes cascade to wires near the vacated route.
        /// <paramref name="budget"/> caps the searches (ideal-route searches included); each wire is tried at most
        /// <see cref="MaxImprovementsPerWire"/> times. Does nothing while <see cref="InteractiveRouting"/> is on.
        /// </summary>
        /// <param name="budget">Most wires searched again by this call.</param>
        /// <param name="changed">Receives the wires whose cached route changed (their views need the new path).</param>
        /// <returns>The number of improved wires.</returns>
        public int ImproveRoutes(int budget, List<PinRef> changed)
        {
            if (_interactive)
            {
                return 0; // seeds are kept until the drag settles
            }

            _improveQueue.Clear();
            _improveQueued.Clear();
            _improveAttempts.Clear();
            ReleaseDemand();
            int attempts = 0;
            int improved = 0;
            if (Circuit != null && budget > 0)
            {
                for (int i = 0; i < _freedAreas.Count; i++)
                {
                    EnqueueSensitiveNear(_freedAreas[i], default, hasExclude: false);
                }

                _recentRoutes.Sort(PinRefComparer.Instance);
                for (int i = 0; i < _recentRoutes.Count; i++)
                {
                    PinRef recent = _recentRoutes[i];
                    EnqueueOverlapping(recent);
                    if (attempts < budget && _laneSensitive.Contains(recent) && TryRecordDemand(recent))
                    {
                        attempts++; // the ideal-route search counts against the budget
                    }
                }

                _improveQueue.Sort(PinRefComparer.Instance);
            }

            _freedAreas.Clear();
            _recentRoutes.Clear();

            for (int q = 0; q < _improveQueue.Count && attempts < budget; q++)
            {
                PinRef destination = _improveQueue[q];
                _improveQueued.Remove(destination);
                if (!_wires.TryGetValue(destination, out List<Vector3> current) || current.Count < 2 ||
                    !Circuit.TryGetConnectionTo(destination, out ConnectionDefinition connection) ||
                    !TryGetPin(connection.Source, out Vector2 from, out _) ||
                    !TryGetPin(destination, out Vector2 to, out _))
                {
                    continue;
                }

                attempts++;
                ImprovementAttempts++;
                Rect fromBody = GetBounds(connection.Source.Node);
                Rect toBody = GetBounds(destination.Node);
                Vector2 fromNormal = GetPinNormal(connection.Source.Node, PortDirection.Output);
                Vector2 toNormal = GetPinNormal(destination.Node, PortDirection.Input);
                bool routed =
                    TryMazeRoute(connection, from, fromNormal, fromBody, to, toNormal, toBody, RouteMargin,
                        _candidatePath, LaneMode.OtherNetsAndDemand) ||
                    TryMazeRoute(connection, from, fromNormal, fromBody, to, toNormal, toBody, WideRouteMargin,
                        _candidatePath, LaneMode.OtherNetsAndDemand);
                if (!routed)
                {
                    continue;
                }

                bool influenced = GridRouter.LastLaneInfluenced;

                // _lanes / _softLanes hold the wires around the last search: compare both routes under the same lanes.
                float currentCost = GridRouter.PathCost(current, _lanes);
                float candidateCost = GridRouter.PathCost(_candidatePath, _lanes);
                bool better = candidateCost < currentCost - ImprovementEpsilon;
                if (!better)
                {
                    bool hasSoft = _demand.Count > 0 && _softLanes.Count > 0;
                    bool yields = hasSoft && candidateCost <= currentCost + YieldTolerance &&
                        GridRouter.SoftCost(_candidatePath, _softLanes) <
                        GridRouter.SoftCost(current, _softLanes) - ImprovementEpsilon;
                    if (!yields)
                    {
                        continue;
                    }
                }

                // A wire that found a better route no longer needs others to make room for it.
                RemoveDemand(destination);

                Rect oldBounds = PathBounds(current);
                current.Clear();
                current.AddRange(_candidatePath);
                Rect newBounds = PathBounds(current);
                _wireIndex.Set(destination, newBounds);
                SetLaneSensitive(destination, influenced);
                MarkCrossingsAround(destination, oldBounds);
                MarkCrossingsAround(destination, newBounds);
                changed.Add(destination);
                improved++;
                ImprovedRoutes++;

                // Cascade: the vacated route may let nearby wires straighten; wires the new route runs along may
                // now prefer another lane.
                EnqueueSensitiveNear(oldBounds, destination, hasExclude: true);
                EnqueueOverlapping(destination);
            }

            _improveQueue.Clear();
            _improveQueued.Clear();
            ReleaseDemand();
            return improved;
        }

        /// <summary>
        /// If <paramref name="destination"/>'s route is worse than the route it would take alone (its ideal route),
        /// records that ideal route as demand and queues the other nets' wires lying on it, then the wire itself.
        /// </summary>
        /// <returns>True when an ideal-route search was run.</returns>
        private bool TryRecordDemand(PinRef destination)
        {
            if (!_wires.TryGetValue(destination, out List<Vector3> current) || current.Count < 2 ||
                !Circuit.TryGetConnectionTo(destination, out ConnectionDefinition connection) ||
                !TryGetPin(connection.Source, out Vector2 from, out _) ||
                !TryGetPin(destination, out Vector2 to, out _))
            {
                return false;
            }

            Rect fromBody = GetBounds(connection.Source.Node);
            Rect toBody = GetBounds(destination.Node);
            Vector2 fromNormal = GetPinNormal(connection.Source.Node, PortDirection.Output);
            Vector2 toNormal = GetPinNormal(destination.Node, PortDirection.Input);
            List<Vector3> ideal = _pathPool.Count > 0 ? _pathPool.Pop() : new List<Vector3>(16);
            bool routed = TryMazeRoute(connection, from, fromNormal, fromBody, to, toNormal, toBody, RouteMargin, ideal,
                    LaneMode.None) ||
                TryMazeRoute(connection, from, fromNormal, fromBody, to, toNormal, toBody, WideRouteMargin, ideal,
                    LaneMode.None);
            if (!routed || GridRouter.PathCost(ideal, NoLanes) >= GridRouter.PathCost(current, NoLanes) - ImprovementEpsilon)
            {
                ideal.Clear();
                _pathPool.Push(ideal);
                return true; // already as good as it gets alone: nothing to ask of other wires
            }

            bool found = false;
            _scratchImprove.Clear();
            _wireIndex.Query(Inflate(PathBounds(ideal), QueryPadding), _scratchImprove);
            for (int i = 0; i < _scratchImprove.Count; i++)
            {
                PinRef other = _scratchImprove[i];
                if (other == destination || !_wires.TryGetValue(other, out List<Vector3> otherPath) ||
                    !Circuit.TryGetConnectionTo(other, out ConnectionDefinition otherConnection) ||
                    otherConnection.Source == connection.Source ||
                    GridRouter.OverlapLength(ideal, otherPath) < 0.01f)
                {
                    continue;
                }

                EnqueueImprovement(other);
                found = true;
            }

            if (!found)
            {
                ideal.Clear();
                _pathPool.Push(ideal);
                return true; // detoured around gates, not wires
            }

            RemoveDemand(destination);
            _demand[destination] = ideal;
            EnqueueImprovement(destination);
            return true;
        }

        private void RemoveDemand(PinRef destination)
        {
            if (_demand.TryGetValue(destination, out List<Vector3> ideal))
            {
                _demand.Remove(destination);
                ideal.Clear();
                _pathPool.Push(ideal);
            }
        }

        private void ReleaseDemand()
        {
            foreach (List<Vector3> ideal in _demand.Values)
            {
                ideal.Clear();
                _pathPool.Push(ideal);
            }

            _demand.Clear();
        }

        private void EnqueueImprovement(PinRef destination)
        {
            _improveAttempts.TryGetValue(destination, out int tried);
            if (tried >= MaxImprovementsPerWire || !_improveQueued.Add(destination))
            {
                return;
            }

            _improveAttempts[destination] = tried + 1;
            _improveQueue.Add(destination);
        }

        /// <summary>Queues the lane-sensitive wires whose bounds touch <paramref name="area"/>.</summary>
        private void EnqueueSensitiveNear(Rect area, PinRef exclude, bool hasExclude)
        {
            if (_laneSensitive.Count == 0)
            {
                return;
            }

            _scratchImprove.Clear();
            _wireIndex.Query(Inflate(area, WireRouter.Stub), _scratchImprove);
            for (int i = 0; i < _scratchImprove.Count; i++)
            {
                PinRef wire = _scratchImprove[i];
                if ((!hasExclude || wire != exclude) && _laneSensitive.Contains(wire))
                {
                    EnqueueImprovement(wire);
                }
            }
        }

        /// <summary>
        /// Queues every wire of another source that runs along <paramref name="destination"/>'s route, then the wire
        /// itself, so the earlier-routed partner gets the first chance to move away.
        /// </summary>
        private void EnqueueOverlapping(PinRef destination)
        {
            if (!_wires.TryGetValue(destination, out List<Vector3> path) || path.Count < 2 ||
                !Circuit.TryGetConnectionTo(destination, out ConnectionDefinition connection))
            {
                return;
            }

            bool found = false;
            _scratchImprove.Clear();
            _wireIndex.Query(Inflate(PathBounds(path), QueryPadding), _scratchImprove);
            for (int i = 0; i < _scratchImprove.Count; i++)
            {
                PinRef other = _scratchImprove[i];
                if (other == destination || !_wires.TryGetValue(other, out List<Vector3> otherPath) ||
                    !Circuit.TryGetConnectionTo(other, out ConnectionDefinition otherConnection) ||
                    otherConnection.Source == connection.Source ||
                    GridRouter.OverlapLength(path, otherPath) < 0.01f)
                {
                    continue;
                }

                EnqueueImprovement(other);
                found = true;
            }

            if (found)
            {
                EnqueueImprovement(destination);
            }
        }

        private void SetLaneSensitive(PinRef destination, bool sensitive)
        {
            if (sensitive)
            {
                _laneSensitive.Add(destination);
            }
            else
            {
                _laneSensitive.Remove(destination);
            }
        }

        /// <summary>
        /// Points where <paramref name="destination"/>'s wire crosses another net's wire, on its own vertical segments
        /// (see <see cref="WireCrossings"/>) as (x, y, hop side). Cached until this wire or a wire near it is rerouted;
        /// those wires are queued for <see cref="DrainCrossingChanges"/>.
        /// </summary>
        public IReadOnlyList<Vector3> GetCrossings(PinRef destination)
        {
            if (_crossings.TryGetValue(destination, out List<Vector3> crossings))
            {
                return crossings;
            }

            if (Circuit == null || !Circuit.TryGetConnectionTo(destination, out ConnectionDefinition connection))
            {
                return NoCrossings;
            }

            GetWirePath(destination);
            crossings = _crossingPool.Count > 0 ? _crossingPool.Pop() : new List<Vector3>(4);
            crossings.Clear();
            List<Vector3> path = _wires[destination];
            if (path.Count >= 2)
            {
                Rect area = Inflate(PathBounds(path), WireCrossings.HopReach + QueryPadding);
                GatherSegments(connection, area, _crossSegments, _scratchCrossWires);
                GatherBodies(area, _crossBodies);
                WireCrossings.Find(path, _crossSegments, crossings, _crossBodies);
            }

            _crossings[destination] = crossings;
            return crossings;
        }

        /// <summary>
        /// Wires whose crossings may have changed since the last call (appended to <paramref name="results"/>; the
        /// queue is cleared). Wires that no longer exist may be included; callers skip unknown ones.
        /// </summary>
        public void DrainCrossingChanges(List<PinRef> results)
        {
            results.AddRange(_crossingDirtyOrder);
            _crossingDirty.Clear();
            _crossingDirtyOrder.Clear();
        }

        /// <summary>
        /// Drops the cached crossings of <paramref name="destination"/> and of every wire whose bounds touch
        /// <paramref name="bounds"/> (its old or new route), and queues them.
        /// </summary>
        private void MarkCrossingsAround(PinRef destination, Rect bounds)
        {
            DirtyCrossings(destination);
            _scratchCrossWires.Clear();
            _wireIndex.Query(Inflate(bounds, QueryPadding), _scratchCrossWires);
            for (int i = 0; i < _scratchCrossWires.Count; i++)
            {
                if (_scratchCrossWires[i] != destination)
                {
                    DirtyCrossings(_scratchCrossWires[i]);
                }
            }
        }

        private void DirtyCrossings(PinRef destination)
        {
            if (_crossings.TryGetValue(destination, out List<Vector3> list))
            {
                _crossings.Remove(destination);
                ReleaseCrossingList(list);
            }

            if (_crossingDirty.Add(destination))
            {
                _crossingDirtyOrder.Add(destination);
            }
        }

        private void ReleaseCrossingList(List<Vector3> list)
        {
            list.Clear();
            _crossingPool.Push(list);
        }

        /// <summary>
        /// Forgets a wire's cached route: its old bounds are remembered as freed room for
        /// <see cref="ImproveRoutes"/>, and the crossings around it are queued for an update.
        /// </summary>
        private void DropRoute(PinRef destination)
        {
            if (_wireIndex.TryGetRect(destination, out Rect old))
            {
                if (_interactive)
                {
                    _interactiveFreed = _hasInteractiveFreed ? Union(_interactiveFreed, old) : old;
                    _hasInteractiveFreed = true;
                }
                else
                {
                    _freedAreas.Add(old);
                }

                MarkCrossingsAround(destination, old);
            }
            else
            {
                DirtyCrossings(destination);
            }

            _wires.Remove(destination);
            _wireIndex.Remove(destination);
            _laneSensitive.Remove(destination);
        }

        private static Rect Union(Rect a, Rect b)
        {
            return Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax),
                Mathf.Max(a.yMax, b.yMax));
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
                DirtyCrossingsNear(newBody);
            }

            if (hadOld)
            {
                InvalidateWiresTouching(oldBody);
                DirtyCrossingsNear(oldBody);
            }
        }

        /// <summary>
        /// Queues a crossing update for every wire within hop reach of a gate body. A hop's side depends on the
        /// gate bodies next to it, so a gate arriving or leaving can flip a hop on a wire that is not rerouted.
        /// </summary>
        private void DirtyCrossingsNear(Rect body)
        {
            if (_wireIndex.Count == 0)
            {
                return;
            }

            _scratchCrossWires.Clear();
            _wireIndex.Query(Inflate(body, WireCrossings.HopReach + QueryPadding), _scratchCrossWires);
            for (int i = 0; i < _scratchCrossWires.Count; i++)
            {
                if (_crossings.ContainsKey(_scratchCrossWires[i]))
                {
                    DirtyCrossings(_scratchCrossWires[i]);
                }
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
            _fastRouted.Remove(destination);
            DropRoute(destination);
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
                    return component.ClockDivider > 1 ? ClockTitle + " ÷" + component.ClockDivider : ClockTitle;
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
                node.Component.DisplayStyle, node.Component.LedColor);
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
                        DropRoute(stale[i]);
                    }
                }
            }
        }

        /// <summary>Orders pins by node, then port (deterministic reroute order).</summary>
        private sealed class PinRefComparer : IComparer<PinRef>
        {
            public static readonly PinRefComparer Instance = new PinRefComparer();

            public int Compare(PinRef x, PinRef y)
            {
                int byNode = x.Node.CompareTo(y.Node);
                return byNode != 0 ? byNode : x.Port.CompareTo(y.Port);
            }
        }
    }
}
