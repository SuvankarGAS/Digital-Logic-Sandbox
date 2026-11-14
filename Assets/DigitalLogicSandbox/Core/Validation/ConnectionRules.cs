using System;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;

namespace DigitalLogicSandbox.Core.Validation
{
    /// <summary>
    /// Full connection compatibility check used by the editor before issuing a connect command: structural
    /// invariants plus pin existence and direction against component signatures. Feedback loops are allowed
    /// here on purpose (future latches need them) and are reported by <see cref="CircuitValidator"/> instead.
    /// </summary>
    public static class ConnectionRules
    {
        public static ConnectionError Check(CircuitDefinition circuit, IComponentResolver resolver, PinRef source,
            PinRef destination)
        {
            if (circuit == null)
            {
                throw new ArgumentNullException(nameof(circuit));
            }

            if (resolver == null)
            {
                throw new ArgumentNullException(nameof(resolver));
            }

            if (circuit.IsReadOnly)
            {
                return ConnectionError.CircuitReadOnly;
            }

            if (!circuit.TryGetNode(source.Node, out NodeDefinition sourceNode))
            {
                return ConnectionError.MissingSourceNode;
            }

            if (!circuit.TryGetNode(destination.Node, out NodeDefinition destinationNode))
            {
                return ConnectionError.MissingDestinationNode;
            }

            if (!resolver.TryGetSignature(sourceNode.Component, out ComponentSignature sourceSignature))
            {
                return ConnectionError.UnresolvedSourceComponent;
            }

            if (!resolver.TryGetSignature(destinationNode.Component, out ComponentSignature destinationSignature))
            {
                return ConnectionError.UnresolvedDestinationComponent;
            }

            if (!sourceSignature.HasOutput(source.Port))
            {
                return ConnectionError.InvalidSourcePort;
            }

            if (!destinationSignature.HasInput(destination.Port))
            {
                return ConnectionError.InvalidDestinationPort;
            }

            if (circuit.IsDriven(destination))
            {
                return ConnectionError.DestinationAlreadyDriven;
            }

            return ConnectionError.None;
        }

        /// <summary>Checks and, if valid, connects. Returns the failure reason otherwise.</summary>
        public static ConnectionError TryConnect(CircuitDefinition circuit, IComponentResolver resolver, PinRef source,
            PinRef destination)
        {
            ConnectionError error = Check(circuit, resolver, source, destination);
            return error != ConnectionError.None ? error : circuit.TryConnect(source, destination);
        }
    }
}
