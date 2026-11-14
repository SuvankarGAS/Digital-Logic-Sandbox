using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DigitalLogicSandbox.Components.Abstractions;
using DigitalLogicSandbox.Core.Circuit;
using DigitalLogicSandbox.Core.Ports;
using DigitalLogicSandbox.Core.Signals;
using DigitalLogicSandbox.Core.Validation;
using DigitalLogicSandbox.Simulation.Context;
using DigitalLogicSandbox.Simulation.Engine;
using DigitalLogicSandbox.Simulation.Propagation;

namespace DigitalLogicSandbox.TruthTable.Generation
{
    /// <summary>
    /// Explicit stimulus for analysing a sequential circuit: an initial state, then a list of steps. Each step
    /// applies one value per root input, settles, and (optionally) pulses the clock once.
    /// </summary>
    public sealed class SequenceSpec
    {
        public const int DefaultMaxSteps = 1024;

        private readonly List<Signal[]> _steps = new List<Signal[]>();

        public SequenceSpec(int inputCount)
        {
            if (inputCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(inputCount));
            }

            InputCount = inputCount;
        }

        public int InputCount { get; }

        /// <summary>How feedback loops are initialised before step 1. Must be chosen explicitly by the caller.</summary>
        public PowerOnState PowerOnState { get; set; } = PowerOnState.Resolved;

        /// <summary>
        /// When true and the circuit has a clock, every step ends with one full clock cycle: rising edge, settle,
        /// falling edge, settle. Outputs are sampled after the cycle.
        /// </summary>
        public bool PulseClock { get; set; } = true;

        public int StepCount => _steps.Count;

        public IReadOnlyList<Signal[]> Steps => _steps;

        /// <summary>Appends a step. <paramref name="inputs"/> must have one binary value per root input.</summary>
        public void AddStep(params Signal[] inputs)
        {
            if (inputs == null || inputs.Length != InputCount)
            {
                throw new ArgumentException("Expected " + InputCount + " input values.", nameof(inputs));
            }

            _steps.Add((Signal[])inputs.Clone());
        }

        /// <summary>
        /// Builds a spec from one pattern per input (see <see cref="ParsePattern"/>). Each input holds its last
        /// value once its pattern ends; <paramref name="steps"/> defaults to the longest pattern.
        /// </summary>
        public static SequenceSpec FromPatterns(IReadOnlyList<string> patterns, int steps = -1)
        {
            if (patterns == null)
            {
                throw new ArgumentNullException(nameof(patterns));
            }

            var parsed = new List<Signal[]>(patterns.Count);
            int longest = 0;
            for (int i = 0; i < patterns.Count; i++)
            {
                Signal[] values = ParsePattern(patterns[i]);
                parsed.Add(values);
                longest = Math.Max(longest, values.Length);
            }

            int count = steps >= 0 ? steps : Math.Max(1, longest);
            var spec = new SequenceSpec(patterns.Count);
            var row = new Signal[patterns.Count];
            for (int step = 0; step < count; step++)
            {
                for (int i = 0; i < patterns.Count; i++)
                {
                    Signal[] values = parsed[i];
                    row[i] = values.Length == 0 ? Signal.Low : values[Math.Min(step, values.Length - 1)];
                }

                spec.AddStep(row);
            }

            return spec;
        }

        /// <summary>
        /// Parses "0110 1" style patterns: '0' and '1' are values; spaces, commas, '_' and '|' are ignored as
        /// separators. Any other character is an error.
        /// </summary>
        public static Signal[] ParsePattern(string pattern)
        {
            var values = new List<Signal>();
            if (string.IsNullOrEmpty(pattern))
            {
                return values.ToArray();
            }

            for (int i = 0; i < pattern.Length; i++)
            {
                char c = pattern[i];
                switch (c)
                {
                    case '0':
                        values.Add(Signal.Low);
                        break;
                    case '1':
                        values.Add(Signal.High);
                        break;
                    case ' ':
                    case ',':
                    case '_':
                    case '|':
                    case '\t':
                        break;
                    default:
                        throw new FormatException("Invalid character '" + c + "' in pattern \"" + pattern +
                            "\". Use 0 and 1 (spaces and commas are ignored).");
                }
            }

            return values.ToArray();
        }
    }

    /// <summary>Result of a sequence analysis: row 0 is the power-on state, then one row per step.</summary>
    public sealed class SequenceTable
    {
        private readonly List<Signal[]> _inputs = new List<Signal[]>();
        private readonly List<Signal[]> _outputs = new List<Signal[]>();
        private readonly List<bool> _stable = new List<bool>();

        internal SequenceTable(string title, TruthTableColumn[] inputs, TruthTableColumn[] outputs, bool clocked,
            PowerOnState powerOn)
        {
            Title = title ?? string.Empty;
            Inputs = inputs;
            Outputs = outputs;
            Clocked = clocked;
            PowerOnState = powerOn;
        }

        public string Title { get; }

        public IReadOnlyList<TruthTableColumn> Inputs { get; }

        public IReadOnlyList<TruthTableColumn> Outputs { get; }

        /// <summary>True when each step pulsed the clock.</summary>
        public bool Clocked { get; }

        public PowerOnState PowerOnState { get; }

        /// <summary>Rows including the power-on row (index 0).</summary>
        public int RowCount => _outputs.Count;

        public Signal GetInput(int row, int column)
        {
            return _inputs[row][column];
        }

        public Signal GetOutput(int row, int column)
        {
            return _outputs[row][column];
        }

        /// <summary>False when the circuit oscillated during this row (affected outputs read X).</summary>
        public bool IsStable(int row)
        {
            return _stable[row];
        }

        internal void Add(Signal[] inputs, Signal[] outputs, bool stable)
        {
            _inputs.Add(inputs);
            _outputs.Add(outputs);
            _stable.Add(stable);
        }

        /// <summary>Header label of the step column ("Step" or "Cycle").</summary>
        public string StepHeader => Clocked ? "Cycle" : "Step";

        /// <summary>Label of a row: "Power-on" for row 0, otherwise its step number.</summary>
        public string RowLabel(int row)
        {
            return row == 0 ? "Power-on" : row.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        public string ToCsv()
        {
            var writer = new StringWriter();
            Write(writer, ',', quote: true);
            return writer.ToString();
        }

        public string ToTabSeparated()
        {
            var writer = new StringWriter();
            Write(writer, '\t', quote: false);
            return writer.ToString();
        }

        private void Write(TextWriter writer, char separator, bool quote)
        {
            var line = new StringBuilder();
            line.Append(Escape(StepHeader, quote));
            for (int i = 0; i < Inputs.Count; i++)
            {
                line.Append(separator).Append(Escape(Inputs[i].Name, quote));
            }

            for (int i = 0; i < Outputs.Count; i++)
            {
                line.Append(separator).Append(Escape(Outputs[i].Name, quote));
            }

            writer.Write(line.ToString());
            writer.Write("\r\n");
            for (int row = 0; row < RowCount; row++)
            {
                line.Clear();
                line.Append(Escape(RowLabel(row), quote));
                for (int i = 0; i < Inputs.Count; i++)
                {
                    line.Append(separator).Append(row == 0 ? '-' : GetInput(row, i).ToDisplayChar());
                }

                for (int i = 0; i < Outputs.Count; i++)
                {
                    line.Append(separator).Append(GetOutput(row, i).ToDisplayChar());
                }

                writer.Write(line.ToString());
                writer.Write("\r\n");
            }
        }

        private static string Escape(string text, bool quote)
        {
            if (!quote || text.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0)
            {
                return text;
            }

            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }
    }

    /// <summary>
    /// Sequence analysis for sequential circuits (feedback loops and/or clock). Runs the real engine in a fresh,
    /// isolated <see cref="SimulationContext"/>: reset with the spec's power-on state, record row 0, then for each
    /// step apply the inputs, settle, optionally pulse the clock, and record the outputs. The live simulation is
    /// never touched. Unlike a truth table, the result describes this one stimulus, not the circuit in general.
    /// </summary>
    public sealed class SequenceAnalyzer
    {
        private readonly ComponentCatalog _catalog;
        private readonly SimulationOptions _options;

        public SequenceAnalyzer(ComponentCatalog catalog, SimulationOptions options = null)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
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

        public int MaxSteps { get; set; } = SequenceSpec.DefaultMaxSteps;

        /// <summary>Compiles and runs. Returns null and fills <paramref name="report"/> when it cannot run.</summary>
        public SequenceTable Run(CircuitDefinition definition, SequenceSpec spec, ValidationReport report,
            string title = null)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            CompileResult compiled = new CircuitCompiler(_catalog, _options).Compile(definition);
            report?.AddRange(compiled.Report);
            return compiled.Succeeded ? Run(compiled.Circuit, spec, report, title) : null;
        }

        public SequenceTable Run(CompiledCircuit circuit, SequenceSpec spec, ValidationReport report,
            string title = null)
        {
            if (circuit == null)
            {
                throw new ArgumentNullException(nameof(circuit));
            }

            if (spec == null)
            {
                throw new ArgumentNullException(nameof(spec));
            }

            report ??= new ValidationReport();
            CircuitDefinition definition = circuit.RootDefinition;
            if (spec.InputCount != circuit.InputCount)
            {
                report.Add(new Diagnostic(DiagnosticSeverity.Error, DiagnosticCode.InterfaceMismatch,
                    "The sequence defines " + spec.InputCount + " input(s); '" + definition.Name + "' has " +
                    circuit.InputCount + "."));
                return null;
            }

            if (circuit.OutputCount == 0)
            {
                report.Add(new Diagnostic(DiagnosticSeverity.Error, DiagnosticCode.NoOutputs,
                    "'" + definition.Name + "' has no outputs, so there is nothing to observe. Add an output first."));
                return null;
            }

            if (spec.StepCount > MaxSteps)
            {
                report.Add(new Diagnostic(DiagnosticSeverity.Error, DiagnosticCode.TruthTableTooLarge,
                    "The sequence has " + spec.StepCount + " steps; the limit is " + MaxSteps + "."));
                return null;
            }

            bool clocked = spec.PulseClock && circuit.HasClock;
            var table = new SequenceTable(title ?? definition.Name, Columns(definition.Inputs),
                Columns(definition.Outputs), clocked, spec.PowerOnState);

            var options = new SimulationOptions
            {
                MaxHierarchyDepth = _options.MaxHierarchyDepth,
                MinimumEvaluationBudget = _options.MinimumEvaluationBudget,
                EvaluationsPerGate = _options.EvaluationsPerGate,
                RecordChanges = false,
                MaxRecordedChanges = 1,
                PowerOnState = spec.PowerOnState,
            };
            var context = new SimulationContext(circuit, options);
            table.Add(new Signal[circuit.InputCount], ReadOutputs(context), context.LastResult.IsStable);

            int oscillations = context.LastResult.IsStable ? 0 : 1; // the power-on settle counts too
            for (int step = 0; step < spec.StepCount; step++)
            {
                Signal[] inputs = spec.Steps[step];
                for (int i = 0; i < inputs.Length; i++)
                {
                    context.SetInput(i, inputs[i]);
                }

                bool stable = context.Settle().IsStable;
                if (clocked)
                {
                    context.SetClock(Signal.High);
                    stable &= context.Settle().IsStable;
                    context.SetClock(Signal.Low);
                    stable &= context.Settle().IsStable;
                }

                if (!stable)
                {
                    oscillations++;
                }

                table.Add((Signal[])inputs.Clone(), ReadOutputs(context), stable);
            }

            if (oscillations > 0)
            {
                report.Add(new Diagnostic(DiagnosticSeverity.Warning, DiagnosticCode.Oscillation,
                    "The circuit oscillated in " + oscillations + " row(s) (power-on included); affected signals " +
                    "are shown as X."));
            }

            return table;
        }

        private static Signal[] ReadOutputs(SimulationContext context)
        {
            var outputs = new Signal[context.Circuit.OutputCount];
            for (int i = 0; i < outputs.Length; i++)
            {
                outputs[i] = context.GetOutput(i);
            }

            return outputs;
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
    }
}
