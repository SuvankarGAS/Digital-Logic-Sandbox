using System;

namespace DigitalLogicSandbox.Core.Circuit
{
    /// <summary>
    /// Stable, globally unique identity of a circuit definition. Never derived from the gate name and never
    /// reused; all versions of one gate share the same <see cref="DefinitionId"/>.
    /// </summary>
    public readonly struct DefinitionId : IEquatable<DefinitionId>
    {
        public DefinitionId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Definition id must not be empty.", nameof(value));
            }

            Value = value;
        }

        public string Value { get; }

        public bool IsValid => !string.IsNullOrEmpty(Value);

        public static DefinitionId New()
        {
            return new DefinitionId(Guid.NewGuid().ToString("N"));
        }

        public bool Equals(DefinitionId other)
        {
            return string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is DefinitionId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        }

        public override string ToString()
        {
            return Value ?? "<none>";
        }

        public static bool operator ==(DefinitionId left, DefinitionId right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(DefinitionId left, DefinitionId right)
        {
            return !left.Equals(right);
        }
    }
}
