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
    public sealed class WireCrossingsTests
    {
        private static List<Vector3> Path(params float[] xy)
        {
            var points = new List<Vector3>();
            for (int i = 0; i < xy.Length; i += 2)
            {
                points.Add(new Vector3(xy[i], xy[i + 1]));
            }

            return points;
        }

        [Test]
        public void FindsACrossingOnTheVerticalSegmentOnly()
        {
            List<Vector3> vertical = Path(0f, -4f, 5f, -4f, 5f, 4f, 10f, 4f);
            var horizontal = new List<Vector4> { new Vector4(-2f, 0f, 12f, 0f) };
            var results = new List<Vector2>();

            WireCrossings.Find(vertical, horizontal, results);

            Assert.That(results, Is.EqualTo(new List<Vector2> { new Vector2(5f, 0f) }));

            // The horizontal wire is never decorated: its own segments are not vertical.
            results.Clear();
            WireCrossings.Find(Path(-2f, 0f, 12f, 0f), new List<Vector4> { new Vector4(5f, -4f, 5f, 4f) }, results);
            Assert.That(results, Is.Empty);
        }

        [Test]
        public void IgnoresTouchesNearBendsAndEnds()
        {
            List<Vector3> vertical = Path(5f, -4f, 5f, 4f);
            var others = new List<Vector4>
            {
                new Vector4(5f, 1f, 9f, 1f), // starts on the wire: a T-touch
                new Vector4(0f, 3.9f, 9f, 3.9f), // too close to the segment's end
                new Vector4(4.9f, -1f, 0f, -1f), // ends just short of the wire
            };
            var results = new List<Vector2>();

            WireCrossings.Find(vertical, others, results);

            Assert.That(results, Is.Empty);
        }

        [Test]
        public void ReportsEachCrossingOnce()
        {
            List<Vector3> vertical = Path(5f, -4f, 5f, 4f);
            var others = new List<Vector4> { new Vector4(0f, 0f, 9f, 0f), new Vector4(9f, 0f, 0f, 0f) };
            var results = new List<Vector2>();

            WireCrossings.Find(vertical, others, results);

            Assert.That(results.Count, Is.EqualTo(1));
        }

        private sealed class Cross
        {
            public CircuitDefinition Circuit;
            public CircuitGeometry Geometry;
            public NodeDefinition Y;
            public PinRef H, V;
        }

        /// <summary>H runs straight along y = 0; V comes from above and leaves below, crossing it.</summary>
        private static Cross BuildCross()
        {
            var env = new TestEnvironment();
            var cross = new Cross { Circuit = new CircuitDefinition(DefinitionId.New(), "Cross") };
            NodeDefinition a = cross.Circuit.AddInputBoundary("A", new CircuitPoint(0f, 0f));
            cross.Y = cross.Circuit.AddOutputBoundary("Y", new CircuitPoint(20f, 0f));
            NodeDefinition c = cross.Circuit.AddInputBoundary("C", new CircuitPoint(6f, 4f));
            NodeDefinition d = cross.Circuit.AddOutputBoundary("D", new CircuitPoint(14f, -4f));
            cross.H = BoundaryIn(cross.Y);
            cross.V = BoundaryIn(d);
            cross.Circuit.Connect(BoundaryOut(a), cross.H);
            cross.Circuit.Connect(BoundaryOut(c), cross.V);
            cross.Geometry = new CircuitGeometry(env.Catalog);
            cross.Geometry.Bind(cross.Circuit);
            cross.Circuit.Changed += cross.Geometry.OnChanged;
            cross.Geometry.GetWirePath(cross.H);
            cross.Geometry.GetWirePath(cross.V);
            return cross;
        }

        [Test]
        public void Geometry_TheVerticalWireCarriesTheCrossing()
        {
            Cross cross = BuildCross();

            IReadOnlyList<Vector2> onV = cross.Geometry.GetCrossings(cross.V);
            Assert.That(onV.Count, Is.EqualTo(1));
            Assert.That(onV[0].y, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(cross.Geometry.GetCrossings(cross.H), Is.Empty);
        }

        [Test]
        public void Geometry_FanOutWiresOfOneSourceDoNotCross()
        {
            var env = new TestEnvironment();
            var circuit = new CircuitDefinition(DefinitionId.New(), "Fan");
            NodeDefinition a = circuit.AddInputBoundary("A", new CircuitPoint(0f, 0f));
            NodeDefinition y = circuit.AddOutputBoundary("Y", new CircuitPoint(20f, 0f));
            NodeDefinition d = circuit.AddOutputBoundary("D", new CircuitPoint(14f, -4f));
            circuit.Connect(BoundaryOut(a), BoundaryIn(y));
            circuit.Connect(BoundaryOut(a), BoundaryIn(d));
            var geometry = new CircuitGeometry(env.Catalog);
            geometry.Bind(circuit);

            Assert.That(geometry.GetCrossings(BoundaryIn(y)), Is.Empty);
            Assert.That(geometry.GetCrossings(BoundaryIn(d)), Is.Empty);
        }

        [Test]
        public void Geometry_MovingOneWireQueuesTheOtherWiresCrossings()
        {
            Cross cross = BuildCross();
            cross.Geometry.GetCrossings(cross.V);
            cross.Geometry.GetCrossings(cross.H);
            var queued = new List<PinRef>();
            cross.Geometry.DrainCrossingChanges(queued);

            // Move H's end far below: H no longer crosses V where it used to.
            cross.Circuit.MoveNode(cross.Y.Id, new CircuitPoint(20f, -12f));
            var invalidated = new List<PinRef>();
            cross.Geometry.DrainInvalidatedWires(invalidated);
            foreach (PinRef wire in invalidated)
            {
                cross.Geometry.GetWirePath(wire);
            }

            queued.Clear();
            cross.Geometry.DrainCrossingChanges(queued);
            Assert.That(queued, Does.Contain(cross.V), "V's crossings depend on H's route.");
            Assert.That(queued, Does.Contain(cross.H));
            foreach (Vector2 point in cross.Geometry.GetCrossings(cross.V))
            {
                Assert.That(point.y, Is.Not.EqualTo(0f).Within(1e-3f), "The old crossing is gone.");
            }
        }

        [Test]
        public void Geometry_RemovingAWireQueuesTheWiresItCrossed()
        {
            Cross cross = BuildCross();
            cross.Geometry.GetCrossings(cross.V);
            var queued = new List<PinRef>();
            cross.Geometry.DrainCrossingChanges(queued);

            cross.Circuit.Disconnect(cross.H);
            queued.Clear();
            cross.Geometry.DrainCrossingChanges(queued);

            Assert.That(queued, Does.Contain(cross.V));
            Assert.That(cross.Geometry.GetCrossings(cross.V), Is.Empty);
        }
    }
}
