using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using DigitalLogicSandbox.Components.Abstractions;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.Core.Validation;
using DigitalLogicSandbox.Simulation.Engine;
using DigitalLogicSandbox.TruthTable.Evaluation;

namespace DigitalLogicSandbox.TruthTable.Generation
{
    /// <summary>
    /// Builds truth tables by running the real simulation engine over every input combination.
    /// <para>
    /// Isolation: each job compiles (or reuses) an immutable <see cref="CompiledCircuit"/> and evaluates it in a
    /// <b>fresh</b> <c>SimulationContext</c> with change recording off. The live editor simulation is never
    /// touched, so there is nothing to capture or restore and the user's input values cannot change.
    /// </para>
    /// <para>
    /// Only combinational circuits are accepted: a feedback loop means the outputs can depend on history, which an
    /// ordinary truth table cannot describe, so such circuits are refused with an explanation.
    /// </para>
    /// </summary>
    public sealed class TruthTableGenerator
    {
        private readonly ComponentCatalog _catalog;
        private readonly SimulationOptions _options;

        public TruthTableGenerator(ComponentCatalog catalog, TruthTableLimits limits = null,
            SimulationOptions options = null)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            Limits = limits ?? new TruthTableLimits();
            SimulationOptions source = options ?? SimulationOptions.Default;
            _options = new SimulationOptions
            {
                MaxHierarchyDepth = source.MaxHierarchyDepth,
                MinimumEvaluationBudget = source.MinimumEvaluationBudget,
                EvaluationsPerGate = source.EvaluationsPerGate,
                RecordChanges = false,
                MaxRecordedChanges = 1,
            };
        }

        public TruthTableLimits Limits { get; }

        /// <summary>Compiles <paramref name="definition"/> and checks it against the limits. Evaluates nothing.</summary>
        public TruthTablePlan Plan(CircuitDefinition definition, string title = null)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            CompileResult compiled = new CircuitCompiler(_catalog, _options).Compile(definition);
            if (!compiled.Succeeded)
            {
                return new TruthTablePlan(title ?? definition.Name, null, Columns(definition.Inputs),
                    Columns(definition.Outputs), TruthTableLimits.RowsFor(definition.Inputs.Count), false,
                    compiled.Report);
            }

            return Plan(compiled.Circuit, title, compiled.Report);
        }

        /// <summary>Plans a table for an already compiled circuit (e.g. the live root, avoiding a recompile).</summary>
        public TruthTablePlan Plan(CompiledCircuit circuit, string title = null)
        {
            return Plan(circuit, title, null);
        }

        /// <summary>Starts a time-sliced job. Drive it with <see cref="TruthTableJob.AdvanceFor"/> or run it to completion.</summary>
        public TruthTableJob Start(TruthTablePlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (!plan.CanGenerate)
            {
                throw new InvalidOperationException("The truth table cannot be generated: " + plan.FirstError);
            }

            return new TruthTableJob(plan, _options);
        }

        /// <summary>
        /// Plans and generates synchronously (tests, export tools). Returns the finished job, or a job that never
        /// started when the plan has errors (<see cref="TruthTableJob.Plan"/> explains why). Confirmation thresholds
        /// do not apply here; the hard limits do.
        /// </summary>
        public TruthTableJob Generate(CircuitDefinition definition, CancellationToken cancellation = default)
        {
            TruthTablePlan plan = Plan(definition);
            if (!plan.CanGenerate)
            {
                return TruthTableJob.Refused(plan);
            }

            TruthTableJob job = Start(plan);
            job.RunToCompletion(cancellation);
            return job;
        }

        private TruthTablePlan Plan(CompiledCircuit circuit, string title, ValidationReport compileReport)
        {
            if (circuit == null)
            {
                throw new ArgumentNullException(nameof(circuit));
            }

            CircuitDefinition definition = circuit.RootDefinition;
            var report = new ValidationReport();
            if (compileReport != null)
            {
                report.AddRange(compileReport);
            }

            TruthTableColumn[] inputs = Columns(definition.Inputs);
            TruthTableColumn[] outputs = Columns(definition.Outputs);
            long rows = TruthTableLimits.RowsFor(inputs.Length);

            if (outputs.Length == 0)
            {
                report.Add(new Diagnostic(DiagnosticSeverity.Error, DiagnosticCode.NoOutputs,
                    "'" + definition.Name + "' has no outputs, so there is nothing to tabulate. Add an output first."));
            }

            if (circuit.HasCombinationalCycles)
            {
                report.Add(new Diagnostic(DiagnosticSeverity.Error, DiagnosticCode.CombinationalCycle,
                    "'" + definition.Name + "' contains a feedback loop. Truth tables describe combinational " +
                    "circuits only: a loop can hold state or oscillate, so its outputs are not a function of the " +
                    "current inputs."));
            }

            if (inputs.Length > Limits.EffectiveMaxInputs)
            {
                report.Add(new Diagnostic(DiagnosticSeverity.Error, DiagnosticCode.TruthTableTooLarge,
                    "'" + definition.Name + "' has " + inputs.Length + " inputs; the limit is " +
                    Limits.EffectiveMaxInputs + " (" + FormatCount(TruthTableLimits.RowsFor(Limits.EffectiveMaxInputs)) +
                    " rows)."));
            }
            else if (rows > Limits.EffectiveMaxRows)
            {
                report.Add(new Diagnostic(DiagnosticSeverity.Error, DiagnosticCode.TruthTableTooLarge,
                    "The table would have " + FormatCount(rows) + " rows; the limit is " +
                    FormatCount(Limits.EffectiveMaxRows) + "."));
            }

            bool confirm = !report.HasErrors && rows > Limits.EffectiveConfirmRows;
            if (confirm)
            {
                report.Add(new Diagnostic(DiagnosticSeverity.Warning, DiagnosticCode.TruthTableTooLarge,
                    "Large table: " + FormatCount(rows) + " rows. Generation runs in the background and can be cancelled."));
            }

            return new TruthTablePlan(title ?? definition.Name, circuit, inputs, outputs, rows, confirm, report);
        }

        private static TruthTableColumn[] Columns(IReadOnlyList<InterfacePort> ports)
        {
            var columns = new TruthTableColumn[ports.Count];
            for (int i = 0; i < ports.Count; i++)
            {
                columns[i] = new TruthTableColumn(ports[i].Id, ports[i].Name);
            }

            return columns;
        }

        /// <summary>"65,536" style (invariant culture).</summary>
        public static string FormatCount(long value)
        {
            return value == long.MaxValue ? "more than 4 quintillion" : value.ToString("N0", CultureInfo.InvariantCulture);
        }
    }
}
