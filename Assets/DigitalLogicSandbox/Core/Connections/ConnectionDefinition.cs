using System;

namespace DigitalLogicSandbox.Core.Connections
{
    /// <summary>
    /// A logical link from an output pin (source) to an input pin (destination). Because every input pin has at
    /// most one driver, the destination pin uniquely identifies a connection. Visual routing is stored
    /// separately and never affects simulation.
    /// </summary>
    public readonly struct ConnectionDefinition : IEquatable<ConnectionDefinition>
    {
        public ConnectionDefinition(PinRef source, PinRef destination)
        {
            Source = source;
            Destination = destination;
        }

        public PinRef Source { get; }

        public PinRef Destination { get; }

        public bool Equals(ConnectionDefinition other)
        {
            return Source.Equals(other.Source) && Destination.Equals(other.Destination);
        }

        public override bool Equals(object obj)
        {
            return obj is ConnectionDefinition other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Source.GetHashCode() * 397) ^ Destination.GetHashCode();
            }
        }

        public override string ToString()
        {
            return Source + " -> " + Destination;
        }
    }
}
