using System;

namespace DigitalLogicSandbox.Core.Circuit
{
    /// <summary>
    /// Reference to one immutable, published version of a circuit definition. Instances pin an exact version
    /// so editing a gate never silently changes circuits that use it.
    /// </summary>
    public readonly struct DefinitionRef : IEquatable<DefinitionRef>
    {
        public DefinitionRef(DefinitionId id, int version)
        {
            if (!id.IsValid)
            {
                throw new ArgumentException("Definition reference requires a valid id.", nameof(id));
            }

            if (version < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(version), version, "Versions start at 1.");
            }

            Id = id;
            Version = version;
        }

        public DefinitionId Id { get; }

        public int Version { get; }

        public bool IsValid => Id.IsValid && Version >= 1;

        public bool Equals(DefinitionRef other)
        {
            return Id.Equals(other.Id) && Version == other.Version;
        }

        public override bool Equals(object obj)
        {
            return obj is DefinitionRef other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Id.GetHashCode() * 397) ^ Version;
            }
        }

        public override string ToString()
        {
            return Id + "@v" + Version;
        }

        public static bool operator ==(DefinitionRef left, DefinitionRef right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(DefinitionRef left, DefinitionRef right)
        {
            return !left.Equals(right);
        }
    }
}
