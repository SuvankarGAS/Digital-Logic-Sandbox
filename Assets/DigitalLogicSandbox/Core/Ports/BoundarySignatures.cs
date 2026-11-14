namespace DigitalLogicSandbox.Core.Ports
{
    /// <summary>
    /// Pin layout of the circuit utility components that form a circuit's external interface.
    /// An input boundary drives the circuit through a single output pin; an output boundary observes a single
    /// input pin.
    /// </summary>
    public static class BoundarySignatures
    {
        /// <summary>The single pin of every boundary node.</summary>
        public static readonly PortId Pin = new PortId(0);

        public static readonly ComponentSignature Input =
            new ComponentSignature(null, new[] { new PortDescriptor(Pin, "OUT") });

        public static readonly ComponentSignature Output =
            new ComponentSignature(new[] { new PortDescriptor(Pin, "IN") }, null);

        /// <summary>Clock source: a single output pin carrying the global clock.</summary>
        public static readonly ComponentSignature Clock =
            new ComponentSignature(null, new[] { new PortDescriptor(Pin, "CLK") });
    }
}
