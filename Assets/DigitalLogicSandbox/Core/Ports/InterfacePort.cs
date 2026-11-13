namespace DigitalLogicSandbox.Core.Ports
{
    /// <summary>
    /// One port of a circuit's external interface. Its display order is its index in the owning circuit's
    /// input or output list. Each interface port is backed by exactly one boundary node inside the circuit.
    /// </summary>
    public sealed class InterfacePort
    {
        internal InterfacePort(PortId id, PortDirection direction, string name)
        {
            Id = id;
            Direction = direction;
            Name = name ?? string.Empty;
        }

        public PortId Id { get; }

        public PortDirection Direction { get; }

        public string Name { get; internal set; }

        internal InterfacePort Clone()
        {
            return new InterfacePort(Id, Direction, Name);
        }

        public override string ToString()
        {
            return Direction + " " + Name + "(" + Id + ")";
        }
    }
}
