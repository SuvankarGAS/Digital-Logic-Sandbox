namespace DigitalLogicSandbox.Core.Circuit
{
    /// <summary>Source of published (immutable) circuit definitions, addressed by id and exact version.</summary>
    public interface IDefinitionProvider
    {
        bool TryGetDefinition(DefinitionRef reference, out CircuitDefinition definition);
    }
}
