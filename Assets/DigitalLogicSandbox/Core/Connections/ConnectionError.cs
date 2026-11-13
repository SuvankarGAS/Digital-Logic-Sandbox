namespace DigitalLogicSandbox.Core.Connections
{
    /// <summary>Reason a connection request was rejected.</summary>
    public enum ConnectionError
    {
        None = 0,
        CircuitReadOnly,
        MissingSourceNode,
        MissingDestinationNode,
        SelfConnection,
        UnresolvedSourceComponent,
        UnresolvedDestinationComponent,
        InvalidSourcePort,
        InvalidDestinationPort,
        DestinationAlreadyDriven,
    }
}
