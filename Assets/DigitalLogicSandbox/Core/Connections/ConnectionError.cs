namespace DigitalLogicSandbox.Core.Connections
{
    /// <summary>Reason a connection request was rejected.</summary>
    public enum ConnectionError
    {
        None = 0,
        CircuitReadOnly,
        MissingSourceNode,
        MissingDestinationNode,
        /// <summary>Obsolete since Phase B: self-connections are allowed as one-node feedback loops.</summary>
        SelfConnection,
        UnresolvedSourceComponent,
        UnresolvedDestinationComponent,
        InvalidSourcePort,
        InvalidDestinationPort,
        DestinationAlreadyDriven,

        /// <summary>The pins carry different numbers of bits (e.g. a 4-bit bus into a 1-bit input).</summary>
        WidthMismatch,
    }
}
