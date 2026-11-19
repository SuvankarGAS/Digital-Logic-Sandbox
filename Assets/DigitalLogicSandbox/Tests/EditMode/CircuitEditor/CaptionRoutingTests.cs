using System.Collections.Generic;
using DigitalLogicSandbox.Components.BuiltIn;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Rendering;
using DigitalLogicSandbox.Tests.EditMode.Support;
using NUnit.Framework;
using UnityEngine;
using static DigitalLogicSandbox.Tests.EditMode.Support.TestCircuits;

namespace DigitalLogicSandbox.Tests.EditMode.CircuitEditor
{
    /// <summary>
    /// Settled wires keep out of part labels (captions drawn above gates). A wire runs straight from A to Y at the
    /// height of a labelled gate's caption. It has to bend around the caption, and runs straight again once the
    /// label is removed.
    /// </summary>
    public sealed class CaptionRoutingTests
    {
        private CircuitDefinition _circuit;
        private CircuitGeometry _geometry;
        private NodeDefinition _gate;
        private PinRef _wire;

        [SetUp]
        public void SetUp()
        {
            var env = new TestEnvironment();
            _circuit = new CircuitDefinition(DefinitionId.New(), "Captions");
            _geometry = new CircuitGeometry(env.Catalog);
            _geometry.Bind(_circuit);
            _circuit.Changed += _geometry.OnChanged;

            _gate = _circuit.AddNode(ComponentRef.Primitive(BuiltInComponentIds.Not), new CircuitPoint(8f, 0f));
            _circuit.SetNodeLabel(_gate.Id, "Data RAM");
            Rect caption = _geometry.GetCaptionBounds(_gate.Id);
            Assert.That(caption.width, Is.GreaterThan(1f), "A labelled gate has a caption area.");
            Assert.That(caption.yMin, Is.GreaterThan(_geometry.GetBounds(_gate.Id).yMax), "Above the body.");

            float lane = Mathf.Round(caption.center.y * 2f) / 2f; // on the half-unit grid, inside the caption
            Assert.That(lane, Is.InRange(caption.yMin, caption.yMax));
            NodeDefinition a = _circuit.AddInputBoundary("A", new CircuitPoint(-4f, lane));
            NodeDefinition y = _circuit.AddOutputBoundary("Y", new CircuitPoint(20f, lane));
            _circuit.Connect(BoundaryOut(a), BoundaryIn(y));
            _wire = BoundaryIn(y);
        }

        [Test]
        public void Wire_BendsAroundAPartLabel()
        {
            IReadOnlyList<Vector3> path = _geometry.GetWirePath(_wire);
            Assert.That(Crosses(path, _geometry.GetCaptionBounds(_gate.Id)), Is.False);
            Assert.That(path.Count, Is.GreaterThan(2), "It cannot run straight through the label.");
            Assert.That(_geometry.RouteFallbackCount, Is.EqualTo(0));
        }

        [Test]
        public void RemovingTheLabel_StraightensTheWire_AndAddingItBackReroutes()
        {
            _geometry.GetWirePath(_wire);
            var invalidated = new List<PinRef>();
            _geometry.DrainInvalidatedWires(invalidated);

            _circuit.SetNodeLabel(_gate.Id, string.Empty);
            _geometry.DrainInvalidatedWires(invalidated);
            Assert.That(invalidated, Does.Contain(_wire), "The wire next to the old caption is rerouted.");
            Assert.That(_geometry.GetWirePath(_wire).Count, Is.EqualTo(2), "Nothing in the way: straight.");

            invalidated.Clear();
            _circuit.SetNodeLabel(_gate.Id, "Data RAM");
            _geometry.DrainInvalidatedWires(invalidated);
            Assert.That(invalidated, Does.Contain(_wire), "The wire under the new caption is rerouted.");
            Assert.That(Crosses(_geometry.GetWirePath(_wire), _geometry.GetCaptionBounds(_gate.Id)), Is.False);
        }

        [Test]
        public void Captions_DoNotChangeHitTestingBounds()
        {
            Rect body = _geometry.GetBounds(_gate.Id);
            Rect footprint = _geometry.GetFootprint(_gate.Id);
            Assert.That(footprint.yMax, Is.GreaterThan(body.yMax));
            Assert.That(footprint.yMin, Is.EqualTo(body.yMin));
            _circuit.SetNodeLabel(_gate.Id, string.Empty);
            Assert.That(_geometry.GetFootprint(_gate.Id), Is.EqualTo(_geometry.GetBounds(_gate.Id)));
        }

        private static bool Crosses(IReadOnlyList<Vector3> path, Rect rect)
        {
            for (int i = 0; i < path.Count - 1; i++)
            {
                Vector2 p = path[i];
                Vector2 q = path[i + 1];
                if (Mathf.Max(p.x, q.x) > rect.xMin + 0.01f && Mathf.Min(p.x, q.x) < rect.xMax - 0.01f &&
                    Mathf.Max(p.y, q.y) > rect.yMin + 0.01f && Mathf.Min(p.y, q.y) < rect.yMax - 0.01f)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
