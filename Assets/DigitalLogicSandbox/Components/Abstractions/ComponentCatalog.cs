using System;
using DigitalLogicSandbox.Components.BuiltIn;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;

namespace DigitalLogicSandbox.Components.Abstractions
{
    /// <summary>
    /// The application's <see cref="IComponentResolver"/>: primitives come from the registry, custom gates from a
    /// definition provider (the gate library), boundaries have fixed signatures. Kind dispatch happens here once,
    /// so no other system needs to know which kinds of components exist.
    /// </summary>
    public sealed class ComponentCatalog : IComponentResolver
    {
        private readonly PrimitiveRegistry _primitives;
        private readonly IDefinitionProvider _definitions;

        public ComponentCatalog(PrimitiveRegistry primitives, IDefinitionProvider definitions)
        {
            _primitives = primitives ?? throw new ArgumentNullException(nameof(primitives));
            _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        }

        public PrimitiveRegistry Primitives => _primitives;

        public bool TryGetSignature(ComponentRef component, out ComponentSignature signature)
        {
            switch (component.Kind)
            {
                case ComponentKind.Primitive:
                    if (_primitives.TryGet(component.PrimitiveId, out IPrimitiveComponent primitive))
                    {
                        signature = primitive.Signature;
                        return true;
                    }

                    break;

                case ComponentKind.Custom:
                    if (component.Definition.IsValid &&
                        _definitions.TryGetDefinition(component.Definition, out CircuitDefinition definition))
                    {
                        signature = definition.GetSignature();
                        return true;
                    }

                    break;

                default:
                    // Boundaries, clock and bus wiring: fixed layouts parameterised by width/arity.
                    signature = BoundarySignatures.ForUtility(component);
                    return signature != null;
            }

            signature = null;
            return false;
        }

        public bool TryGetPrimitive(ComponentRef component, out IPrimitiveComponent primitive)
        {
            if (component.Kind == ComponentKind.Primitive)
            {
                return _primitives.TryGet(component.PrimitiveId, out primitive);
            }

            if (component.Kind == ComponentKind.TriState)
            {
                // Evaluated bit by bit; the compiler slices bus pins into one evaluator per bit.
                primitive = TriStateBuffer.Instance;
                return true;
            }

            primitive = null;
            return false;
        }

        public bool TryGetDefinition(DefinitionRef reference, out CircuitDefinition definition)
        {
            if (!reference.IsValid)
            {
                definition = null;
                return false;
            }

            return _definitions.TryGetDefinition(reference, out definition);
        }
    }
}
