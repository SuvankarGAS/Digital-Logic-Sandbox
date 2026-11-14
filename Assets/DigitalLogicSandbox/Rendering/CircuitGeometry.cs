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
    /// </summary>
    public sealed class CircuitGeometry
    {
        private readonly ComponentCatalog _catalog;
        private readonly Dictionary<NodeId, GateGeometry> _gates = new Dictionary<NodeId, GateGeometry>();
        private readonly Dictionary<PinRef, List<Vector3>> _wires = new Dictionary<PinRef, List<Vector3>>();
        private readonly List<ConnectionDefinition> _scratchConnections = new List<ConnectionDefinition>();

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
        }

        /// <summary>Invalidates caches affected by a model change.</summary>
        public void OnChanged(CircuitChange change)
        {
            switch (change.Kind)
            {
                case CircuitChangeKind.NodeRemoved:
                    _gates.Remove(change.Node);
                    InvalidateWiresOf(change.Node);
                    break;
                case CircuitChangeKind.NodeMoved:
                    InvalidateWiresOf(change.Node);
                    break;
                case CircuitChangeKind.NodeRotated:
                case CircuitChangeKind.InterfacePortRenamed:
                    _gates.Remove(change.Node);
                    InvalidateWiresOf(change.Node);
                    break;
                case CircuitChangeKind.Connected:
                case CircuitChangeKind.Disconnected:
                    _wires.Remove(change.Connection.Destination);
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
            return GateLayout.Compute(component.Kind, signature, Rotation.R0, title, icon);
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
                WireRouter.Route(from, GetPinNormal(connection.Source.Node, PortDirection.Output),
                    GetBounds(connection.Source.Node), to, GetPinNormal(destination.Node, PortDirection.Input),
                    GetBounds(destination.Node), path);
            }

            _wires[destination] = path;
            return path;
        }

        /// <summary>
        /// Outward normal of a node's pins on one side: outputs face right and inputs face left before rotation.
        /// </summary>
        public Vector2 GetPinNormal(NodeId node, PortDirection direction)
        {
            Vector2 local = direction == PortDirection.Output ? Vector2.right : Vector2.left;
            return GateLayout.Rotate(local, GetGate(node).Rotation);
        }

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
                default:
                    return "Output";
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

            return GateLayout.Compute(node.Component.Kind, signature, node.Rotation, TitleOf(node), icon);
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
                _wires.Remove(_scratchConnections[i].Destination);
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
                    }
                }
            }
        }
    }
}
