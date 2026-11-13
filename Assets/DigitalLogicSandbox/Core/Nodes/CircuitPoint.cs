using System;

namespace DigitalLogicSandbox.Core.Nodes
{
    /// <summary>Engine-free 2D position in workspace grid units.</summary>
    public readonly struct CircuitPoint : IEquatable<CircuitPoint>
    {
        public static readonly CircuitPoint Zero = default;

        public CircuitPoint(float x, float y)
        {
            X = x;
            Y = y;
        }

        public float X { get; }

        public float Y { get; }

        public CircuitPoint Offset(float dx, float dy)
        {
            return new CircuitPoint(X + dx, Y + dy);
        }

        public bool Equals(CircuitPoint other)
        {
            return X.Equals(other.X) && Y.Equals(other.Y);
        }

        public override bool Equals(object obj)
        {
            return obj is CircuitPoint other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (X.GetHashCode() * 397) ^ Y.GetHashCode();
            }
        }

        public override string ToString()
        {
            return "(" + X + ", " + Y + ")";
        }

        public static bool operator ==(CircuitPoint left, CircuitPoint right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(CircuitPoint left, CircuitPoint right)
        {
            return !left.Equals(right);
        }
    }
}
