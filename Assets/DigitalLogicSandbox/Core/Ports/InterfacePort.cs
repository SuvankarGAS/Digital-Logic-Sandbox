namespace DigitalLogicSandbox.Core.Ports
{
    /// <summary>
    /// One port of a circuit's external interface. Its display order is its index in the owning circuit's
    /// input or output list. Each interface port is backed by exactly one boundary node inside the circuit, whose
    /// width is the port's width.
    /// </summary>
    public sealed class InterfacePort
    {
        internal InterfacePort(PortId id, PortDirection direction, string name, int width = 1)
        {
            Id = id;
            Direction = direction;
            Name = name ?? string.Empty;
            Width = width < 1 ? 1 : width;
        }

        public PortId Id { get; }

        public PortDirection Direction { get; }

        public string Name { get; internal set; }

        /// <summary>Bits carried by the port (1 unless it is a bus).</summary>
        public int Width { get; }

        internal InterfacePort Clone()
        {
            return new InterfacePort(Id, Direction, Name, Width);
        }

        public override string ToString()
        {
            return Direction + " " + Name + (Width > 1 ? "[" + Width + "]" : string.Empty) + "(" + Id + ")";
        }
    }
}
