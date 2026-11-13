using System;
using System.Collections.Generic;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.Rendering;
using DigitalLogicSandbox.Rendering.Gates;
using DigitalLogicSandbox.Rendering.Wires;
using UnityEngine;

namespace DigitalLogicSandbox.CircuitEditor.Interaction
{
    /// <summary>
    /// Model-based picking against <see cref="CircuitGeometry"/> (no colliders or physics). Priority is pins first,
    /// then gate bodies, then wires. Later nodes are drawn on top, so they are tested first.
    /// </summary>
    public sealed class WorkspaceHitTester
    {
        private readonly CircuitGeometry _geometry;

        public WorkspaceHitTester(CircuitGeometry geometry)
        {
            _geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
        }

        public CircuitGeometry Geometry => _geometry;

        private CircuitDefinition Circuit => _geometry.Circuit;

        /// <summary>Nearest pin within <paramref name="radius"/> of <paramref name="world"/>.</summary>
        public bool TryHitPin(Vector2 world, float radius, out PinRef pin, out PortDirection direction)
        {
            pin = default;
            direction = PortDirection.Input;
            if (Circuit == null)
            {
                return false;
            }

            float best = radius * radius;
            bool found = false;
            IReadOnlyList<NodeDefinition> nodes = Circuit.Nodes;
            for (int n = nodes.Count - 1; n >= 0; n--)
            {
                NodeDefinition node = nodes[n];
                Rect bounds = _geometry.GetBounds(node.Id);
                if (!Expand(bounds, radius).Contains(world))
                {
                    continue;
                }

                GateGeometry geometry = _geometry.GetGate(node.Id);
                Vector2 center = _geometry.GetPosition(node.Id);
                for (int i = 0; i < geometry.InputPins.Length; i++)
                {
                    float d = (center + geometry.InputPins[i] - world).sqrMagnitude;
                    if (d <= best)
                    {
                        best = d;
                        found = true;
                        pin = new PinRef(node.Id, geometry.InputPorts[i]);
                        direction = PortDirection.Input;
                    }
                }

                for (int i = 0; i < geometry.OutputPins.Length; i++)
                {
                    float d = (center + geometry.OutputPins[i] - world).sqrMagnitude;
                    if (d <= best)
                    {
                        best = d;
                        found = true;
                        pin = new PinRef(node.Id, geometry.OutputPorts[i]);
                        direction = PortDirection.Output;
                    }
                }
            }

            return found;
        }

        /// <summary>Topmost node whose body contains <paramref name="world"/>.</summary>
        public bool TryHitNode(Vector2 world, out NodeId node)
        {
            node = NodeId.None;
            if (Circuit == null)
            {
                return false;
            }

            IReadOnlyList<NodeDefinition> nodes = Circuit.Nodes;
            for (int n = nodes.Count - 1; n >= 0; n--)
            {
                if (_geometry.GetBounds(nodes[n].Id).Contains(world))
                {
                    node = nodes[n].Id;
                    return true;
                }
            }

            return false;
        }

        /// <summary>Closest wire within <paramref name="tolerance"/>; returns its destination pin.</summary>
        public bool TryHitWire(Vector2 world, float tolerance, out PinRef destination)
        {
            destination = default;
            if (Circuit == null)
            {
                return false;
            }

            float best = tolerance;
            bool found = false;
            IReadOnlyList<ConnectionDefinition> connections = Circuit.Connections;
            for (int i = 0; i < connections.Count; i++)
            {
                PinRef candidate = connections[i].Destination;
                float d = WireRouter.DistanceToPath(_geometry.GetWirePath(candidate), world);
                if (d <= best)
                {
                    best = d;
                    destination = candidate;
                    found = true;
                }
            }

            return found;
        }

        /// <summary>Appends nodes whose bodies overlap <paramref name="area"/>.</summary>
        public void QueryNodes(Rect area, List<NodeId> results)
        {
            if (Circuit == null)
            {
                return;
            }

            IReadOnlyList<NodeDefinition> nodes = Circuit.Nodes;
            for (int n = 0; n < nodes.Count; n++)
            {
                if (_geometry.GetBounds(nodes[n].Id).Overlaps(area))
                {
                    results.Add(nodes[n].Id);
                }
            }
        }

        /// <summary>Bounds of every node (for framing); false when the circuit is empty.</summary>
        public bool TryGetContentBounds(out Rect bounds)
        {
            bounds = default;
            if (Circuit == null || Circuit.Nodes.Count == 0)
            {
                return false;
            }

            IReadOnlyList<NodeDefinition> nodes = Circuit.Nodes;
            bounds = _geometry.GetBounds(nodes[0].Id);
            for (int n = 1; n < nodes.Count; n++)
            {
                Rect r = _geometry.GetBounds(nodes[n].Id);
                bounds = Rect.MinMaxRect(Mathf.Min(bounds.xMin, r.xMin), Mathf.Min(bounds.yMin, r.yMin),
                    Mathf.Max(bounds.xMax, r.xMax), Mathf.Max(bounds.yMax, r.yMax));
            }

            return true;
        }

        private static Rect Expand(Rect rect, float amount)
        {
            return new Rect(rect.x - amount, rect.y - amount, rect.width + amount * 2f, rect.height + amount * 2f);
        }
    }
}
