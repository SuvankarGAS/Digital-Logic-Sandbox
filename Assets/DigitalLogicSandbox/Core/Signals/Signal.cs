namespace DigitalLogicSandbox.Core.Signals
{
    /// <summary>
    /// Logical value carried by a net. Stored as a byte so signal arrays stay compact.
    /// Only <see cref="Low"/> and <see cref="High"/> are driven by user inputs initially; the remaining
    /// states exist so tri-state drivers and uninitialised analysis can be added without data migration.
    /// </summary>
    public enum Signal : byte
    {
        Low = 0,
        High = 1,
        Unknown = 2,
        HighImpedance = 3,
    }
}
