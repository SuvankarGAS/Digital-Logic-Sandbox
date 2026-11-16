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
    /// <summary>
    /// Rip-up and reroute (<see cref="CircuitGeometry.ImproveRoutes"/>). The layout: wire Z runs from B (right, top)
    /// back to W (left, bottom), so its middle lane is a free choice; alone it picks y = 0.5. Wire X wants to run
    /// straight along y = 0.5 through that span. Routed after Z, X has to detour unless Z steps aside.
    /// </summary>
    public sealed class RouteImprovementTests
    {
        private const float Lane = 0.5f;

        private sealed class Layout
        {
            public CircuitDefinition Circuit;
            public CircuitGeometry Geometry;
            public NodeDefinition A, Y, B, W;
            public PinRef X, Z;
        }

        private static Layout Build()
        {
            var env = new TestEnvironment();
            var layout = new Layout { Circuit = new CircuitDefinition(DefinitionId.New(), "Improve") };
            CircuitDefinition circuit = layout.Circuit;
            layout.B = circuit.AddInputBoundary("B", new CircuitPoint(14f, 3f));
            layout.W = circuit.AddOutputBoundary("W", new CircuitPoint(6f, -3f));
            layout.A = circuit.AddInputBoundary("A", new CircuitPoint(-4f, Lane));
            layout.Y = circuit.AddOutputBoundary("Y", new CircuitPoint(24f, Lane));
            layout.X = BoundaryIn(layout.Y);
            layout.Z = BoundaryIn(layout.W);
            layout.Geometry = new CircuitGeometry(env.Catalog);
            layout.Geometry.Bind(circuit);
            circuit.Changed += layout.Geometry.OnChanged;
            return layout;
        }

        private static void ConnectZ(Layout layout)
        {
            layout.Circuit.Connect(BoundaryOut(layout.B), layout.Z);
            layout.Geometry.GetWirePath(layout.Z);
        }

        private static void ConnectX(Layout layout)
        {
            layout.Circuit.Connect(BoundaryOut(layout.A), layout.X);
            layout.Geometry.GetWirePath(layout.X);
        }

        private static float Overlap(Layout layout)
        {
            return GridRouter.OverlapLength(layout.Geometry.GetWirePath(layout.X), layout.Geometry.GetWirePath(layout.Z));
        }

        [Test]
        public void Precondition_ALaterWireDetoursAroundAnEarlierOnesLane()
        {
            Layout layout = Build();
            ConnectZ(layout);
            ConnectX(layout);

            Assert.That(layout.Geometry.GetWirePath(layout.X).Count, Is.GreaterThan(2),
                "Without improvement X cannot run straight: Z already uses its lane.");
        }

        [Test]
        public void EarlierWireStepsAside_SoTheDetouredWireRunsStraight()
        {
            Layout layout = Build();
            ConnectZ(layout);
            ConnectX(layout);
            var changed = new List<PinRef>();

            int improved = layout.Geometry.ImproveRoutes(64, changed);

            Assert.That(improved, Is.GreaterThanOrEqualTo(2));
            Assert.That(changed, Does.Contain(layout.X));
            Assert.That(changed, Does.Contain(layout.Z));
            Assert.That(layout.Geometry.GetWirePath(layout.X).Count, Is.EqualTo(2), "X runs straight.");
            Assert.That(Overlap(layout), Is.EqualTo(0f));
            Assert.That(layout.Geometry.RouteFallbackCount, Is.EqualTo(0));
        }

        [Test]
        public void TheResultDoesNotDependOnConnectionOrder()
        {
            Layout zFirst = Build();
            ConnectZ(zFirst);
            ConnectX(zFirst);
            zFirst.Geometry.ImproveRoutes(64, new List<PinRef>());

            Layout xFirst = Build();
            ConnectX(xFirst);
            ConnectZ(xFirst);
            xFirst.Geometry.ImproveRoutes(64, new List<PinRef>());

            Assert.That(zFirst.Geometry.GetWirePath(zFirst.X).Count, Is.EqualTo(2));
            Assert.That(xFirst.Geometry.GetWirePath(xFirst.X).Count, Is.EqualTo(2));
            Assert.That(Overlap(zFirst), Is.EqualTo(0f));
            Assert.That(Overlap(xFirst), Is.EqualTo(0f));
            Assert.That(PathCost(zFirst, zFirst.Z), Is.EqualTo(PathCost(xFirst, xFirst.Z)).Within(0.01f),
                "Z ends up just as good either way.");
        }

        [Test]
        public void ImprovementConverges_ASecondPassChangesNothing()
        {
            Layout layout = Build();
            ConnectZ(layout);
            ConnectX(layout);
            layout.Geometry.ImproveRoutes(64, new List<PinRef>());

            var changed = new List<PinRef>();
            Assert.That(layout.Geometry.ImproveRoutes(64, changed), Is.EqualTo(0));
            Assert.That(changed, Is.Empty);
        }

        [Test]
        public void ADetouredWireStraightens_WhenTheWireInItsWayIsRemoved()
        {
            Layout layout = Build();
            ConnectZ(layout);
            ConnectX(layout);
            layout.Geometry.ImproveRoutes(0, new List<PinRef>()); // no budget: forget the seeds, keep the detour
            Assume.That(layout.Geometry.GetWirePath(layout.X).Count, Is.GreaterThan(2));

            layout.Circuit.Disconnect(layout.Z);
            var changed = new List<PinRef>();
            layout.Geometry.ImproveRoutes(64, changed);

            Assert.That(changed, Does.Contain(layout.X));
            Assert.That(layout.Geometry.GetWirePath(layout.X).Count, Is.EqualTo(2));
        }

        [Test]
        public void ADetouredWireStraightens_WhenTheGateInItsWayMovesAway()
        {
            Layout layout = Build();
            ConnectZ(layout);
            ConnectX(layout);
            layout.Geometry.ImproveRoutes(0, new List<PinRef>());
            Assume.That(layout.Geometry.GetWirePath(layout.X).Count, Is.GreaterThan(2));

            // Moving W far below reroutes Z (it no longer needs a lane at y = 0.5 next to X).
            layout.Circuit.MoveNode(layout.W.Id, new CircuitPoint(6f, -20f));
            var invalidated = new List<PinRef>();
            layout.Geometry.DrainInvalidatedWires(invalidated);
            foreach (PinRef wire in invalidated)
            {
                layout.Geometry.GetWirePath(wire);
            }

            layout.Geometry.ImproveRoutes(64, new List<PinRef>());

            Assert.That(layout.Geometry.GetWirePath(layout.X).Count, Is.EqualTo(2));
            Assert.That(Overlap(layout), Is.EqualTo(0f));
        }

        [Test]
        public void NoImprovementWhileDragging_ItRunsWhenTheDragSettles()
        {
            Layout layout = Build();
            ConnectZ(layout);
            ConnectX(layout);

            layout.Geometry.InteractiveRouting = true;
            var changed = new List<PinRef>();
            Assert.That(layout.Geometry.ImproveRoutes(64, changed), Is.EqualTo(0));
            Assert.That(layout.Geometry.ImprovementAttempts, Is.EqualTo(0));

            layout.Geometry.InteractiveRouting = false;
            layout.Geometry.ImproveRoutes(64, changed);
            Assert.That(layout.Geometry.GetWirePath(layout.X).Count, Is.EqualTo(2));
        }

        [Test]
        public void UnrelatedWiresAreNotReconsidered()
        {
            // Two parallel wires far apart: neither is shaped by the other, so nothing is searched again.
            var env = new TestEnvironment();
            var circuit = new CircuitDefinition(DefinitionId.New(), "Apart");
            NodeDefinition a = circuit.AddInputBoundary("A", new CircuitPoint(0f, 0f));
            NodeDefinition y = circuit.AddOutputBoundary("Y", new CircuitPoint(12f, 0f));
            NodeDefinition b = circuit.AddInputBoundary("B", new CircuitPoint(0f, 10f));
            NodeDefinition w = circuit.AddOutputBoundary("W", new CircuitPoint(12f, 10f));
            circuit.Connect(BoundaryOut(a), BoundaryIn(y));
            circuit.Connect(BoundaryOut(b), BoundaryIn(w));
            var geometry = new CircuitGeometry(env.Catalog);
            geometry.Bind(circuit);
            geometry.GetWirePath(BoundaryIn(y));
            geometry.GetWirePath(BoundaryIn(w));

            Assert.That(geometry.ImproveRoutes(64, new List<PinRef>()), Is.EqualTo(0));
            Assert.That(geometry.ImprovementAttempts, Is.EqualTo(0));
        }

        [Test]
        public void ADenseGridStaysWithinTheBudget()
        {
            var env = new TestEnvironment();
            var circuit = new CircuitDefinition(DefinitionId.New(), "Dense");
            var wires = new List<PinRef>();
            for (int i = 0; i < 12; i++)
            {
                // Pairs that all fold back across one shared corridor: plenty of lane conflicts.
                NodeDefinition source = circuit.AddInputBoundary("S" + i, new CircuitPoint(14f, 6f - i * 0.5f));
                NodeDefinition target = circuit.AddOutputBoundary("T" + i, new CircuitPoint(-2f + i * 0.25f, -6f - i));
                PinRef destination = BoundaryIn(target);
                circuit.Connect(BoundaryOut(source), destination);
                wires.Add(destination);
            }

            var geometry = new CircuitGeometry(env.Catalog);
            geometry.Bind(circuit);
            foreach (PinRef wire in wires)
            {
                geometry.GetWirePath(wire);
            }

            const int budget = 20;
            geometry.ImproveRoutes(budget, new List<PinRef>());
            Assert.That(geometry.ImprovementAttempts, Is.LessThanOrEqualTo(budget));
        }

        private static float PathCost(Layout layout, PinRef wire)
        {
            return GridRouter.PathCost(layout.Geometry.GetWirePath(wire), new List<Vector4>());
        }
    }
}
