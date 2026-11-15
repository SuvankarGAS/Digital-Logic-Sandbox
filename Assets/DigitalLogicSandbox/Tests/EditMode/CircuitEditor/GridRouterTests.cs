using System.Collections.Generic;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Rendering;
using DigitalLogicSandbox.Rendering.Wires;
using DigitalLogicSandbox.Tests.EditMode.Support;
using NUnit.Framework;
using UnityEngine;
using static DigitalLogicSandbox.Tests.EditMode.Support.TestCircuits;

namespace DigitalLogicSandbox.Tests.EditMode.CircuitEditor
{
    public sealed class GridRouterTests
    {
        private static readonly List<Rect> NoRects = new List<Rect>();
        private static readonly List<Vector4> NoLanes = new List<Vector4>();

        private static Rect Area(float xMin, float yMin, float xMax, float yMax)
        {
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        [Test]
        public void StraightWhenFacingAndUnobstructed()
        {
            var points = new List<Vector3>();
            bool routed = GridRouter.TryRoute(new Vector2(0f, 0f), Vector2.right, default, new Vector2(6f, 0f),
                Vector2.left, default, Area(-3f, -3f, 9f, 3f), NoRects, NoLanes, points);

            Assert.That(routed, Is.True);
            Assert.That(points.Count, Is.EqualTo(2));
        }

        [Test]
        public void AvoidsGatesBetweenTheEndpoints()
        {
            // A row of three gates sits between the pins, wider than the candidate router's detour lanes.
            var obstacles = new List<Rect>
            {
                new Rect(3f, -1.5f, 2f, 3f),
                new Rect(6f, -2.5f, 2f, 5f),
                new Rect(9f, -1.5f, 2f, 3f),
            };
            var points = new List<Vector3>();
            bool routed = GridRouter.TryRoute(new Vector2(0f, 0f), Vector2.right, new Rect(-2f, -1f, 2f, 2f),
                new Vector2(14f, 0.5f), Vector2.left, new Rect(14f, -1f, 2f, 2f), Area(-5f, -6f, 19f, 6f), obstacles,
                NoLanes, points);

            Assert.That(routed, Is.True);
            AssertOrthogonal(points);
            Assert.That(points[0], Is.EqualTo(new Vector3(0f, 0f)));
            Assert.That(points[points.Count - 1], Is.EqualTo(new Vector3(14f, 0.5f)));
            foreach (Rect body in obstacles)
            {
                AssertAvoids(points, body, fromSegment: 0);
            }
        }

        [Test]
        public void LeavesAndEntersAlongPinNormals()
        {
            var points = new List<Vector3>();
            bool routed = GridRouter.TryRoute(new Vector2(4f, 0f), Vector2.right, new Rect(1f, -1f, 3f, 2f),
                new Vector2(0f, 3f), Vector2.left, new Rect(0f, 2f, 3f, 2f), Area(-4f, -5f, 8f, 7f), NoRects, NoLanes,
                points);

            Assert.That(routed, Is.True);
            AssertOrthogonal(points);
            Assert.That(points[1].y, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(points[1].x, Is.GreaterThan(4f), "Leaves the output to the right.");
            Assert.That(points[points.Count - 2].y, Is.EqualTo(3f).Within(1e-4f));
            Assert.That(points[points.Count - 2].x, Is.LessThan(0f), "Enters the input from the left.");
        }

        [Test]
        public void KeepsOffAnotherNetsLane()
        {
            // Another net already runs along y = 0 between the pins; the new wire must take a different lane.
            var lanes = new List<Vector4> { new Vector4(1f, 0f, 9f, 0f) };
            var points = new List<Vector3>();
            bool routed = GridRouter.TryRoute(new Vector2(0f, 0f), Vector2.right, default, new Vector2(10f, 1f),
                Vector2.left, default, Area(-3f, -4f, 13f, 4f), NoRects, lanes, points);

            Assert.That(routed, Is.True);
            for (int i = 0; i < points.Count - 1; i++)
            {
                bool horizontal = Mathf.Abs(points[i].y - points[i + 1].y) < 1e-4f;
                float length = Mathf.Abs(points[i + 1].x - points[i].x);
                Assert.That(horizontal && Mathf.Abs(points[i].y) < 1e-4f && length > 1f, Is.False,
                    $"Segment {i} ({points[i]} -> {points[i + 1]}) runs along the other net's lane.");
            }
        }

        [Test]
        public void SameInputGivesSameRoute()
        {
            var obstacles = new List<Rect> { new Rect(4f, -2f, 2f, 4f) };
            var first = new List<Vector3>();
            var second = new List<Vector3>();
            GridRouter.TryRoute(new Vector2(0f, 0f), Vector2.right, default, new Vector2(10f, 1f), Vector2.left,
                default, Area(-3f, -5f, 13f, 5f), obstacles, NoLanes, first);
            GridRouter.TryRoute(new Vector2(0f, 0f), Vector2.right, default, new Vector2(10f, 1f), Vector2.left,
                default, Area(-3f, -5f, 13f, 5f), obstacles, NoLanes, second);

            Assert.That(second, Is.EqualTo(first));
        }

        [Test]
        public void FailsWhenTheDestinationIsWalledIn()
        {
            // The destination's stub end is enclosed by gates on every side within the search area.
            var obstacles = new List<Rect>
            {
                new Rect(7f, 1f, 4f, 2f),
                new Rect(7f, -3f, 4f, 2f),
                new Rect(7f, -1.5f, 1f, 3f),
            };
            var points = new List<Vector3>();
            bool routed = GridRouter.TryRoute(new Vector2(0f, 0f), Vector2.right, default, new Vector2(10f, 0f),
                Vector2.left, new Rect(10f, -1f, 1f, 2f), Area(-2f, -3f, 11f, 3f), obstacles, NoLanes, points);

            Assert.That(routed, Is.False);
            Assert.That(points, Is.Empty);
        }

        [Test]
        public void RoutingAllocatesNoGarbageAfterWarmUp()
        {
            var obstacles = new List<Rect> { new Rect(4f, -2f, 2f, 4f) };
            var lanes = new List<Vector4> { new Vector4(2f, 3f, 8f, 3f) };
            var points = new List<Vector3>(32);
            GridRouter.TryRoute(new Vector2(0f, 0f), Vector2.right, default, new Vector2(10f, 1f), Vector2.left,
                default, Area(-3f, -5f, 13f, 5f), obstacles, lanes, points); // warm up

            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 200; i++)
            {
                GridRouter.TryRoute(new Vector2(0f, i % 3), Vector2.right, default, new Vector2(10f, 1f),
                    Vector2.left, default, Area(-3f, -5f, 13f, 5f), obstacles, lanes, points);
            }

            Assert.That(System.GC.GetAllocatedBytesForCurrentThread() - before, Is.EqualTo(0));
        }

        [Test]
        public void Geometry_InteractiveDragUsesFastRouterThenSettles()
        {
            var env = new TestEnvironment();
            var circuit = new CircuitDefinition(DefinitionId.New(), "Drag");
            NodeDefinition input = circuit.AddInputBoundary("A", new CircuitPoint(0f, 0f));
            NodeDefinition output = circuit.AddOutputBoundary("Y", new CircuitPoint(14f, 0f));
            PinRef destination = BoundaryIn(output);
            circuit.Connect(BoundaryOut(input), destination);
            var geometry = new CircuitGeometry(env.Catalog);
            geometry.Bind(circuit);
            circuit.Changed += geometry.OnChanged;
            geometry.GetWirePath(destination);
            var drained = new List<PinRef>();
            geometry.DrainInvalidatedWires(drained);

            geometry.InteractiveRouting = true;
            circuit.MoveNode(output.Id, new CircuitPoint(14f, 4f));
            geometry.DrainInvalidatedWires(drained);
            Assert.That(geometry.GetWirePath(destination).Count, Is.GreaterThan(2));

            drained.Clear();
            geometry.InteractiveRouting = false;
            geometry.DrainInvalidatedWires(drained);
            Assert.That(drained, Does.Contain(destination), "Ending the drag reroutes the fast-routed wire.");
            IReadOnlyList<Vector3> settled = geometry.GetWirePath(destination);
            Assert.That(settled[0].x, Is.LessThan(settled[settled.Count - 1].x));
            Assert.That(geometry.RouteFallbackCount, Is.EqualTo(0));
        }

        [Test]
        public void Geometry_WireAvoidsEveryGateInARow()
        {
            var env = new TestEnvironment();
            var circuit = new CircuitDefinition(DefinitionId.New(), "Row");
            NodeDefinition input = circuit.AddInputBoundary("A", new CircuitPoint(0f, 0f));
            NodeDefinition output = circuit.AddOutputBoundary("Y", new CircuitPoint(30f, 0f));
            var blockers = new List<NodeDefinition>();
            for (int i = 0; i < 6; i++)
            {
                blockers.Add(circuit.AddNode(Not, new CircuitPoint(5f + i * 4f, (i % 2) * 0.5f)));
            }

            PinRef destination = BoundaryIn(output);
            circuit.Connect(BoundaryOut(input), destination);
            var geometry = new CircuitGeometry(env.Catalog);
            geometry.Bind(circuit);

            var path = new List<Vector3>(geometry.GetWirePath(destination));
            AssertOrthogonal(path);
            foreach (NodeDefinition blocker in blockers)
            {
                AssertAvoids(path, geometry.GetBounds(blocker.Id), fromSegment: 0);
            }
        }

        private static void AssertAvoids(List<Vector3> points, Rect body, int fromSegment)
        {
            for (int i = fromSegment; i < points.Count - 1; i++)
            {
                Assert.That(WireRouter.SegmentCrossesRect(points[i], points[i + 1], body), Is.False,
                    $"Segment {i} ({points[i]} -> {points[i + 1]}) crosses the gate body {body}.");
            }
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
}
