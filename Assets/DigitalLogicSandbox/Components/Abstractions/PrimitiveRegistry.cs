using System;
using System.Collections.Generic;
using DigitalLogicSandbox.Components.BuiltIn;

namespace DigitalLogicSandbox.Components.Abstractions
{
    /// <summary>Lookup of built-in primitives by persisted component id.</summary>
    public sealed class PrimitiveRegistry
    {
        private readonly Dictionary<string, IPrimitiveComponent> _byId =
            new Dictionary<string, IPrimitiveComponent>(StringComparer.Ordinal);

        private readonly List<IPrimitiveComponent> _ordered = new List<IPrimitiveComponent>();

        /// <summary>Primitives in registration order (library display order).</summary>
        public IReadOnlyList<IPrimitiveComponent> All => _ordered;

        /// <summary>The application's built-in set: exactly AND and NOT.</summary>
        public static PrimitiveRegistry CreateDefault()
        {
            var registry = new PrimitiveRegistry();
            registry.Register(AndGate.Instance);
            registry.Register(NotGate.Instance);
            return registry;
        }

        public void Register(IPrimitiveComponent component)
        {
            if (component == null)
            {
                throw new ArgumentNullException(nameof(component));
            }

            if (_byId.ContainsKey(component.Id))
            {
                throw new InvalidOperationException("A primitive with id '" + component.Id + "' is already registered.");
            }

            _byId.Add(component.Id, component);
            _ordered.Add(component);
        }

        public bool TryGet(string id, out IPrimitiveComponent component)
        {
            if (id == null)
            {
                component = null;
                return false;
            }

            return _byId.TryGetValue(id, out component);
        }
    }
}
