using DigitalLogicSandbox.Core.Nodes;
using DigitalLogicSandbox.Core.Ports;

namespace DigitalLogicSandbox.Core.Circuit
{
    /// <summary>
    /// Resolves a <see cref="ComponentRef"/> to its pin layout (and, for custom components, its definition).
    /// The single abstraction through which the model, validation, simulation and editor learn a component's
    /// shape, so none of them contain gate-specific knowledge.
    /// </summary>
    public interface IComponentResolver : IDefinitionProvider
    {
        bool TryGetSignature(ComponentRef component, out ComponentSignature signature);
    }
}
