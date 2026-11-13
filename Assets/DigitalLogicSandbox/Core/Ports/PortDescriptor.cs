namespace DigitalLogicSandbox.Core.Ports
{
    /// <summary>Describes one pin of a component as seen from outside (id + display name).</summary>
    public readonly struct PortDescriptor
    {
        public PortDescriptor(PortId id, string name)
        {
            Id = id;
            Name = name ?? string.Empty;
        }

        public PortId Id { get; }

        public string Name { get; }

        public override string ToString()
        {
            return Name + "(" + Id + ")";
        }
    }
}
