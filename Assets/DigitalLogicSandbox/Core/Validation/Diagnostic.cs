using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Connections;
using DigitalLogicSandbox.Core.Nodes;

namespace DigitalLogicSandbox.Core.Validation
{
    public enum DiagnosticSeverity : byte
    {
        Info = 0,
        Warning = 1,
        Error = 2,
    }

    public enum DiagnosticCode
    {
        // Components and references
        MissingComponent,
        MissingDefinition,

        // Connections
        InvalidSourcePort,
        InvalidDestinationPort,
        DanglingConnection,
        MultipleDrivers,
        SelfConnection,

        // Interface
        BoundaryPortMismatch,
        DuplicatePortName,
        EmptyPortName,
        NoOutputs,

        // Usage warnings
        UnconnectedInput,
        UnusedInterfaceInput,
        UndrivenInterfaceOutput,

        // Structure
        CombinationalCycle,
        CircularReference,
        HierarchyTooDeep,

        // Packaging
        MissingName,
        ExternalConnection,
        NoInputs,
        DuplicateGateName,
        InterfaceMismatch,
        CircuitReadOnly,
    }

    /// <summary>A single validation finding with enough context for the UI to highlight the culprit.</summary>
    public readonly struct Diagnostic
    {
        public Diagnostic(DiagnosticSeverity severity, DiagnosticCode code, string message, NodeId node = default,
            PinRef pin = default, DefinitionRef definition = default)
        {
            Severity = severity;
            Code = code;
            Message = message ?? string.Empty;
            Node = node;
            Pin = pin;
            Definition = definition;
        }

        public DiagnosticSeverity Severity { get; }

        public DiagnosticCode Code { get; }

        /// <summary>Human-readable, actionable explanation.</summary>
        public string Message { get; }

        /// <summary>Offending node in the validated circuit, if any.</summary>
        public NodeId Node { get; }

        /// <summary>Offending pin, if any (meaningful only when <see cref="Node"/> is valid).</summary>
        public PinRef Pin { get; }

        /// <summary>Offending nested definition, for hierarchy diagnostics.</summary>
        public DefinitionRef Definition { get; }

        public override string ToString()
        {
            return Severity + " " + Code + ": " + Message;
        }
    }
}
