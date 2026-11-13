using System;
using DigitalLogicSandbox.Core.Circuit;

namespace DigitalLogicSandbox.Core.Nodes
{
    /// <summary>
    /// What a node instantiates: a primitive (by component id), a specific version of a packaged definition,
    /// or a boundary utility. Behaviour is always resolved through this reference, never through a name.
    /// </summary>
    public readonly struct ComponentRef : IEquatable<ComponentRef>
    {
        private ComponentRef(ComponentKind kind, string primitiveId, DefinitionRef definition)
        {
            Kind = kind;
            PrimitiveId = primitiveId;
            Definition = definition;
        }

        public ComponentKind Kind { get; }

        /// <summary>Component id of a primitive (e.g. "builtin.and"); null for other kinds.</summary>
        public string PrimitiveId { get; }

        /// <summary>Referenced definition version for <see cref="ComponentKind.Custom"/>; default otherwise.</summary>
        public DefinitionRef Definition { get; }

        public bool IsBoundary => Kind == ComponentKind.InputBoundary || Kind == ComponentKind.OutputBoundary;

        public static ComponentRef Primitive(string primitiveId)
        {
            if (string.IsNullOrWhiteSpace(primitiveId))
            {
                throw new ArgumentException("Primitive id must not be empty.", nameof(primitiveId));
            }

            return new ComponentRef(ComponentKind.Primitive, primitiveId, default);
        }

        public static ComponentRef Custom(DefinitionRef definition)
        {
            if (!definition.IsValid)
            {
                throw new ArgumentException("Custom component requires a valid definition reference.", nameof(definition));
            }

            return new ComponentRef(ComponentKind.Custom, null, definition);
        }

        public static ComponentRef InputBoundary => new ComponentRef(ComponentKind.InputBoundary, null, default);

        public static ComponentRef OutputBoundary => new ComponentRef(ComponentKind.OutputBoundary, null, default);

        public bool Equals(ComponentRef other)
        {
            return Kind == other.Kind
                && string.Equals(PrimitiveId, other.PrimitiveId, StringComparison.Ordinal)
                && Definition.Equals(other.Definition);
        }

        public override bool Equals(object obj)
        {
            return obj is ComponentRef other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Kind;
                hash = (hash * 397) ^ (PrimitiveId == null ? 0 : StringComparer.Ordinal.GetHashCode(PrimitiveId));
                hash = (hash * 397) ^ Definition.GetHashCode();
                return hash;
            }
        }

        public override string ToString()
        {
            switch (Kind)
            {
                case ComponentKind.Primitive:
                    return PrimitiveId;
                case ComponentKind.Custom:
                    return "custom:" + Definition;
                default:
                    return Kind.ToString();
            }
        }

        public static bool operator ==(ComponentRef left, ComponentRef right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(ComponentRef left, ComponentRef right)
        {
            return !left.Equals(right);
        }
    }
}
