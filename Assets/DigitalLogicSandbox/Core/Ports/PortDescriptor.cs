namespace DigitalLogicSandbox.Core.Ports
{
    /// <summary>Describes one pin of a component as seen from outside (id, display name and bit width).</summary>
    public readonly struct PortDescriptor
    {
        /// <summary>Widest supported bus, in bits.</summary>
        public const int MaxWidth = 32;

        public PortDescriptor(PortId id, string name, int width = 1)
        {
            Id = id;
            Name = name ?? string.Empty;
            Width = width < 1 ? 1 : width;
        }

        public PortId Id { get; }

        public string Name { get; }

        /// <summary>Number of bits carried by the pin (1 for ordinary single-wire pins).</summary>
        public int Width { get; }

        public bool IsBus => Width > 1;

        public override string ToString()
        {
            return Width > 1 ? Name + "[" + Width + "](" + Id + ")" : Name + "(" + Id + ")";
        }
    }
}
