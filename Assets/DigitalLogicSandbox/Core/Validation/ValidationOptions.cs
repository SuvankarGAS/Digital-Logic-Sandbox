namespace DigitalLogicSandbox.Core.Validation
{
    public sealed class ValidationOptions
    {
        public const int DefaultMaxHierarchyDepth = 32;

        public static ValidationOptions Default => new ValidationOptions();

        /// <summary>
        /// Maximum custom-gate nesting depth. The root circuit is depth 0; a custom gate placed in it is depth 1.
        /// </summary>
        public int MaxHierarchyDepth { get; set; } = DefaultMaxHierarchyDepth;

        /// <summary>Report gate inputs with no driver (they read LOW) as warnings.</summary>
        public bool WarnUnconnectedInputs { get; set; } = true;

        /// <summary>Report interface inputs that drive nothing as warnings (intentional unused inputs are allowed).</summary>
        public bool WarnUnusedInterfaceInputs { get; set; } = true;

        /// <summary>Require at least one interface output (packaging sets this).</summary>
        public bool RequireOutputs { get; set; }

        /// <summary>Walk referenced custom definitions for cycles and depth.</summary>
        public bool ValidateHierarchy { get; set; } = true;
    }
}
