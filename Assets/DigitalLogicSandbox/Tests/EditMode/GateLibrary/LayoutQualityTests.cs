using System;
using System.Collections.Generic;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.GateLibrary.Examples;
using DigitalLogicSandbox.Rendering;
using DigitalLogicSandbox.Rendering.Gates;
using DigitalLogicSandbox.Tests.EditMode.Support;
using NUnit.Framework;
using UnityEngine;

namespace DigitalLogicSandbox.Tests.EditMode.GateLibrary
{
    /// <summary>
    /// The generated example gates stay readable: few crossing wires, compact big gates, deterministic layouts.
    /// Crossings are counted between straight lines from pin to pin, a stable stand-in for the routed wires.
    /// </summary>
    public sealed class LayoutQualityTests
    {
        private TestEnvironment _env;
        private ExamplePack _pack;
        private CircuitGeometry _geometry;

        [SetUp]
        public void SetUp()
        {
            _env = new TestEnvironment();
            _pack = ExampleGatePack.Build(_env.Catalog.Primitives, new GeometryNodeSizer().Measure);
            _geometry = new CircuitGeometry(_pack.Catalog);
        }

        // Before this layout (single-pass placement, bit-level RAM/ROM/ALU/CPU): RAM 3 173 crossings and 191 units
        // tall, ROM 2 859 / 191, ALU 137 / 38, CPU 424, Decoder 4→16 172. Limits are the new results plus a margin.
        [TestCase("ram16x4", 400, 70f)]
        [TestCase("rom16x8", 60, 65f)]
        [TestCase("alu4", 25, 20f)]
        [TestCase("cpu4", 110, 25f)]
        [TestCase("dec4", 172, 50f)]
        [TestCase("register4", 12, 15f)]
        public void BigGates_HaveFewCrossings_AndStayCompact(string key, int maxCrossings, float maxHeight)
        {
            CircuitDefinition gate = _pack.Get(key);
            int crossings = Crossings(gate, out Rect bounds);
            TestContext.WriteLine(gate.Name + ": " + crossings + " crossings, " + bounds.width + " × " + bounds.height);
            Assert.That(crossings, Is.LessThanOrEqualTo(maxCrossings), gate.Name + " crossings");
            Assert.That(bounds.height, Is.LessThanOrEqualTo(maxHeight), gate.Name + " height");
        }

        [Test]
        public void SmallGates_StayNearlyCrossingFree()
        {
            foreach (string key in new[] { "nand", "or", "nor", "xor", "mux2", "mux4", "half-adder", "full-adder",
                         "d-latch", "dff", "counter4" })
            {
                CircuitDefinition gate = _pack.Get(key);
                Assert.That(Crossings(gate, out _), Is.LessThanOrEqualTo(3), gate.Name);
            }
        }

        [Test]
        public void Layout_IsDeterministic()
        {
            ExamplePack again = ExampleGatePack.Build(_env.Catalog.Primitives, new GeometryNodeSizer().Measure);
            for (int d = 0; d < _pack.Definitions.Count; d++)
            {
                IReadOnlyList<NodeDefinition> first = _pack.Definitions[d].Nodes;
                IReadOnlyList<NodeDefinition> second = again.Definitions[d].Nodes;
                for (int i = 0; i < first.Count; i++)
                {
                    Assert.That(second[i].Position, Is.EqualTo(first[i].Position), _pack.Definitions[d].Name);
                }
            }
        }

        [Test]
        public void Constants_SitInTheColumnNextToAUser()
        {
            int checkedConstants = 0;
            foreach (CircuitDefinition gate in _pack.Definitions)
            {
                _geometry.Bind(gate);
                foreach (NodeDefinition node in gate.Nodes)
                {
                    if (node.Component.Kind != ComponentKind.Constant)
                    {
                        continue;
                    }

                    // Columns are the distinct x positions; the nearest user must be in the very next one.
                    var columns = new SortedSet<float>();
                    foreach (NodeDefinition other in gate.Nodes)
                    {
                        columns.Add(other.Position.X);
                    }

                    var users = new List<ConnectionDefinition>();
                    gate.GetConnectionsFrom(node.Id, users);
                    float nearestUser = float.MaxValue;
                    foreach (ConnectionDefinition wire in users)
                    {
                        nearestUser = Mathf.Min(nearestUser, gate.GetNode(wire.Destination.Node).Position.X);
                    }

                    int between = columns.GetViewBetween(node.Position.X, nearestUser).Count - 2;
                    Assert.That(between, Is.EqualTo(0), gate.Name + ": a constant sits right before one of its users.");
                    checkedConstants++;
                }
            }

            Assert.That(checkedConstants, Is.GreaterThan(0));
        }

        [Test]
        public void SignaturePins_MatchTheRenderersPins()
        {
            PinOffset pins = LayeredLayout.SignaturePins(_pack.Catalog);
            foreach (CircuitDefinition gate in _pack.Definitions)
            {
                _geometry.Bind(gate);
                foreach (NodeDefinition node in gate.Nodes)
                {
                    GateGeometry geometry = _geometry.GetGate(node.Id);
                    for (int i = 0; i < geometry.InputPorts.Length; i++)
                    {
                        Assert.That(pins(node, geometry.InputPorts[i]), Is.EqualTo(geometry.InputPins[i].y).Within(1e-4f),
                            gate.Name + " " + node.Component + " input " + i);
                    }

                    for (int i = 0; i < geometry.OutputPorts.Length; i++)
                    {
                        Assert.That(pins(node, geometry.OutputPorts[i]), Is.EqualTo(geometry.OutputPins[i].y).Within(1e-4f),
                            gate.Name + " " + node.Component + " output " + i);
                    }
                }
            }
        }

        [Test]
        public void Wires_KeepOutOfPartLabels()
        {
            int total = 0;
            foreach (CircuitDefinition gate in _pack.Definitions)
            {
                _geometry.Bind(gate);
                int crossings = 0;
                foreach (ConnectionDefinition wire in gate.Connections)
                {
                    IReadOnlyList<Vector3> path = _geometry.GetWirePath(wire.Destination);
                    Assert.That(_geometry.TryGetPin(wire.Source, out Vector2 from, out PortDirection _), Is.True);
                    Assert.That(_geometry.TryGetPin(wire.Destination, out Vector2 to, out PortDirection _), Is.True);
                    Rect fromBody = _geometry.GetBounds(wire.Source.Node);
                    Rect toBody = _geometry.GetBounds(wire.Destination.Node);
                    foreach (NodeDefinition node in gate.Nodes)
                    {
                        Rect caption = _geometry.GetCaptionBounds(node.Id);
                        bool endpoint = node.Id == wire.Source.Node || node.Id == wire.Destination.Node;
                        if (caption.width <= 0f || NearPin(caption, from, to) ||
                            (!endpoint && (caption.Overlaps(fromBody) || caption.Overlaps(toBody))))
                        {
                            continue; // the router may pass these by design (see CircuitGeometry.AddObstacles)
                        }

                        if (PathCrosses(path, caption))
                        {
                            crossings++;
                            TestContext.WriteLine(gate.Name + ": wire to " + wire.Destination + " crosses '" + node.Label + "'");
                        }
                    }
                }

                total += crossings;
            }

            Assert.That(_geometry.RouteFallbackCount, Is.EqualTo(0), "Every wire maze-routed.");
            Assert.That(total, Is.EqualTo(0), "Wires crossing part labels.");
        }

        [Test]
        public void Cpu_ReadsAsAControlBlockAndDatapath_InFewColumns()
        {
            CircuitDefinition cpu = _pack.Get("cpu4");
            var columns = new HashSet<float>();
            foreach (NodeDefinition node in cpu.Nodes)
            {
                if (node.Component.Kind != ComponentKind.InputBoundary && node.Component.Kind != ComponentKind.OutputBoundary)
                {
                    columns.Add(Mathf.Round(node.Position.X * 2f) / 2f);
                }
            }

            Crossings(cpu, out Rect bounds);
            TestContext.WriteLine("CPU: " + columns.Count + " columns, " + bounds.width + " × " + bounds.height);
            Assert.That(Labels(cpu), Does.Contain("Control").And.Not.Contain("Opcode decoder"));
            Assert.That(Labels(_pack.Get("ctrl4")), Does.Contain("Opcode decoder"));
            Assert.That(columns.Count, Is.LessThanOrEqualTo(13), "Columns (was 21).");
            Assert.That(bounds.width, Is.LessThan(135f), "Width (was 151).");
        }

        private static bool NearPin(Rect caption, Vector2 from, Vector2 to)
        {
            Rect clearance = Rect.MinMaxRect(caption.xMin - 0.5f, caption.yMin - 0.5f, caption.xMax + 0.5f, caption.yMax + 0.5f);
            return clearance.Contains(from) || clearance.Contains(to);
        }

        /// <summary>True when any segment of the path runs through the interior of <paramref name="rect"/>.</summary>
        private static bool PathCrosses(IReadOnlyList<Vector3> path, Rect rect)
        {
            const float inset = 0.01f;
            Rect inner = Rect.MinMaxRect(rect.xMin + inset, rect.yMin + inset, rect.xMax - inset, rect.yMax - inset);
            for (int i = 0; i < path.Count - 1; i++)
            {
                Vector2 p = path[i];
                Vector2 q = path[i + 1];
                float xMin = Mathf.Min(p.x, q.x), xMax = Mathf.Max(p.x, q.x);
                float yMin = Mathf.Min(p.y, q.y), yMax = Mathf.Max(p.y, q.y);
                if (xMax > inner.xMin && xMin < inner.xMax && yMax > inner.yMin && yMin < inner.yMax)
                {
                    return true; // axis-aligned segments: bounding-box overlap is an intersection
                }
            }

            return false;
        }

        [Test]
        public void BigGates_NameTheirParts()
        {
            Assert.That(Labels(_pack.Get("cpu4")), Is.SupersetOf(new[] { "PC", "A", "OUT", "Z flag", "C flag", "Data RAM", "ALU" }));
            Assert.That(Labels(_pack.Get("ram16x4")), Does.Contain("Word 0").And.Contain("Word 15").And.Contain("Address decoder"));
            Assert.That(Labels(_pack.Get("alu4")), Does.Contain("B or NOT B"));
            foreach (string label in Labels(_pack.Get("cpu4")))
            {
                Assert.That(label.Length, Is.LessThanOrEqualTo(24), "Fits the inspector's label field.");
            }
        }

        [Test]
        public void ComputePositions_LeavesTheCircuitUnchanged_AndHandlesEmptyCircuits()
        {
            CircuitDefinition alu = _pack.Get("alu4");
            var before = new CircuitPoint[alu.Nodes.Count];
            for (int i = 0; i < before.Length; i++)
            {
                before[i] = alu.Nodes[i].Position;
            }

            CircuitPoint[] computed = LayeredLayout.ComputePositions(alu, node => new CircuitPoint(3f, 2f));
            Assert.That(computed.Length, Is.EqualTo(before.Length));
            for (int i = 0; i < before.Length; i++)
            {
                Assert.That(alu.Nodes[i].Position, Is.EqualTo(before[i]));
            }

            var empty = new CircuitDefinition(DefinitionId.New(), "Empty");
            Assert.That(LayeredLayout.ComputePositions(empty, node => new CircuitPoint(1f, 1f)), Is.Empty);
        }

        private static List<string> Labels(CircuitDefinition gate)
        {
            var labels = new List<string>();
            foreach (NodeDefinition node in gate.Nodes)
            {
                if (!string.IsNullOrEmpty(node.Label))
                {
                    labels.Add(node.Label);
                }
            }

            return labels;
        }

        private int Crossings(CircuitDefinition gate, out Rect bounds)
        {
            _geometry.Bind(gate);
            bounds = default;
            bool first = true;
            foreach (NodeDefinition node in gate.Nodes)
            {
                Rect r = _geometry.GetBounds(node.Id);
                bounds = first ? r : Rect.MinMaxRect(Mathf.Min(bounds.xMin, r.xMin), Mathf.Min(bounds.yMin, r.yMin),
                    Mathf.Max(bounds.xMax, r.xMax), Mathf.Max(bounds.yMax, r.yMax));
                first = false;
            }

            var lines = new List<(Vector2 a, Vector2 b)>();
            foreach (ConnectionDefinition wire in gate.Connections)
            {
                Assert.That(_geometry.TryGetPin(wire.Source, out Vector2 a, out PortDirection _), Is.True);
                Assert.That(_geometry.TryGetPin(wire.Destination, out Vector2 b, out PortDirection _), Is.True);
                lines.Add((a, b));
            }

            int count = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                for (int j = i + 1; j < lines.Count; j++)
                {
                    if (ProperlyIntersect(lines[i].a, lines[i].b, lines[j].a, lines[j].b))
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private static bool ProperlyIntersect(Vector2 p1, Vector2 p2, Vector2 q1, Vector2 q2)
        {
            float d1 = Cross(q1, q2, p1);
            float d2 = Cross(q1, q2, p2);
            float d3 = Cross(p1, p2, q1);
            float d4 = Cross(p1, p2, q2);
            return ((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) && ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f));
        }

        private static float Cross(Vector2 a, Vector2 b, Vector2 p)
        {
            return (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
        }
    }
}
