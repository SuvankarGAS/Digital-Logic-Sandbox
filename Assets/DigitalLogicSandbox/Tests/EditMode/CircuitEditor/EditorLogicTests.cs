using System.Collections.Generic;
using DigitalLogicSandbox.CircuitEditor.Interaction;
using DigitalLogicSandbox.CircuitEditor.Placement;
using DigitalLogicSandbox.CircuitEditor.Selection;
using DigitalLogicSandbox.Components.BuiltIn;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.Rendering;
using DigitalLogicSandbox.Rendering.Gates;
using DigitalLogicSandbox.Rendering.Wires;
using DigitalLogicSandbox.Tests.EditMode.Support;
using NUnit.Framework;
using UnityEngine;
using static DigitalLogicSandbox.Tests.EditMode.Support.TestCircuits;

namespace DigitalLogicSandbox.Tests.EditMode.CircuitEditor
{
    public sealed class SelectionModelTests
    {
        [Test]
        public void SelectAddToggleClear()
        {
            var selection = new SelectionModel();
            int changes = 0;
            selection.Changed += () => changes++;

            selection.Select(new NodeId(1));
            selection.Add(new NodeId(2));
            Assert.That(selection.NodeCount, Is.EqualTo(2));
            selection.Toggle(new NodeId(1));
            Assert.That(selection.Contains(new NodeId(1)), Is.False);
            selection.Select(new NodeId(2)); // already the only selection: no event
            selection.Clear();
            Assert.That(selection.IsEmpty, Is.True);
            Assert.That(changes, Is.EqualTo(4));
        }

        [Test]
        public void WireSelection_IsExclusiveWithNodes()
        {
            var selection = new SelectionModel();
            selection.Select(new NodeId(1));
            selection.SelectWire(new PinRef(new NodeId(3), new PortId(0)));
            Assert.That(selection.NodeCount, Is.EqualTo(0));
            Assert.That(selection.Wire.HasValue, Is.True);
            selection.Add(new NodeId(1));
            Assert.That(selection.Wire.HasValue, Is.False);
        }

        [Test]
        public void Prune_RemovesDeletedNodesAndWires()
        {
            CircuitDefinition nand = BuildNand();
            var selection = new SelectionModel();
            selection.SetNodes(new[] { nand.Nodes[2].Id, nand.Nodes[3].Id }, additive: false);
            nand.RemoveNode(nand.Nodes[2].Id);
            selection.Prune(nand);
            Assert.That(selection.NodeCount, Is.EqualTo(1));

            PinRef wire = nand.Connections[0].Destination;
            selection.SelectWire(wire);
            nand.Disconnect(wire);
            selection.Prune(nand);
            Assert.That(selection.IsEmpty, Is.True);
        }

        [Test]
        public void ToSortedArray_IsDeterministic()
        {
            var selection = new SelectionModel();
            selection.SetNodes(new[] { new NodeId(5), new NodeId(2), new NodeId(9) }, additive: false);
            Assert.That(selection.ToSortedArray(), Is.EqualTo(new[] { new NodeId(2), new NodeId(5), new NodeId(9) }));
        }
    }

    public sealed class LayoutAndRoutingTests
    {
        [Test]
        public void GridSnap_UsesHalfUnits()
        {
            Assert.That(GridSnap.Snap(new Vector2(1.26f, -0.74f)), Is.EqualTo(new Vector2(1.5f, -0.5f)));
            Assert.That(GridSnap.ToPoint(new Vector2(0.2f, 0.3f), snap: false), Is.EqualTo(new CircuitPoint(0.2f, 0.3f)));
        }

        [Test]
        public void AndGateLayout_PinsOnHalfGrid_InputsLeftOutputsRight()
        {
            var env = new TestEnvironment();
            env.Catalog.TryGetSignature(And, out ComponentSignature signature);
            GateGeometry geometry = GateLayout.Compute(ComponentKind.Primitive, signature, Rotation.R0, "AND");

            Assert.That(geometry.InputPins.Length, Is.EqualTo(2));
            Assert.That(geometry.OutputPins.Length, Is.EqualTo(1));
            Assert.That(geometry.InputPins[0].x, Is.LessThan(0f));
            Assert.That(geometry.OutputPins[0].x, Is.GreaterThan(0f));
            Assert.That(geometry.InputPins[0].y - geometry.InputPins[1].y, Is.EqualTo(GateLayout.PinSpacing));
            foreach (Vector2 pin in geometry.InputPins)
            {
                Assert.That(pin.y * 2f, Is.EqualTo(Mathf.Round(pin.y * 2f)).Within(1e-5f), "Pins lie on the half grid.");
            }
        }

        [Test]
        public void Rotation_SwapsSizeAndRotatesPins()
        {
            var env = new TestEnvironment();
            env.Catalog.TryGetSignature(Not, out ComponentSignature signature);
            GateGeometry r0 = GateLayout.Compute(ComponentKind.Primitive, signature, Rotation.R0, "NOT");
            GateGeometry r90 = GateLayout.Compute(ComponentKind.Primitive, signature, Rotation.R90, "NOT");

            Assert.That(r90.Size, Is.EqualTo(new Vector2(r0.Size.y, r0.Size.x)));
            Assert.That(r90.OutputPins[0], Is.EqualTo(GateLayout.Rotate(r0.OutputPins[0], Rotation.R90)));
            Assert.That(GateLayout.Next(Rotation.R270), Is.EqualTo(Rotation.R0));
        }

        [Test]
        public void WireRouter_ForwardRouteIsOrthogonal()
        {
            var points = new List<Vector3>();
            WireRouter.Route(new Vector2(0, 0), new Vector2(6, 3), points);

            Assert.That(points[0], Is.EqualTo(new Vector3(0, 0)));
            Assert.That(points[points.Count - 1], Is.EqualTo(new Vector3(6, 3)));
            AssertOrthogonal(points);
        }

        [Test]
        public void WireRouter_StraightWhenAligned()
        {
            var points = new List<Vector3>();
            WireRouter.Route(new Vector2(0, 1), new Vector2(5, 1), points);
            Assert.That(points.Count, Is.EqualTo(2));
        }

        [Test]
        public void WireRouter_BackwardRouteDetoursOrthogonally()
        {
            var points = new List<Vector3>();
            WireRouter.Route(new Vector2(4, 0), new Vector2(0, 0.5f), points);
            Assert.That(points.Count, Is.GreaterThan(3));
            AssertOrthogonal(points);
            Assert.That(points[1].x, Is.GreaterThan(4f), "Leaves the output to the right first.");
        }

        [Test]
        public void DistanceToPath_MeasuresNearestSegment()
        {
            var path = new List<Vector3> { new Vector3(0, 0), new Vector3(4, 0), new Vector3(4, 4) };
            Assert.That(WireRouter.DistanceToPath(path, new Vector2(2, 1)), Is.EqualTo(1f).Within(1e-5f));
            Assert.That(WireRouter.DistanceToPath(path, new Vector2(5, 2)), Is.EqualTo(1f).Within(1e-5f));
        }

        private static void AssertOrthogonal(List<Vector3> points)
        {
            for (int i = 1; i < points.Count; i++)
            {
                bool horizontal = Mathf.Approximately(points[i].y, points[i - 1].y);
                bool vertical = Mathf.Approximately(points[i].x, points[i - 1].x);
                Assert.That(horizontal || vertical, Is.True, "Segment " + i + " is not axis-aligned.");
            }
        }
    }

    public sealed class GeometryAndHitTestTests
    {
        private TestEnvironment _env;
        private CircuitDefinition _nand;
        private CircuitGeometry _geometry;
        private WorkspaceHitTester _hits;

        [SetUp]
        public void SetUp()
        {
            _env = new TestEnvironment();
            _nand = BuildNand();
            _geometry = new CircuitGeometry(_env.Catalog);
            _geometry.Bind(_nand);
            _hits = new WorkspaceHitTester(_geometry);
        }

        [Test]
        public void TryGetPin_ResolvesDirectionAndPosition()
        {
            NodeDefinition and = _nand.Nodes[2];
            Assert.That(_geometry.TryGetPin(new PinRef(and.Id, AndGate.Output), out Vector2 world, out PortDirection dir), Is.True);
            Assert.That(dir, Is.EqualTo(PortDirection.Output));
            Assert.That(world.x, Is.GreaterThan(and.Position.X));
            Assert.That(_geometry.TryGetPin(new PinRef(and.Id, AndGate.InputA), out _, out dir), Is.True);
            Assert.That(dir, Is.EqualTo(PortDirection.Input));
        }

        [Test]
        public void HitTesting_PinsBeforeNodes_ThenWires()
        {
            NodeDefinition and = _nand.Nodes[2];
            _geometry.TryGetPin(new PinRef(and.Id, AndGate.Output), out Vector2 pinWorld, out _);

            Assert.That(_hits.TryHitPin(pinWorld + new Vector2(0.05f, 0f), 0.3f, out PinRef pin, out _), Is.True);
            Assert.That(pin, Is.EqualTo(new PinRef(and.Id, AndGate.Output)));
            Assert.That(_hits.TryHitNode(new Vector2(and.Position.X, and.Position.Y), out NodeId node), Is.True);
            Assert.That(node, Is.EqualTo(and.Id));

            IReadOnlyList<Vector3> path = _geometry.GetWirePath(new PinRef(_nand.Nodes[3].Id, NotGate.Input));
            Vector2 mid = (path[0] + path[path.Count - 1]) * 0.5f;
            Assert.That(_hits.TryHitWire(mid, 0.2f, out PinRef wire), Is.True);
            Assert.That(wire, Is.EqualTo(new PinRef(_nand.Nodes[3].Id, NotGate.Input)));
            Assert.That(_hits.TryHitNode(new Vector2(100, 100), out _), Is.False);
        }

        [Test]
        public void QueryNodes_FindsNodesInRectangle()
        {
            var results = new List<NodeId>();
            _hits.QueryNodes(Rect.MinMaxRect(2f, -1f, 7f, 1f), results);
            Assert.That(results, Is.EquivalentTo(new[] { _nand.Nodes[2].Id, _nand.Nodes[3].Id }));
        }

        [Test]
        public void WirePathCache_InvalidatedWhenEndpointMoves()
        {
            var destination = new PinRef(_nand.Nodes[3].Id, NotGate.Input);
            Vector3 before = _geometry.GetWirePath(destination)[0];
            _nand.Changed += _geometry.OnChanged;
            _nand.MoveNode(_nand.Nodes[2].Id, new CircuitPoint(3, 4));
            Vector3 after = _geometry.GetWirePath(destination)[0];
            Assert.That(after.y, Is.EqualTo(before.y + 4f).Within(1e-5f));
        }
    }

    public sealed class WorkspaceControlsTests
    {
        [Test]
        public void DefaultMap_ContainsEveryAction()
        {
            using (WorkspaceControls controls = WorkspaceControls.CreateDefault())
            {
                Assert.That(controls.Map.name, Is.EqualTo(WorkspaceControls.MapName));
                Assert.That(controls.Undo.bindings.Count, Is.EqualTo(3), "Composite + modifier + key.");
                Assert.That(controls.Delete.bindings.Count, Is.EqualTo(2));
                Assert.That(controls.Point, Is.Not.Null);
            }
        }
    }
}
