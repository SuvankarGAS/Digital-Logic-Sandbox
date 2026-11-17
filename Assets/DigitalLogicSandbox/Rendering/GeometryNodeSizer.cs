using DigitalLogicSandbox.Components.Abstractions;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Nodes;
using UnityEngine;

namespace DigitalLogicSandbox.Rendering
{
    /// <summary>
    /// Measures node bodies exactly as the workspace draws them (through <see cref="CircuitGeometry"/>), for code
    /// that lays out generated circuits. Keeps one geometry per circuit, so measuring a whole circuit is cheap.
    /// </summary>
    public sealed class GeometryNodeSizer
    {
        private CircuitGeometry _geometry;
        private ComponentCatalog _catalog;
        private CircuitDefinition _circuit;

        /// <summary>Body size (width, height) of <paramref name="node"/> in world units.</summary>
        public CircuitPoint Measure(ComponentCatalog catalog, CircuitDefinition circuit, NodeDefinition node)
        {
            if (_geometry == null || !ReferenceEquals(catalog, _catalog))
            {
                _catalog = catalog;
                _geometry = new CircuitGeometry(catalog);
                _circuit = null;
            }

            if (!ReferenceEquals(circuit, _circuit))
            {
                _circuit = circuit;
                _geometry.Bind(circuit);
            }

            Vector2 size = _geometry.GetGate(node.Id).Size;
            return new CircuitPoint(size.x, size.y);
        }
    }
}
