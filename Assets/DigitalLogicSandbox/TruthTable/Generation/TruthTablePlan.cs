using System.Collections.Generic;
using DigitalLogicSandbox.Core.Validation;
using DigitalLogicSandbox.Simulation.Engine;

namespace DigitalLogicSandbox.TruthTable.Generation
{
    /// <summary>
    /// The outcome of preparing a truth table: the compiled circuit, its columns, the row count and every
    /// reason it cannot (or should not silently) be generated. Preparing never evaluates anything.
    /// </summary>
    public sealed class TruthTablePlan
    {
        private readonly TruthTableColumn[] _inputs;
        private readonly TruthTableColumn[] _outputs;

        internal TruthTablePlan(string title, CompiledCircuit circuit, TruthTableColumn[] inputs,
            TruthTableColumn[] outputs, long rowCount, bool requiresConfirmation, ValidationReport report)
        {
            Title = title ?? string.Empty;
            Circuit = circuit;
            _inputs = inputs ?? System.Array.Empty<TruthTableColumn>();
            _outputs = outputs ?? System.Array.Empty<TruthTableColumn>();
            RowCount = rowCount;
            RequiresConfirmation = requiresConfirmation;
            Report = report ?? new ValidationReport();
        }

        public string Title { get; }

        /// <summary>Null when compilation failed.</summary>
        public CompiledCircuit Circuit { get; }

        public IReadOnlyList<TruthTableColumn> Inputs => _inputs;

        public IReadOnlyList<TruthTableColumn> Outputs => _outputs;

        /// <summary>2^inputs (saturated); may exceed the configured limit, in which case the plan has an error.</summary>
        public long RowCount { get; }

        /// <summary>Compilation diagnostics plus truth-table specific errors and warnings.</summary>
        public ValidationReport Report { get; }

        public bool CanGenerate => Circuit != null && !Report.HasErrors;

        /// <summary>True when the table is large enough that the user should confirm before generating.</summary>
        public bool RequiresConfirmation { get; }

        /// <summary>First error message, or null.</summary>
        public string FirstError
        {
            get
            {
                IReadOnlyList<Diagnostic> diagnostics = Report.Diagnostics;
                for (int i = 0; i < diagnostics.Count; i++)
                {
                    if (diagnostics[i].Severity == DiagnosticSeverity.Error)
                    {
                        return diagnostics[i].Message;
                    }
                }

                return null;
            }
        }

        internal TruthTableColumn[] InputColumns => _inputs;

        internal TruthTableColumn[] OutputColumns => _outputs;
    }
}
