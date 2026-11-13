using System;
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

                case ComponentKind.InputBoundary:
                    signature = BoundarySignatures.Input;
                    return true;

                case ComponentKind.OutputBoundary:
                    signature = BoundarySignatures.Output;
                    return true;
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
